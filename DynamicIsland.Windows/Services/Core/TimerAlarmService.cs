using System.Media;
using System.Text.Json;
using System.Windows.Threading;
using DynamicIsland.Windows.Models;

namespace DynamicIsland.Windows.Services;

public sealed class TimerAlarmEventArgs(string name, string title, string message) : EventArgs
{
    public string Name { get; } = name;
    public string Title { get; } = title;
    public string Message { get; } = message;
}

public sealed class TimerAlarmService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly TimeSpan AlarmTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan TimerDoneVisibility = TimeSpan.FromSeconds(5);
    private readonly LoggingService _log;
    private readonly DispatcherTimer _tickTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(200)
    };
    private readonly string _statePath;
    private System.Threading.Timer? _soundTimer;

    public event EventHandler? Changed;
    public event EventHandler<TimerAlarmEventArgs>? EventRaised;
    public TimerAlarmSnapshot State { get; private set; } = new();

    public TimerAlarmService(LoggingService log)
    {
        _log = log;
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DynamicIsland.Windows");
        Directory.CreateDirectory(directory);
        _statePath = Path.Combine(directory, "timer-alarm.json");
        Load();
        _tickTimer.Tick += (_, _) => Tick();
    }

    public void Start() => _tickTimer.Start();

    public TimeSpan TimerRemaining
    {
        get
        {
            var timer = State.Timer;
            if (timer.Phase == TimerPhase.Paused)
                return TimeSpan.FromSeconds(Math.Max(0, timer.PausedRemainingSeconds));
            if (timer.Phase != TimerPhase.Running || timer.StartedAt is null)
                return timer.Phase == TimerPhase.Completed
                    ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Max(0, timer.TotalSeconds));
            var elapsed = (DateTimeOffset.Now - timer.StartedAt.Value).TotalSeconds + timer.AccumulatedSeconds;
            return TimeSpan.FromSeconds(Math.Max(0, timer.TotalSeconds - elapsed));
        }
    }

    public double TimerProgress => State.Timer.TotalSeconds <= 0 ? 0 :
        Math.Clamp(1 - TimerRemaining.TotalSeconds / State.Timer.TotalSeconds, 0, 1);

    public void StartTimer(TimeSpan duration, string? label = null)
    {
        StopSound();
        State.Timer = new TimerState
        {
            Phase = TimerPhase.Running,
            Label = label?.Trim() ?? string.Empty,
            TotalSeconds = Math.Max(1, duration.TotalSeconds),
            StartedAt = DateTimeOffset.Now
        };
        SaveAndNotify();
    }

    public void PauseTimer()
    {
        var timer = State.Timer;
        if (timer.Phase != TimerPhase.Running || timer.StartedAt is null) return;
        timer.AccumulatedSeconds += (DateTimeOffset.Now - timer.StartedAt.Value).TotalSeconds;
        timer.PausedRemainingSeconds = Math.Max(0, timer.TotalSeconds - timer.AccumulatedSeconds);
        timer.StartedAt = null;
        timer.Phase = TimerPhase.Paused;
        SaveAndNotify();
    }

    public void ResumeTimer()
    {
        var timer = State.Timer;
        if (timer.Phase != TimerPhase.Paused) return;
        timer.StartedAt = DateTimeOffset.Now;
        timer.Phase = TimerPhase.Running;
        SaveAndNotify();
    }

    public void ResetTimer()
    {
        StopSound();
        var previous = State.Timer;
        State.Timer = new TimerState { Label = previous.Label, TotalSeconds = previous.TotalSeconds };
        SaveAndNotify();
    }

    public void CancelTimer()
    {
        StopSound();
        State.Timer = new TimerState();
        SaveAndNotify();
    }

    public void AcknowledgeTimer()
    {
        StopSound();
        State.Timer.CompletionAcknowledged = true;
        SaveAndNotify();
    }

    public void SetAlarm(int hour, int minute, bool use24Hour, string? label = null, AlarmRepeat repeat = AlarmRepeat.Once)
    {
        StopSound();
        var h = Math.Clamp(hour, 0, 23);
        var m = Math.Clamp(minute, 0, 59);
        // Weekly anchors on the weekday of the first occurrence, so it repeats on that same day each week.
        var firstDaily = NextOccurrence(h, m, AlarmRepeat.Daily, null, DateTimeOffset.Now);
        int? anchor = repeat == AlarmRepeat.Weekly ? (int)firstDaily.DayOfWeek : null;
        var target = NextOccurrence(h, m, repeat, anchor, DateTimeOffset.Now);
        State.Alarm = new AlarmState
        {
            Phase = AlarmPhase.Scheduled,
            Hour = h,
            Minute = m,
            Use24Hour = use24Hour,
            Label = label?.Trim() ?? string.Empty,
            Repeat = repeat,
            RepeatAnchorDayOfWeek = anchor,
            TargetAt = target
        };
        SaveAndNotify();
    }

    // Next date/time strictly after <paramref name="after"/> matching the repeat rule.
    private static DateTimeOffset NextOccurrence(int hour, int minute, AlarmRepeat repeat, int? anchorDow, DateTimeOffset after)
    {
        var candidate = new DateTimeOffset(after.Year, after.Month, after.Day, hour, minute, 0, after.Offset);
        while (candidate <= after
               || (repeat == AlarmRepeat.Weekdays && candidate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
               || (repeat == AlarmRepeat.Weekly && anchorDow is int dow && (int)candidate.DayOfWeek != dow))
            candidate = candidate.AddDays(1);
        return candidate;
    }

    // Advances a repeating alarm to its next occurrence; returns false for a one-shot alarm.
    private bool RescheduleIfRepeating(AlarmState alarm, DateTimeOffset after)
    {
        if (alarm.Repeat == AlarmRepeat.Once) return false;
        alarm.Phase = AlarmPhase.Scheduled;
        alarm.RingStartedAt = null;
        alarm.SnoozeUntil = null;
        alarm.SnoozeCount = 0;
        alarm.TargetAt = NextOccurrence(alarm.Hour, alarm.Minute, alarm.Repeat, alarm.RepeatAnchorDayOfWeek, after);
        return true;
    }

    public void DeleteAlarm()
    {
        StopSound();
        State.Alarm = new AlarmState();
        SaveAndNotify();
    }

    public void DismissAlarm()
    {
        StopSound();
        if (!RescheduleIfRepeating(State.Alarm, DateTimeOffset.Now))
            State.Alarm.Phase = AlarmPhase.Dismissed;
        SaveAndNotify();
    }

    public void SnoozeAlarm(int minutes)
    {
        if (State.Alarm.Phase is not (AlarmPhase.Ringing or AlarmPhase.Scheduled or AlarmPhase.Snoozed)) return;
        StopSound();
        State.Alarm.Phase = AlarmPhase.Snoozed;
        State.Alarm.SnoozeUntil = DateTimeOffset.Now.AddMinutes(Math.Max(1, minutes));
        State.Alarm.SnoozeCount++;
        SaveAndNotify();
    }

    private void Tick()
    {
        var now = DateTimeOffset.Now;
        var dirty = false;
        var timer = State.Timer;
        if (timer.Phase == TimerPhase.Running && TimerRemaining <= TimeSpan.Zero)
        {
            timer.Phase = TimerPhase.Completed;
            timer.CompletedAt = now;
            timer.CompletionAcknowledged = false;
            StartTimerSound();
            RaiseEvent("timer_completed", "Timer done",
                string.IsNullOrWhiteSpace(timer.Label) ? "Your timer finished." : timer.Label);
            dirty = true;
        }
        else if (timer.Phase == TimerPhase.Completed && !timer.CompletionAcknowledged &&
                 timer.CompletedAt is not null && now - timer.CompletedAt >= TimerDoneVisibility)
        {
            timer.CompletionAcknowledged = true;
            StopSound();
            dirty = true;
        }

        var alarm = State.Alarm;
        var target = alarm.Phase == AlarmPhase.Snoozed ? alarm.SnoozeUntil : alarm.TargetAt;
        if (alarm.Phase is AlarmPhase.Scheduled or AlarmPhase.Snoozed && target is not null && now >= target)
        {
            alarm.Phase = AlarmPhase.Ringing;
            alarm.RingStartedAt = now;
            StartAlarmSound();
            RaiseEvent("alarm_ringing", "Alarm",
                string.IsNullOrWhiteSpace(alarm.Label) ? FormatAlarmTime(alarm) : alarm.Label);
            dirty = true;
        }
        else if (alarm.Phase == AlarmPhase.Ringing && alarm.RingStartedAt is not null &&
                 now - alarm.RingStartedAt >= AlarmTimeout)
        {
            StopSound();
            if (!RescheduleIfRepeating(alarm, now)) alarm.Phase = AlarmPhase.Dismissed;
            dirty = true;
        }

        if (dirty) Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void StartTimerSound()
    {
        StopSound();
        SystemSounds.Asterisk.Play();
    }

    private void StartAlarmSound()
    {
        StopSound();
        _soundTimer = new System.Threading.Timer(_ =>
        {
            try { SystemSounds.Exclamation.Play(); } catch { }
        }, null, TimeSpan.Zero, TimeSpan.FromSeconds(1.4));
    }

    private void StopSound()
    {
        _soundTimer?.Dispose();
        _soundTimer = null;
    }

    private void Load()
    {
        if (!File.Exists(_statePath)) return;
        try
        {
            State = JsonSerializer.Deserialize<TimerAlarmSnapshot>(File.ReadAllText(_statePath), JsonOptions)
                ?? new TimerAlarmSnapshot();
            RecoverAfterRestart();
            Save();
        }
        catch (Exception ex)
        {
            try { File.Move(_statePath, _statePath + $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}", true); }
            catch { }
            State = new TimerAlarmSnapshot();
            _log.Error("Timer/alarm state was invalid and has been reset", ex);
        }
    }

    private void RecoverAfterRestart()
    {
        var now = DateTimeOffset.Now;
        if (State.Timer.Phase == TimerPhase.Running && TimerRemaining <= TimeSpan.Zero)
        {
            State.Timer.Phase = TimerPhase.Completed;
            State.Timer.CompletedAt = now;
            State.Timer.CompletionAcknowledged = true;
        }
        if (State.Alarm.Phase == AlarmPhase.Ringing)
        {
            if (!RescheduleIfRepeating(State.Alarm, now)) State.Alarm.Phase = AlarmPhase.Dismissed;
        }
        // A scheduled alarm whose time passed while the app was closed: reschedule if repeating, else drop.
        if (State.Alarm.Phase == AlarmPhase.Scheduled && State.Alarm.TargetAt is not null &&
            now - State.Alarm.TargetAt > AlarmTimeout)
        {
            if (!RescheduleIfRepeating(State.Alarm, now)) State.Alarm.Phase = AlarmPhase.Dismissed;
        }
    }

    private void SaveAndNotify()
    {
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Save()
    {
        try
        {
            var temporary = _statePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(State, JsonOptions));
            File.Move(temporary, _statePath, true);
        }
        catch (Exception ex) { _log.Error("Unable to save timer/alarm state", ex); }
    }

    private void RaiseEvent(string name, string title, string message) =>
        EventRaised?.Invoke(this, new TimerAlarmEventArgs(name, title, message));

    public static string FormatDuration(TimeSpan value) => value.TotalHours >= 1
        ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
        : $"{value.Minutes:00}:{value.Seconds:00}";

    public static string FormatRepeat(AlarmRepeat repeat) => repeat switch
    {
        AlarmRepeat.Daily => "Daily",
        AlarmRepeat.Weekdays => "Weekdays",
        AlarmRepeat.Weekly => "Weekly",
        _ => "Once"
    };

    public static string FormatAlarmTime(AlarmState alarm)
    {
        if (alarm.Use24Hour) return $"{alarm.Hour:00}:{alarm.Minute:00}";
        var suffix = alarm.Hour < 12 ? "AM" : "PM";
        var hour = alarm.Hour % 12;
        if (hour == 0) hour = 12;
        return $"{hour}:{alarm.Minute:00} {suffix}";
    }

    public void Dispose()
    {
        _tickTimer.Stop();
        StopSound();
        Save();
    }
}
