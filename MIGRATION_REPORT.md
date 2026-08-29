# Migration Report — Python/Tkinter → C# / WPF / .NET 10

This document records the audit of the legacy Python prototype, what the native
Windows rewrite preserved, improved, and deferred, the resulting architecture,
the files created, and the manual test results.

---

## 1. Source of truth

The repository contained a single Python implementation,
`Animated og version.py` (~160 KB). The other variants named in the migration
brief (`DynamicIsland_Premium.py`, `DynamicIsland_ApplePlus.py`, `left docked.py`,
`docked to top best so far.py`) were **not present** in the workspace, so the one
available file was treated as the behavioral source of truth. It is archived
unchanged at [`legacy-python/Animated og version.py`](legacy-python/Animated%20og%20version.py).

## 2. Python features found (audit)

The legacy app was behavior-rich but architecturally fragile (one large Tkinter
file). Behaviors identified:

| Area | Legacy behavior |
| --- | --- |
| Island states | Compact pill + expanded panel |
| Expansion | Hover to expand, spring/animated resize |
| Media | Now-playing title/artist, ranking across sessions, playback controls |
| Volume / mute | Endpoint volume + mute read/write |
| Audio activity | Some active-source detection (could reflect the wrong/first source) |
| Battery | Percentage + charging, with a fallback path |
| Clock | Time/date display |
| Timer / alarm | Presets, running/done states, persistent timer/alarm state across restart |
| Positioning | Top-center placement, drag-and-snap |
| Settings | Small inline settings menu (no durable general settings store) |
| Visual assistant | None in the legacy prototype; Q is a native feature added in the WPF rewrite |

Weaknesses identified to fix rather than copy: first/wrong audio source
selection, no durable general settings, no tray UI, fragile multi-monitor
positioning, no per-session audio classification, ad-hoc startup handling, and
no clean window lifecycle.

## 3. Features preserved

- Compact and expanded island states with hover expansion and a collapse delay.
- Media title/artist/artwork, progress, and play/pause / previous / next.
- Volume display and master mute state; volume and mute are adjustable.
- Battery percentage and charging state.
- Time and date display.
- Timer presets + custom durations, a *done* state with sound, and **persistent
  timer/alarm state that survives app restart** (with real timestamps, not just
  a countdown number).
- Top-center default positioning and draggable repositioning (with the manual
  position persisted).
- Light/dark theme behavior.
- Animated transitions between states.
- Native **Q visual assistant** with active-window/monitor capture, OCR, optional
  vision input, Ask/Say modes, streaming responses, follow-ups, dictation, and
  one-click prompt shortcuts.

## 4. Features improved

- **Media selection** — deterministic scoring across all GSMTC sessions (playing
  > paused > stopped, preferred-app boost, OS-current boost, recency boost)
  instead of first-found. Tie-breaks are stable.
- **Audio model** — CoreAudio/WASAPI layer distinguishes no-session, paused,
  playing, system-muted, any-session-muted, active-output, and
  unknown/unsupported, fixing the "wrong source / single flag" problem.
- **Settings** — full JSON settings store in `%LOCALAPPDATA%` with safe defaults
  and **corruption recovery** (invalid file is backed up and regenerated).
- **Tray integration** — system-tray menu (Open Settings, Recenter, Always on
  Top, Click-through, Launch on startup, Quit) with notifications for timer/alarm
  events.
- **Startup** — per-user `HKCU\...\Run` registration toggled from settings; no
  service, never SYSTEM, so audio/media APIs stay in the user session.
- **Window system integration** — per-monitor-V2 DPI awareness, multi-monitor
  recenter, single-instance mutex, hidden-from-Alt-Tab by default (debug toggle),
  optional click-through when compact, optional locked position,
  display-change/resume recovery.
- **Visuals** — premium liquid-glass redesign: rounded pill, layered soft
  shadows, glossy inner rim, blue accents, high-contrast text, and a dedicated
  animated **charging capsule**. The previous rectangular HWND halo was removed
  by rendering glass with clipped WPF brushes instead of window-wide acrylic.
- **Architecture** — MVVM with one-responsibility services and isolated interop,
  replacing the single-file Tkinter design.
- **Q assistant** — provider-neutral contracts and streaming session state in
  `DynamicIsland.Q.Core`; the WPF layer owns capture, OCR, dictation, encrypted
  credentials, settings, and Island presentation.

## 5. Features deferred / decisions

- **Live desktop blur (system acrylic)** intentionally dropped in favor of
  clipped WPF glass to remove the rectangular halo. Clean translucency over a
  noisy live-blur edge.
- **Per-app / non-default audio device selection** — the indicator reports the
  default output endpoint only.
- **Recurring/weekly alarms** — alarms target the next occurrence (today or
  tomorrow) only.
- **Arm64** — built/tested for win-x64.
- **Hosted AI requests** — Q is opt-in and only sends prompts/OCR or an enabled
  screen image when the user invokes it; provider credentials and terms remain
  provider-specific.

## 6. Architecture summary

- **Composition root:** `App.xaml.cs` constructs services, view models, the
  island/settings/timer windows, and the tray, wires events, and owns shutdown.
- **Models** are plain immutable-ish state types compared by value so the UI
  only updates on real change.
- **Services** each own one concern and surface state via events:
  `MediaSessionService` (GSMTC, async poll + scoring), `AudioSessionService`
  (CoreAudio on a dedicated MTA thread), `BatteryService`, `ClockService`,
   `TimerAlarmService` (DispatcherTimer state machine + atomic JSON persistence),
   `SettingsService` (JSON + corruption recovery), `StartupService` (registry),
   `TrayService` (WinForms NotifyIcon), `WindowPositionService` (DPI/monitor math),
   `ThemeService` (system light/dark), `LoggingService` (AppData log).
- **Q core** contains provider contracts, prompt composition, normalized streaming
  events, model discovery, and conversation state. WPF Q services provide screen
  capture/OCR, Windows dictation, and DPAPI-backed provider secrets.
- **Interop** is confined to `Interop/NativeMethods.cs` and
  `Interop/CoreAudioInterop.cs` (P/Invoke + WASAPI COM interfaces).
- **ViewModels** (`IslandViewModel`, `SettingsViewModel`, `TimerAlarmViewModel`)
  aggregate service state into bindable properties and `RelayCommand`s.
- **Views** are thin XAML windows with minimal code-behind for window-style and
  positioning concerns.

## 7. Files created

New native solution (`DynamicIsland.slnx`) and project `DynamicIsland.Windows`:

```
App.xaml, App.xaml.cs, app.manifest, AssemblyInfo.cs, GlobalUsings.cs,
DynamicIsland.Windows.csproj
Infrastructure/ObservableObject.cs, RelayCommand.cs
Interop/NativeMethods.cs, CoreAudioInterop.cs
Models/AppSettings.cs, AudioState.cs, BatteryState.cs, IslandState.cs,
       MediaInfo.cs, TimerAlarmState.cs
 Services/AudioSessionService.cs, BatteryService.cs, ClockService.cs,
          LoggingService.cs, MediaSessionService.cs, SettingsService.cs,
          StartupService.cs, ThemeService.cs, TimerAlarmService.cs,
          TrayService.cs, WindowPositionService.cs
 Services/Q/                    Q capture, dictation, and DPAPI secret storage
 ViewModels/IslandViewModel.cs, SettingsViewModel.cs, TimerAlarmViewModel.cs
 Views/IslandWindow.xaml(.cs), SettingsWindow.xaml(.cs), TimerAlarmWindow.xaml(.cs)
 DynamicIsland.Q.Core/QContracts.cs, Providers.cs, QPromptComposer.cs,
 DynamicIsland.Q.Core/QSessionController.cs
 README.md, MIGRATION_REPORT.md
```

Legacy Python moved to `legacy-python/Animated og version.py` (not deleted, not
built).

## 8. Build status

`dotnet build DynamicIsland.slnx -c Debug` → **Build succeeded, 0 Warnings, 0
Errors** (verified on the workspace-local .NET 10.0.301 SDK).

## 9. Manual test results

Tested with the Debug build on Windows 11 (24H2-class, 1920×1080, single
monitor). Screenshots are in [`test-artifacts/`](test-artifacts/).

| # | Test | Result | Notes / evidence |
| --- | --- | --- | --- |
| 1 | App launches and stays alive | ✅ | Process persists; no main window in Alt-Tab (tool window) |
| 2 | Island appears top-center | ✅ | After resetting a leftover manual test position to default top-center |
| 3 | Compact state renders | ✅ | Mute/audio glyph, charging capsule, clock — `test-artifacts/compact-island-final.png` |
| 4 | Expanded state on hover | ✅ | Expanded visual comparison and timer surface — `test-artifacts/timer-panel-comparison.png` / `timer-panel-final-crop.png` |
| 5 | No-media state | ✅ | "No media playing / Start playback in any supported app" shown |
| 6 | Volume / mute display | ✅ | "0% / System muted" reflected from CoreAudio |
| 7 | Battery + charging state | ✅ | Green charging capsule at 85% in compact and expanded |
| 8 | Settings window opens from island | ✅ | Full option set; `settings-uia.png` |
| 9 | Light/dark theme | ✅ | Dark theme applied end-to-end across settings/combos; `settings-dark-2.png` |
| 10 | Timer start (5m preset) | ✅ | → Running, real timestamp persisted to `timer-alarm.json`, compact countdown; `timer-active.png` |
| 11 | Timer cancel | ✅ | → Idle, state cleared in `timer-alarm.json` |
| 12 | Alarm set + delete | ✅ | Future alarm scheduled and persisted, then deleted back to empty state |
| 13 | Drag reposition persists | ✅ | Manual position written to `settings.json` and restored on next launch |
| 14 | Settings save/load | ✅ | `settings.json` written to `%LOCALAPPDATA%`, reloaded on restart |
| 15 | Corrupted-settings recovery | ✅ (code-verified) | Invalid JSON is moved to `*.corrupt-<ts>` and defaults regenerated |
| 16 | Single instance | ✅ | Second launch exits immediately, leaves the running instance |
| 17 | Square/halo removed | ✅ | Two fixes: (a) hard `#52000000` shadow rectangle replaced with a soft rounded `DropShadowEffect` + transparent window margin (pixel-sampled — shadow now fades to desktop at the pill edges instead of a flat full-width band); (b) the corner glow ellipses were squaring off because a `Border` with `CornerRadius` does not clip children to its rounded corners — added a size-tracking rounded `RectangleGeometry` clip on the content so the glows follow the rounded corners |
| 18 | Charging icon added | ✅ | Dedicated animated green charging capsule — the requested feature |
| 19 | Q assistant core flows | ✅ (automated) | Prompt composition, Ask/Say guidance, follow-up history, provider errors, empty-stream handling, and OpenRouter parsing are covered by `DynamicIsland.Windows.Tests/QCoreTests.cs` and `QProviderTests.cs` |

### Not yet manually exercised (recommended before release)

- Startup toggle round-trip (enable → confirm `HKCU\...\Run` value → reboot).
- Multi-monitor recenter and DPI scaling across monitors with different scale
  factors.
- Display sleep/wake resilience over a real sleep cycle.
- Reduced-motion mode visual diff (setting and code path exist).
- Tray menu items end-to-end (Always on Top, Click-through, Recenter, Quit).

## 10. Known issues

- The expanded charging row is slightly tight under the clock column on the
  default size; readable but could use more vertical breathing room.
- Acrylic is approximated with WPF glass (see README → Known limitations).
- Audio indicator follows the default output endpoint only.
- Manual position from automated UI testing can persist between runs; the
  documented default is top-center (reset `DefaultPosition` to top-center or use
  tray → Recenter).
- A corrected native Q overlay screenshot still needs to be captured after the
  latest visual fixes; see [`design-qa.md`](design-qa.md). The current Q behavior
  and privacy model are documented in [`Q.md`](Q.md).

## AirPods release audit

The 1.0.4 native AirPods integration uses a WinRT Bluetooth LE watcher for Apple
manufacturer data (company ID 0x004C), then correlates advertisements with one
uniquely connected paired AirPods or compatible Beats device. Rotating BLE addresses
are accepted; the app does not write pairing records or connect to the earbuds.

The presentation model exposes model, left/right/case battery, charging, case-lid,
and in-ear state. Public advertisements provide coarse 10% battery buckets, so the
UI renders ranges such as 90-99% and omits unavailable components rather than
inventing an exact value or showing 0%.

The view model suppresses RSSI/timestamp-only refreshes, preventing ordinary BLE
traffic from restarting the expanded-island animation. ANC, transparency, gesture,
and similar controls remain intentionally deferred because Windows exposes no safe
public API for those controls in this driver-free integration.

Verification for this release: the full Release test suite passes 80/80, and the
production WPF project builds with 0 errors. Physical-AirPods validation remains
hardware and adapter dependent and is not claimed as automated coverage.
