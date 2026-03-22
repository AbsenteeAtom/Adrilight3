# adrilight — Claude Code Notes

## Project Overview

**adrilight** is a Windows desktop app (WPF, .NET 8.0, x64) that drives ambient LED lighting by capturing the screen via SharpDX/DXGI and sending colour data over a serial port to an Arduino-based LED controller.

This is **adrilight 3.4.0 — AbsenteeAtom Edition**, forked from [fabsenet/adrilight](https://github.com/fabsenet/adrilight) v2.0.9.

### Key technologies
- WPF + Windows Forms, targeting `net8.0-windows`
- CommunityToolkit.Mvvm (ObservableObject, RelayCommand)
- Ninject for dependency injection
- NLog for logging
- SharpDX 4.2.0 (DXGI / Direct3D11) for screen capture — net45 DLL variants via direct HintPath
- System.Reactive
- MaterialDesignThemes v4.9.0

### Project layout
```
adrilight/
  App.xaml / App.xaml.cs         — entry point, DI setup, session/screensaver/sleep hooks
  DesktopDuplication/            — SharpDX screen capture + colour sampling
  Extensions/                    — ArrayExtensions (Swap)
  Fakes/                         — Design-time fake implementations
  Settings/                      — IUserSettings interface + UserSettings impl
  Spots/                         — ISpot / Spot / ISpotSet / SpotSet
  Util/                          — SerialStream, TcpControlServer, SleepWakeController, NightLightDetection, FakeSerialPort, etc.
  ValidationRules/               — WPF validation
  View/                          — XAML views + SettingsWindowComponents
  ViewModel/                     — SettingsViewModel, ViewModelLocator

adrilight.Tests/
  SpotsetTests.cs                — BoundsWalker, BuildSpots, LED offset, mirroring (8 tests)
  DependencyInjectionTests.cs    — DI container setup, design-time and runtime (2 tests)
  UserSettingsManagerTests.cs    — Settings save/load/migrate (3 tests)
  BlackBarDetectionTests.cs      — DetectBlackBars + GetSamplingRectangle (11 tests)
  SleepWakeTests.cs              — SleepWakeController suspend/resume state machine (5 tests)
  NightLightDetectionTests.cs   — ParseRegistryData: ON, OFF, null, unexpected byte, too-short data (5 tests)
```

Total tests: **44/44 passing**

### Running tests
```
dotnet test adrilight.Tests/adrilight.Tests.csproj
```

### Building a local executable
```
dotnet publish adrilight/adrilight.csproj -c Release --self-contained false -o ./publish/adrilight-3.4.0
```
Output goes to `publish/adrilight-3.4.0/adrilight.exe` (~24MB, requires .NET 8 Desktop Runtime x64).
The `publish/` folder is excluded from git via `.gitignore`.

### End-user installation guide
`INSTALLATION.md` in the repo root is a plain-English guide for first-time users. It covers hardware requirements (including power supply sizing), LED strip layout and corner connectors, wiring (power injection, data break), Arduino IDE setup and sketch configuration, COM port identification, installing and running Adrilight, basic settings configuration, and common troubleshooting steps. Update it if any of those areas change.

### GitHub Release checklist
1. Run the publish command above to produce the release folder.
2. Copy the Arduino sketch into the publish folder, preserving its subfolder:
   ```
   Arduino/adrilight/adrilight.ino  →  publish/adrilight-3.4.0/Arduino/adrilight/adrilight.ino
   ```
3. Verify the `.exe` file version is correctly stamped (right-click → Properties → Details).
4. Zip the entire `publish/adrilight-X.Y.Z/` folder as `adrilight-X.Y.Z.zip`.
5. Upload the zip as the release asset on GitHub.

> **Important:** Always include `adrilight.ino` in the release zip. End users need it to flash their Arduino — without it they cannot use the application.

---

## Architecture — Capture Pipeline

The main loop runs on a background thread (`DesktopDuplicatorReader`):

```
DesktopDuplicator.GetLatestFrame()        — DXGI Desktop Duplication, ~8× downscale via mipmap level 3
    → Bitmap (locked as Format32bppRgb)
    → DetectBlackBars()                   — sparse edge scan, O(width + height), returns activeRegion Rectangle
    → foreach Spot (parallel if ≥40):
        GetSamplingRectangle()            — clamps spot to nearest content edge if spot is in a black bar
        GetAverageColorOfRectangularRegion() — unsafe pointer walk, 15-step grid sample
        ApplyColorCorrections()           — saturation threshold, white balance, linear/non-linear gamma
        spot.SetColor()                   — stores RGB + marks spot dirty
    → SpotSet.IsDirty = true
    → Thread.Sleep to enforce LimitFps
```

`SerialStream` runs a separate background thread:
```
while running:
    if SpotSet.IsDirty:                   — dirty flag checked under SpotSet.Lock
        SpotSet.IsDirty = false           — cleared atomically with snapshot (inside the same lock)
        send preamble + BGR bytes + postamble over serial port
    else:
        Thread.Sleep(minTimespan)         — sleep calculated once per port open
```

### Key design points
- **BGR byte order** — Arduino/FastLED uses BGR not RGB. Named constants in `SerialStream.cs` document this:
  ```csharp
  private const int ColourByteOrder_Blue  = 0;
  private const int ColourByteOrder_Green = 1;
  private const int ColourByteOrder_Red   = 2;
  ```
- **IsDirty cleared inside lock** — `SpotSet.IsDirty = false` is set inside `lock(SpotSet.Lock)` before reading spot colours, ensuring the flag is always cleared atomically with the colour snapshot. A frame cannot be missed.
- **Baud rate in UserSettings** — `UserSettings.BaudRate` (default 1,000,000) is read by `SerialStream`. If the user changes it the port is reopened. Arduino sketch must be flashed to match.
- **Version migration in UserSettingsManager** — `ApplyMigrations()` is called after settings deserialization. Migration v1→v2: `SpotsY -= 2`, `ConfigFileVersion = 2`. Future migrations go here, not in `App.xaml.cs`.

---

## Black Bar Detection (v3.1.0)

### Detection — `DetectBlackBars()`
Called once per frame after `LockBits`. Scans from each edge inward using `IsRowBlack` / `IsColumnBlack` (5 evenly-spaced pixel samples per row/column). Returns:
- `Rectangle.Empty` — entire frame is below luminance threshold (fully black)
- Full-frame rectangle — no bars detected
- Cropped rectangle — the active (non-black) content region

Controlled by `UserSettings.BlackBarDetectionEnabled` and `UserSettings.BlackBarLuminanceThreshold` (default 20).

### Remapping — `GetSamplingRectangle()`
Rather than turning bar LEDs off, spots whose rectangles fall in a black bar region are remapped to sample the nearest content edge:

```csharp
internal static Rectangle GetSamplingRectangle(Rectangle spotRect, Rectangle activeRegion)
```

- Spot overlaps content → returns the intersection (normal case)
- Spot above content → clamps to topmost content row
- Spot below content → clamps to bottommost content row
- Spot left of content → clamps to leftmost content column
- Spot right of content → clamps to rightmost content column
- `activeRegion.IsEmpty` → returns `spotRect` unchanged (fully black frame fallback)

This means **all LEDs remain active at all times**, reflecting the closest real picture colour.

### `ProcessSpot()` flow
```csharp
var samplingRect = GetSamplingRectangle(spot.Rectangle, activeRegion);
// stepx/stepy derived from samplingRect dimensions
GetAverageColorOfRectangularRegion(samplingRect, ...);
```

---

## Four Architecture Refactoring Fixes (applied before v3.1.0 feature work)

### 1 — BGR colour order documented
`SerialStream.cs` had silent BGR→RGB conversion with no comments. Added named constants and in-line comments at both write sites so future maintainers cannot accidentally introduce RGB writes.

### 2 — Baud rate moved to `IUserSettings`
Previously hardcoded as a local `const` in `SerialStream.DoWork()`. Now stored in `UserSettings.BaudRate` (default 1,000,000) so it can be changed without a rebuild. `SerialStream` tracks `openedBaudRate` and reopens the port when the value changes.

### 3 — `IsDirty` flag cleared atomically
Previously `IsDirty = false` was set *outside* the `SpotSet.Lock` after reading spot colours. This created a race window where a new frame could set the flag between the read and the clear, causing that frame to be skipped. Fixed by moving the clear *inside* the lock, before the colour read.

### 4 — Version migration moved to `UserSettingsManager`
Migration logic (v1→v2 SpotsY adjustment) had lived in `App.xaml.cs` alongside startup code. Extracted into `UserSettingsManager.ApplyMigrations(IUserSettings settings)`, called from `LoadIfExists()` after deserialization. Future migrations go in the same method as a clear chain of `if (ConfigFileVersion == N)` blocks.

---

## Changes from Original fabsenet v2.0.9

### Platform Migration
- Migrated from .NET Framework 4.7.2 to .NET 8.0 (`net8.0-windows`)
- Converted legacy XML-style `.csproj` to modern SDK-style format
- Replaced `packages.config` NuGet with `PackageReference`
- Forced SharpDX to use net45 DLL variants via direct HintPath references (SharpDX 4.2.0 netstandard build is missing `AcquireNextFrame`)
- Added `win-x64` RuntimeIdentifier

### Dependencies
- Replaced MvvmLight (GalaSoft) with **CommunityToolkit.Mvvm 8.3.2** — `ViewModelBase` → `ObservableObject`, `Set()` → `SetProperty()`, `RaisePropertyChanged()` → `OnPropertyChanged()`
- Replaced `System.Windows.Interactivity` with **Microsoft.Xaml.Behaviors.Wpf**
- Updated **MaterialDesignThemes** from 2.x to 4.9.0 and **MaterialDesignColors** to 2.1.4
- Updated all other packages to current .NET 8 compatible versions
- **Removed Squirrel auto-updater entirely** (source of antivirus false positives)
- Added `System.IO.Ports` NuGet package (moved out of .NET Framework BCL)
- Updated MoreLinq to 3.4.0

### Breaking Change Fixes
- `System.Windows.Forms.ContextMenu` / `MenuItem` → `ContextMenuStrip` / `ToolStripMenuItem`
- `SpotSet.TakeLast()` ambiguity with MoreLinq resolved using explicit `Enumerable.TakeLast()`
- `OutputDuplication.AcquireNextFrame()` — changed to pre-declare `OutputDuplicateFrameInformation frameInformation` before call
- Removed `AdrilightUpdater.cs` (depended on Squirrel and Semver)
- Icon loading changed from embedded manifest resource to file copy (`CopyToOutputDirectory: PreserveNewest`)
- Fixed `Process.Start(url)` → `Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })`
- Added `AssemblyVersion` and `AssemblyFileVersion` to `AssemblyInfo.cs`
- Set `GenerateAssemblyInfo=false` to prevent conflict with existing `AssemblyInfo.cs`

### MaterialDesign v4 Compatibility
- `ColorZoneMode.Accent` → `PrimaryMid` in `SettingsWindow.xaml`
- `{StaticResource SecondaryAccentBrush}` → `{DynamicResource MaterialDesignSecondaryLightBrush}` in `Preview.xaml`, `Whitebalance.xaml`, `LedSetup.xaml`
- `MaterialDesignSwitchAccentToggleButton` → `MaterialDesignSwitchToggleButton` in `LedSetup.xaml`, `GeneralSetup.xaml`, `ComPortSetup.xaml`
- Removed WhatsNew browser-based page, replaced with static About page showing version and change history
- `WhatsNewSetupSelectableViewPart` moved from nested class to standalone file `WhatsNewSelectableViewPart.cs`

### New Features

**TCP Control Server** (`adrilight/Util/TcpControlServer.cs`)
- Listens on `127.0.0.1:5080` (loopback only)
- Accepts plain-text commands, responds with JSON

| Command | Effect | Response |
|---|---|---|
| `ON` | Sets `TransferActive = true` | `{"status":"on"}` |
| `OFF` | Sets `TransferActive = false` | `{"status":"off"}` |
| `TOGGLE` | Toggles current state | `{"status":"on/off"}` |
| `STATUS` | Queries state | `{"status":"on/off"}` |
| `EXIT` | Shuts the app down | `{"status":"exiting"}` |

- Started in `App.OnStartup`, stopped in `App.OnExit`

**Session Lock / Unlock** (`App.xaml.cs`)
- `SystemEvents.SessionSwitch` handler automatically sets `TransferActive = false` on lock and restores previous state on unlock

**Screen Saver Detection** (`App.xaml.cs`)
- `DispatcherTimer` polls every 5 seconds using `SystemParametersInfo(SPI_GETSCREENSAVERRUNNING)`
- Automatically turns LEDs off when screen saver starts, restores state when it stops

**Sleep / Wake Awareness** (`App.xaml.cs` + `Util/SleepWakeController.cs`)
- `SystemEvents.PowerModeChanged` handler calls `SleepWakeController.OnSuspend()` / `OnResume()`
- On suspend: saves `TransferActive` state, sets it to `false`
- On resume: restores previous state
- Gated by `UserSettings.SleepWakeAwarenessEnabled` (default `true`)
- Logic extracted to `SleepWakeController` so the state machine is unit-testable without a WPF host

**Black Bar Detection** (`DesktopDuplicatorReader.cs`) — see dedicated section above

### Performance Improvements
- Default `LimitFps` reduced from 60 to 30 in `UserSettings.cs`
- `SerialStream.DoWork()` — `minTimespan` now calculated once per port open rather than every frame
- `SerialStream.DoWork()` — removed unnecessary `GC.Collect()` from exception handler
- `DesktopDuplicatorReader` — removed unnecessary `GC.Collect()` from `GetNextFrame()` exception handler
- `DesktopDuplicatorReader` — `Parallel.ForEach` only used for spot counts ≥ 40; sequential loop used below threshold
- `DesktopDuplicatorReader` — `ProcessSpot()` extracted as separate method
- **Dirty Flag Optimisation** — `IsDirty` bool on `ISpotSet`/`SpotSet`. Set by `DesktopDuplicatorReader` after each frame. `SerialStream` only writes to Arduino when `IsDirty` is `true`, then clears it — eliminates redundant serial writes during static content

---

## Session History

### 2026-03-16 — Dead code cleanup + test migration to .NET 8
- Deleted 3 unused files (`TraceFrameMessage.cs`, `MovedRegion.cs`, `MathExtensions.cs`)
- Removed 5 unused methods, stale `UserSettingsFake` properties, dead PropertyChanged handler
- Fixed `SpotsXMaximum`/`SpotsYMaximum` getter mutation bug in `SettingsViewModel`
- Tightened bare `catch {}` to `catch (UnauthorizedAccessException)` in `SerialStream`
- Migrated `adrilight.Tests` from .NET 4.7.2 to `net8.0-windows`; 13/13 tests passing

### 2026-03-16 — Branding, version, README
- Renamed Perry Edition → AbsenteeAtom Edition; bumped version 2.1.0 → 3.0.0
- `README.md` fully rewritten — badges, TOC, hardware setup, TCP API table, build instructions
- About page (`WhatsNew.xaml`) restructured with description, credits, and change sections

### 2026-03-16 — XAML theme fixes
- `LedSetup.xaml`: removed explicit foreground that resolved to black
- `LightingModeSetup.xaml`: `StaticResource` → `DynamicResource` for theme brush
- `Whitebalance.xaml`: hardcoded light blue hyperlink → `{DynamicResource PrimaryHueMidBrush}`

### 2026-03-20 — Arduino sketch improvements
- Bug fix: `last_serial_available` initialised to `0UL` (was `-1L` → wrapped to `ULONG_MAX`)
- Replaced manual LED copy loop with `memcpy`; inlined BGR→RGB conversion
- Verified on hardware: Flash 6224 bytes (2%), RAM 3090 bytes (37%)

### 2026-03-20 — Four architecture fixes + black bar detection (v3.1.0)
1. BGR byte order documented with named constants in `SerialStream.cs`
2. Baud rate moved from hardcoded const to `UserSettings.BaudRate`
3. `IsDirty = false` moved inside `SpotSet.Lock` for atomicity
4. Version migration extracted to `UserSettingsManager.ApplyMigrations()`
5. Black bar detection added: `DetectBlackBars()` + `GetSamplingRectangle()` + settings toggle + 11 tests
6. Version bumped to 3.1.0

### 2026-03-21 — Sleep/wake awareness + documentation overhaul (v3.2.0)
1. `SleepWakeController` extracted to `Util/SleepWakeController.cs` — testable state machine for suspend/resume
2. `PowerModeChanged` event in `App.xaml.cs` extended: Suspend → pause LEDs, Resume → restore state
3. `SleepWakeAwarenessEnabled` added to `IUserSettings` / `UserSettings` / `UserSettingsFake` (default `true`)
4. 5 new tests in `SleepWakeTests.cs`; total tests 29/29
5. About page restructured: proper 3.0.0 / 3.1.0 / 3.2.0 sections; fixed duplicate 3.1.0 heading
6. README restructured: per-version What's new subsections (3.2.0, 3.1.0, 3.0.0)
7. CLAUDE.md updated to reflect 3.2.0 state
8. Version bumped to 3.2.0

### 2026-03-22 — NLog fix + version bump (v3.2.1)
1. **Root cause:** .NET 8 does not load the `<nlog>` section from `App.config` — log files were never created in the published build
2. **Fix:** Replaced `SetupDebugLogging()` (Debug-only, DebuggerTarget only) with `SetupLogging()` that configures NLog programmatically at startup:
   - General file target: `logs/adrilight.log.YYYY-MM-DD.txt` — Info+ in Release, Debug+ in Debug
   - NightLight-specific file target: `logs/adrilight.log.nightlight.YYYY-MM-DD.txt` — Debug+ always (captures registry-read debug trace from `NightLightDetection`)
   - Debug builds additionally write to `DebuggerTarget` (VS Output window)
   - Encoding changed from iso-8859-2 to UTF-8
3. Confirmed both log files created and writing correctly on first launch of published build
4. All 29 tests passing
5. Version bumped to 3.2.1

### 2026-03-22 — Diagnostics feature + toolbar status indicator (v3.3.0)
1. `LogEntry` model added (`ViewModel/LogEntry.cs`) — timestamp, level, short logger name, message
2. `DiagnosticsViewModel` added (`ViewModel/DiagnosticsViewModel.cs`) — in-memory ring buffer (max 200), ratcheting `DiagnosticStatus` (Ok/Warning/Error), filter (All/Warn+/Error+), `Acknowledge()`, `NightLightConfidenceDisplay`
3. `ObservableCollectionNLogTarget` added (`Util/ObservableCollectionNLogTarget.cs`) — NLog `Target` subclass that pushes entries into `DiagnosticsViewModel`; registered in `App.SetupLogging()` at Info+
4. `Diagnostics.xaml` view added with Night Light status card and log viewer ListBox (colour-coded by level)
5. `DiagnosticsSelectableViewPart` added — `Order = -45` (after About, before other tabs)
6. `SettingsViewModel` extended: `Diagnostics` property, `NavigateToDiagnosticsCommand`, `NightLightProbability`, `UpdateNightLightConfidenceDisplay()`, `OpenUrlInstallationGuideCommand`
7. `NightLightDetection` extended: `_lastProbability` field, sets `SettingsViewModel.NightLightProbability` after each prediction
8. `App.xaml.cs`: `_diagnosticsViewModel` instance created before `SetupLogging()`; passed to DI container via `ToConstant`; `ObservableCollectionNLogTarget` wired in `SetupLogging()`
9. Toolbar status indicator added to `SettingsWindow.xaml` — PackIcon with DataTriggers for Ok/Warning/Error states; tooltip bound to `Diagnostics.StatusTooltip`; click navigates to Diagnostics tab
10. Installation Guide added to kebab menu (between Project Page and I have an issue)
11. 10 new tests in `DiagnosticsViewModelTests.cs`; total tests 39/39
12. Version bumped to 3.3.0

### 2026-03-22 — Status indicator fix + baud rate note correction (v3.3.1)
1. Toolbar status indicator changed from `PackIcon` (outline icon) to a solid `Ellipse` with black `Stroke` and coloured `Fill` — green/amber/red — ensuring visibility against the orange `PrimaryDark` toolbar background
2. Baud rate release note corrected in `README.md` and `WhatsNew.xaml` — baud rate is an internal `UserSettings` property, not yet exposed in the UI; notes previously implied it was user-configurable
3. Version bumped to 3.3.1

### 2026-03-22 — Replace ML Night Light detection with direct registry read (v3.4.0)
1. `NightLightDetection.cs` rewritten — ML.NET inference replaced with registry read of CloudStore REG_BINARY blob; `byte[18] == 0x15` → On, any other value → Off, null/too-short → Unknown
2. `INightLightRegistryReader` interface introduced for testability; `RegistryNightLightReader` is the production implementation; Ninject binding added to `SetupDependencyInjection` (was missing, caused startup crash before fix)
3. `NightLightState` enum added (`Unknown / Off / On`) replacing the old ML prediction class
4. `SettingsViewModel` simplified — `NightLightProbability` and `UpdateNightLightConfidenceDisplay()` removed; replaced by `UpdateNightLightState(NightLightState)`
5. `Whitebalance.xaml` updated — "experimental" label and "Learn how it works" hyperlink removed; description updated to reflect registry read
6. `Microsoft.ML` PackageReference and `NightLightDetectionModel.zip` EmbeddedResource removed from `adrilight.csproj`
7. `DiagnosticsViewModel.NightLightConfidenceDisplay` now shows "Night Light: ON / OFF / Unknown"
8. 5 new tests in `NightLightDetectionTests.cs`; total tests 44/44
9. Version bumped to 3.4.0

---

## Outstanding Bugs

### WhitebalanceBlue random value on restart
- **Symptom:** The "Normal Day Light" Blue slider shows an incorrect value (observed: 135, ~186, ~206) after some restarts. Red and Green stay correct. Restarting again sometimes restores the correct value.
- **Confirmed:** The corrupted value is written to `adrilight-settings.json` by a previous session — subsequent restarts just load the already-corrupt file. It is not loaded incorrectly; it was saved incorrectly.
- **Suspected trigger:** Occurs after a version update (What's New page shown on startup). Consistent with `isNewVersion = true` → `OpenSettingsWindow()` called unconditionally before `_nightLightDetection` is started.
- **Suspected cause:** Possibly a `NightLightState` enum serialisation/deserialisation interaction introduced in v3.4.0, or a MaterialDesignDiscreteSlider write-back during initial layout when the window is opened at startup before bindings fully stabilise. Root cause not yet identified.
- **Investigation status:** Diagnostic logging added and confirmed (stack trace pointed to JSON deserialization on load, not a new write in the same session). Bug has not reproduced since diagnostics were added.

---

## Working Preferences

- **Discuss before implementing.** When given an instruction, briefly outline the proposed approach and flag any concerns or trade-offs before writing code. If there is a better way, say so — pushback is welcome. Do not blindly execute instructions.
- **Rebuild and restart after every push.** After pushing to GitHub, republish to the `publish/adrilight-X.Y.Z/` folder and restart adrilight so the local build stays current with the repository.

---

## Development Notes

- The app starts minimized to the system tray by default — check the tray if the window doesn't appear
- `StartMinimized` is a user setting on the General Setup tab; the settings window always opens on first run or after a version change
- `UserSettings.BaudRate` defaults to 1,000,000. Arduino sketch must be flashed to match.
- Night Light detection reads byte 18 of the CloudStore REG_BINARY blob at `HKCU\Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate`; `0x15` = ON, `0x13` = OFF, absent/other = Unknown
- SharpDX assemblies are referenced directly from the NuGet cache via HintPath — not via PackageReference — because the netstandard build lacks `AcquireNextFrame`
- The TCP control server listens on `127.0.0.1:5080`
- Log files are written to `logs\` next to `adrilight.exe` — NLog is configured programmatically in `App.xaml.cs` (not `App.config`, which .NET 8 ignores for NLog)
