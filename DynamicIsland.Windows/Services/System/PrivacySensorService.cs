using System.Windows.Threading;
using DynamicIsland.Windows.Models;
using Microsoft.Win32;

namespace DynamicIsland.Windows.Services;

/// <summary>
/// Reports whether any app is currently using the webcam or microphone by reading the Windows
/// CapabilityAccessManager consent store. An app in active use has LastUsedTimeStop == 0 while it holds
/// the device. Read-only registry polling — no device is opened. Raises on the UI thread.
/// </summary>
public sealed class PrivacySensorService(LoggingService log) : IDisposable
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromSeconds(3)
    };

    public event EventHandler<PrivacySensorState>? Changed;
    public PrivacySensorState Current { get; private set; } = PrivacySensorState.None;
    private bool _started;

    public void Start()
    {
        if (_started) return;
        _started = true;
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
        Poll();
    }

    private void Poll()
    {
        var next = new PrivacySensorState(AnyInUse("webcam"), AnyInUse("microphone"));
        if (next == Current) return;
        Current = next;
        Changed?.Invoke(this, next);
    }

    private bool AnyInUse(string capability)
    {
        try { return ScanRoot(Registry.CurrentUser, capability) || ScanRoot(Registry.LocalMachine, capability); }
        catch (Exception ex) { log.Debug($"Privacy sensor scan failed: {ex.Message}"); return false; }
    }

    private static bool ScanRoot(RegistryKey root, string capability)
    {
        using var key = root.OpenSubKey(
            $@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{capability}");
        return key is not null && ScanTree(key);
    }

    private static bool ScanTree(RegistryKey key)
    {
        if (IsActive(key)) return true;
        foreach (var name in key.GetSubKeyNames())
        {
            try
            {
                using var sub = key.OpenSubKey(name);
                if (sub is not null && ScanTree(sub)) return true;
            }
            catch (UnauthorizedAccessException) { }
            catch (System.Security.SecurityException) { }
        }
        return false;
    }

    private static bool IsActive(RegistryKey key)
    {
        var start = ReadInt64(key.GetValue("LastUsedTimeStart"));
        var stop = ReadInt64(key.GetValue("LastUsedTimeStop"));
        return start is > 0 && stop.GetValueOrDefault() == 0;
    }

    private static long? ReadInt64(object? value) => value switch
    {
        long number => number,
        int number => number,
        byte[] bytes when bytes.Length >= sizeof(long) => BitConverter.ToInt64(bytes, 0),
        string text when long.TryParse(text, out var number) => number,
        _ => null
    };

    public void Dispose()
    {
        _timer.Stop();
        _started = false;
    }
}
