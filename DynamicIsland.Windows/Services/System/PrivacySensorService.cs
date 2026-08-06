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
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };

    public event EventHandler<PrivacySensorState>? Changed;
    public PrivacySensorState Current { get; private set; } = PrivacySensorState.None;

    public void Start()
    {
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
        return key is not null && ScanApps(key);
    }

    private static bool ScanApps(RegistryKey key)
    {
        foreach (var name in key.GetSubKeyNames())
        {
            using var sub = key.OpenSubKey(name);
            if (sub is null) continue;
            // Desktop (non-Store) apps live one level deeper under "NonPackaged".
            if (name.Equals("NonPackaged", StringComparison.OrdinalIgnoreCase))
            {
                if (ScanApps(sub)) return true;
                continue;
            }
            if (sub.GetValue("LastUsedTimeStart") is long start && start > 0
                && sub.GetValue("LastUsedTimeStop") is long stop && stop == 0)
                return true;
        }
        return false;
    }

    public void Dispose() => _timer.Stop();
}
