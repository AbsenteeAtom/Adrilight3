# adrilight — Claude Code Notes

## Project Overview

**adrilight** is a Windows desktop app (WPF, .NET 8.0, x64) that drives ambient LED lighting by capturing the screen via SharpDX/DXGI and sending colour data over a serial port to an Arduino-based LED controller.

This is **adrilight 3.7.1 — AbsenteeAtom Edition**, forked from [fabsenet/adrilight](https://github.com/fabsenet/adrilight) v2.0.9.

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
  Util/                          — SerialStream, TcpControlServer, SleepWakeController, NightLightDetection, MonitorEnumerator, MonitorInfo, FakeSerialPort, etc.
  ValidationRules/               — WPF validation
  View/                          — XAML views + SettingsWindowComponents
  ViewModel/                     — SettingsViewModel, ViewModelLocator

adrilight.Tests/
  SpotsetTests.cs                — BoundsWalker, BuildSpots, LED offset, mirroring (8 tests)
  DependencyInjectionTests.cs    — DI container setup, design-time and runtime (2 tests)
  UserSettingsManagerTests.cs    — Settings save/load/migrate + whitebalance clamping (6 tests)
  BlackBarDetectionTests.cs      — DetectBlackBars + GetSamplingRectangle (11 tests)
  SleepWakeTests.cs              — SleepWakeController suspend/resume state machine (5 tests)
  NightLightDetectionTests.cs   — ParseRegistryData: ON (0x15, 0x12), OFF (0x13, 0x10), null, unknown byte, too-short data (7 tests)
  ModeManagerTests.cs           — inhibitor model, screen-saver-while-locked scenario, mode switching, pipeline Start/Stop (12 tests)
  AudioCaptureReaderTests.cs    — ModeId, Start/Stop wiring, zero-audio, burst at assigned band, sensitivity, band model helpers (BuildBands, BandBinLo, BandCenterFrequency), wavelength/colour pure helpers (20 tests)
```

Total tests: **101/101 passing** (no GPU required — all hardware-bound code is excluded at the DXGI boundary)

### Running tests
```
dotnet test adrilight.Tests/adrilight.Tests.csproj
```

### Test coverage and the hardware boundary

The test suite covers all logic that does not require a live GPU or display output. The hard boundary is the DXGI/Direct3D11 layer inside `DesktopDuplicator`.

**What is covered (101 tests):**

| Test file | What it exercises |
|---|---|
| `SpotsetTests.cs` | `BoundsWalker`, `SpotSet.BuildSpots`, LED offset, mirroring |
| `BlackBarDetectionTests.cs` | `DesktopDuplicatorReader.DetectBlackBars()` and `GetSamplingRectangle()` |
| `SleepWakeTests.cs` | `SleepWakeController` suspend/resume state machine |
| `NightLightDetectionTests.cs` | `NightLightDetection.ParseRegistryData` |
| `UserSettingsManagerTests.cs` | Settings save/load/migrate, whitebalance clamping |
| `DiagnosticsViewModelTests.cs` | `DiagnosticsViewModel` ring buffer, status ratchet, filtering |
| `ModeManagerTests.cs` | `ModeManager` inhibitor model, screen-saver-while-locked bug scenario, mode switching, pipeline Start/Stop |
| `AudioCaptureReaderTests.cs` | `AudioCaptureReader` Start/Stop wiring, zero-audio, burst at assigned band, sensitivity scaling, band model helpers (`BuildBands`, `BandBinLo`, `BandCenterFrequency`), pure helpers (`WavelengthToRgb`, `FrequencyToWavelength`, `AttackAlpha`, `DecayAlpha`), beat detection, BPM detection helpers (`ComputeOnsetStrength`, `ComputeAutocorrelation`, `ComputeConfidence`, `ComputeStability`, `LagToBpm`/`BpmToLag`) |
| `DependencyInjectionTests.cs` | Ninject container wiring (design-time and runtime) |

`DetectBlackBars` and `GetSamplingRectangle` are declared `internal static` on `DesktopDuplicatorReader`. Tests call them directly, supplying a `BitmapData` obtained by locking a `System.Drawing.Bitmap` constructed in-process — no GPU involved.

**What is excluded and why:**

`DesktopDuplicator` (`DesktopDuplication/DesktopDuplicator.cs`) is entirely untested. Its constructor calls `new Factory1().GetAdapter1()` and `output1.DuplicateOutput(_device)` — both require a real DXGI-capable GPU and an active primary display output. There is no injection seam (no interface, no factory parameter) that would allow a fake frame source to be substituted. Attempting to instantiate it in a test process throws a `DesktopDuplicationException` or `SharpDXException`.

The following methods in `DesktopDuplicatorReader` are also excluded:

- `Run()` / `GetNextFrame()` — the capture loop; instantiates `DesktopDuplicator` directly and cannot be exercised without hardware
- `ApplyColorCorrections()` — `private`; applies saturation threshold, white balance scaling, and gamma (linear/non-linear); not reachable from tests
- `GetAverageColorOfRectangularRegion()` — `private unsafe`; walks pixel memory to compute average colour; not reachable from tests

**Where the testable boundary is drawn:**

The boundary is at the `BitmapData` struct (a pointer to CPU-side locked bitmap memory). Any method that takes a `BitmapData` as input can be tested by constructing a `System.Drawing.Bitmap`, painting it with known colours, locking it, and passing the result directly. This is precisely what `BlackBarDetectionTests` does, and it is why `DetectBlackBars` and `GetSamplingRectangle` have full test coverage despite living inside `DesktopDuplicatorReader`.

Everything upstream of `BitmapData` — acquiring a frame from DXGI, building mip maps in the GPU, copying to a staging texture — requires real hardware and is covered only by manual end-to-end testing with the physical LED setup.

### Building a local executable
```
dotnet publish adrilight/adrilight.csproj -c Release --self-contained false -o ./publish/adrilight-X.Y.Z
```
Output goes to `publish/adrilight-X.Y.Z/adrilight.exe` (~24MB, requires .NET 8 Desktop Runtime x64).
The `publish/` folder is excluded from git via `.gitignore`.

### End-user installation guide
`INSTALLATION.md` in the repo root is a plain-English guide for first-time users. It covers hardware requirements (including power supply sizing), LED strip layout and corner connectors, wiring (power injection, data break), Arduino IDE setup and sketch configuration, COM port identification, installing and running Adrilight, basic settings configuration, and common troubleshooting steps. Update it if any of those areas change.

### GitHub Release checklist
1. Run the publish command above to produce the release folder.
2. Copy the Arduino sketch into the publish folder, preserving its subfolder:
   ```
   Arduino/adrilight/adrilight.ino  →  publish/adrilight-X.Y.Z/Arduino/adrilight/adrilight.ino
   ```
3. Verify the `.exe` file version is correctly stamped (right-click → Properties → Details).
4. Zip the entire `publish/adrilight-X.Y.Z/` folder as `adrilight-X.Y.Z.zip`.
5. Upload the zip as the release asset on GitHub.

> **Important:** Always include `adrilight.ino` in the release zip. End users need it to flash their Arduino — without it they cannot use the application.

---

## Architecture — Capture Pipeline

The main loop runs on a background thread (`DesktopDuplicatorReader`):

```
[if SpanningEnabled]
    DesktopDuplicator(AdapterIndex, OutputIndex).GetLatestFrame()   — left monitor, ~8× downscale
    DesktopDuplicator(AdapterIndex2, OutputIndex2).GetLatestFrame() — right monitor, ~8× downscale
    StitchBitmaps()                       — row-by-row CopyMemory, width=w1+w2, height=max(h1,h2)
[else]
    DesktopDuplicator(AdapterIndex, OutputIndex).GetLatestFrame()   — single monitor, ~8× downscale
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
- **Version migration in UserSettingsManager** — `ApplyMigrations()` is called after settings deserialization. Migration v1→v2: `SpotsY -= 2`, `ConfigFileVersion = 2`. Migration v2→v3: all six whitebalance setters re-assigned through the clamping setter, `ConfigFileVersion = 3`. Future migrations go here, not in `App.xaml.cs`.

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

**Multi-Monitor Support** (`Util/MonitorEnumerator.cs`, `Util/MonitorInfo.cs`, v3.7.0)
- `MonitorEnumerator.Enumerate()` — static helper; enumerates DXGI adapters/outputs, filters `IsAttachedToDesktop`, cross-references `Screen.AllScreens` by `DeviceName` for primary flag and resolution. Labels: `"Display N — W×H (Primary)"` / `"Display N — W×H"`. Falls back to a single default entry on DXGI error.
- `UserSettings.AdapterIndex` / `OutputIndex` (int, default 0) — persisted; selecting a monitor takes effect immediately (next frame).
- `DesktopDuplicatorReader` reconstructs `DesktopDuplicator` on index change via `PropertyChanged`.
- `SettingsViewModel.AvailableMonitors` / `SelectedMonitor` — populated at startup; `SelectedMonitor` setter writes indices to settings.
- Capture Display card on General Setup tab with `ComboBox`.

**Dual-Display Spanning** (`DesktopDuplicatorReader.cs`, v3.7.1)
- `UserSettings.SpanningEnabled` / `AdapterIndex2` / `OutputIndex2` — second display for side-by-side stitching.
- When `SpanningEnabled`: captures both monitors sequentially, stitches via `StitchBitmaps()` (reusable `_stitchedBitmap`, row-by-row `SharpDX.Utilities.CopyMemory`). `SpanningEnabled = false` is a strict no-op.
- `_desktopDuplicator2` disposed and reconstructed on spanning setting changes.
- `SettingsViewModel.SelectedMonitor2` — second monitor selector, shares `AvailableMonitors` list.
- "Span two displays" toggle + second `ComboBox` (visibility bound to `SpanningEnabled`) on General Setup tab.

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

### 2026-03-22 — WhitebalanceBlue corruption fix + test isolation + icon alignment (v3.4.1)
1. Root cause identified: `MaterialDesignDiscreteSlider` discrete-snap formula writes back `pixelPosition × tickFrequency` instead of the correct proportion, producing values above the slider Maximum (100). The observed values 135, 186, 206 correspond exactly to pixel positions 67, 92, 102 multiplied by `Width/(Maximum−Minimum) ≈ 2.02`.
2. **Fix A (invariant):** All six whitebalance setters in `UserSettings` now clamp to `[1, 100]` via a private `Clamp()` helper — `Math.Clamp(value, (byte)1, (byte)100)`. Any out-of-range value written by the slider bug (or any other source) is silently clamped before reaching the backing field and JSON.
3. **Fix B (repair):** `ApplyMigrations` v2→v3 step added: re-assigns all six whitebalance properties through the clamping setter, then sets `ConfigFileVersion = 3`. Repairs any already-corrupt value in an existing `adrilight-settings.json` on first launch after upgrade.
4. **Pre-existing build warning fixed:** `UserSettings.cs` was missing `using System;`, causing a spurious `Guid` error in the WPF design-time temp project. Added the `using` directive.
5. 3 new tests in `UserSettingsManagerTests.cs` (setter clamps above 100, clamps below 1, migration advances version); total tests 47/47
6. **Test isolation fixed:** Tests were writing to the real `%LocalAppData%\adrilight\adrilight-settings.json`, corrupting user settings between runs. `UserSettingsManager` gained an optional `settingsFolder` constructor parameter (null → production path). `App.SetupDependencyInjection` gained a matching optional `settingsFolder` parameter forwarded to `UserSettingsManager`. All tests in `UserSettingsManagerTests` and `DependencyInjectionTests.RunTimeCreation_Works` now pass a unique `Path.GetTempPath()` subfolder.
7. **White Balance mode icon alignment fixed:** Row heights changed from `1*` to `auto`; icon `VerticalAlignment` set to `Top` and top margins unified to `8px` (matching the label top margin); description TextBlocks given matching `8px` top margin. Removes the visual gap between icons and their labels.
8. Version bumped to 3.4.1

### 2026-03-23 — Sound to Light mode (v3.6.0)
1. **NAudio 2.2.1** added to `adrilight.csproj` — provides `WasapiLoopbackCapture`, `FastFourierTransform`, `Complex`.
2. **`ILightingMode`** interface created (`Util/ILightingMode.cs`): `ModeId`, `Start()`, `Stop()`, `IsRunning`. Ninject convention binding discovers all implementations and injects them into `ModeManager` as `IEnumerable<ILightingMode>`.
3. **`IModeManager` extended** with `INotifyPropertyChanged`; `ModeManager` now accepts `IEnumerable<ILightingMode>`, stops the outgoing pipeline and starts the incoming one in `SetMode()`, and raises `PropertyChanged(nameof(ActiveMode))`.
4. **`DesktopDuplicatorReader`** gains `IModeManager` constructor param; subscribes to `PropertyChanged`; `shouldBeRunning` now gated on `ActiveMode == ScreenCapture`. DDR shuts itself off automatically when switching to Sound to Light.
5. **`IAudioCaptureProvider`** interface + **`WasapiAudioCaptureProvider`** implementation — WASAPI hardware not accessed until `Start()` (safe for DI and tests).
6. **`AudioCaptureReader`** (`ILightingMode`, `ModeId = SoundToLight`) — final design after redesign in 2026-03-24 session:
   - 1024-sample circular mono buffer; accumulates front-L+front-R (channels 0 and 1 only) mono-mixed samples from WASAPI callback. Surround devices report many channels but stereo content only populates ch 0/1; mixing all channels would dilute amplitude.
   - Per-buffer: apply Hann window → `FastFourierTransform.FFT` → 32 logarithmically-spaced frequency bands (20 Hz – 20 kHz). Each band energy = per-bin-average RMS, normalised by `reference = 0.04f` (calibrated for 2-channel mono mix).
   - Each LED randomly assigned a band index (0–31). Band colour from `WavelengthToRgb(FrequencyToWavelength(centerHz))` — log mapping 20 Hz→700 nm (red) to 20 kHz→400 nm (violet); Bruton approximation.
   - Strong bass hit (`rawBass > max(0.005 / sensScale, 0.0005)`, rate-limited to once/second) reshuffles all LED→band assignments. Logs at Info level so visible in Diagnostics tab.
   - Per-spot exponential smoothing: `attack = (101-s)/100 * 0.95 + 0.04`, `decay = (101-s)/100 * 0.35 + 0.01`.
   - RGB gain (`SoundToLightRedGain/GreenGain/BlueGain`) applied per-channel with `Math.Clamp` before byte conversion.
   - Locks `SpotSet.Lock`, writes all spots, sets `IsDirty = true` every FFT run.
7. **`IUserSettings` / `UserSettings` / `UserSettingsFake`** — added `SoundToLightSensitivity` (default 50), `SoundToLightSmoothing` (default 50), `SoundToLightRedGain` (0.6), `SoundToLightGreenGain` (1.05), `SoundToLightBlueGain` (1.50).
8. **`App.xaml.cs`** — added `IAudioCaptureProvider → WasapiAudioCaptureProvider` singleton binding in runtime branch; `ILightingMode` convention binding already added in previous session.
9. **`SettingsViewModel`** gains `IModeManager` constructor param; `IsScreenCaptureMode` / `IsSoundToLightMode` bool shim properties; `PropertyChanged` on `ActiveMode` propagates to both.
10. **`SoundToLightSetup.xaml` + `.xaml.cs`** — Sensitivity and Smoothing sliders; `SoundToLightSelectableViewPart` (`Order = 75`).
11. **`GeneralSetup.xaml`** — Lighting Mode card with two `RadioButton`s (`GroupName="LightingMode"`) bound to `IsScreenCaptureMode` / `IsSoundToLightMode`.
12. **`ModeManagerFake`** — `event PropertyChangedEventHandler PropertyChanged` added (no-op).
13. 20 new tests in `AudioCaptureReaderTests.cs`; 2 new pipeline tests in `ModeManagerTests.cs`. Total tests: 81/81.
14. Version bumped to 3.6.0.

### 2026-03-23 — ModeManager architecture (v3.5.0)
1. `IModeManager` interface and `ModeManager` implementation added (`Util/IModeManager.cs`, `Util/ModeManager.cs`). ModeManager is the sole writer of `TransferActive`.
2. Inhibitor model: `AddInhibitor(source)` / `RemoveInhibitor(source)` — sources tracked independently in a `HashSet`; user intent saved on first inhibitor, restored when last clears.
3. **Screen-saver-while-locked bug fixed:** session lock and screen saver previously shared `_transferActiveBeforeLock`; if the screen saver fired while locked it would overwrite the saved state, preventing LEDs from restoring after unlock. Each source now has its own inhibitor entry.
4. `SleepWakeController` retargeted to `IModeManager` — calls `AddInhibitor("sleep")` / `RemoveInhibitor("sleep")` instead of setting `TransferActive` directly. `_wasActive` field deleted.
5. `App.xaml.cs` session-lock and screen-saver handlers retargeted to `AddInhibitor("lock")` / `AddInhibitor("screensaver")`. `_transferActiveBeforeLock` field deleted.
6. `TcpControlServer` updated: `STATUS` now returns `{"status":"on/off","mode":"screen/sound/gamer"}`; new `MODE SCREEN/SOUND/GAMER/STATUS` commands added. Takes `IModeManager` as new constructor parameter.
7. `ModeManagerFake` added to `Fakes/` for design-time DI.
8. `IModeManager` bound in `SetupDependencyInjection` (design-time: fake; runtime: real).
9. `SendRandomColors` marked as `DEBUG-ONLY` in `IUserSettings` and at its usage site in `SerialStream`.
10. 10 new tests in `ModeManagerTests.cs` (including `ScreenSaverWhileLocked_TransferActiveRestoredAfterBoth`); 5 `SleepWakeTests` rewritten to use `Mock<IModeManager>`. Total tests: 59/59.
11. `WhatsNew.xaml` updated: 3.5.0 section added, TCP API table updated with new commands, column width widened for longer command names.
12. Version bumped to 3.5.0.

### 2026-03-23 — Night Light fix, UI polish, documentation update (v3.4.2)
1. **Night Light byte[18]=0x12 not detected as On:** Different Windows builds write different base byte values to the CloudStore REG_BINARY blob. Previously only `0x15` was treated as On; `0x12` (observed on this machine) was silently classified as Off. `ParseRegistryData` now checks `data[18] == 0x15 || data[18] == 0x12`. Known value map: `0x12` / `0x15` = On; `0x10` / `0x13` = Off. Full blob is now logged on every change to aid future diagnosis of further variants.
2. **Night Light state changes not visible in Diagnostics tab:** State change log was at `Debug` level; `ObservableCollectionNLogTarget` captures `Info+`. Changed to `_log.Info` so transitions appear in the Diagnostics UI.
3. 2 new tests in `NightLightDetectionTests.cs` (`0x12` → On, `0x10` → Off); `AnyByteOtherThan0x15` test renamed/split for clarity. Total tests: 49/49.
4. **Navigation sidebar label:** Changed from `{Binding Title}` ("adrilight 3.4.x") to static "Menu".
5. **White Balance info card:** `PollBox` icon replaced with `InformationOutline`; title reworded to "Setting up white balance"; body rewritten for clarity; calibration tip corrected to explain colour ratio rather than giving misleading absolute-value advice; "Controling" typo fixed.
6. **What's New page:** Sections reordered newest-first so the most recent changes appear at the top. "About" menu label renamed to "What's New".
7. **Dead code removed:** `Tools/NighlightDetectionModelGenerator/` deleted — the ML model generator was made redundant by the registry-read approach in v3.4.0. Removing it also eliminated the duplicate `.sln` file that caused VS Code to prompt for solution selection on every open.
8. Version bumped to 3.4.2.

### 2026-03-24 — Sound to Light redesign + UI additions (v3.6.0 continued)
1. **Sound to Light physics redesign:** `AudioCaptureReader` rewritten twice. Final model: 32 logarithmically-spaced frequency bands (20 Hz – 20 kHz). Each LED randomly assigned a band; band colour derived from centre wavelength via `FrequencyToWavelength` (log mapping 20 Hz→700 nm, 20 kHz→400 nm) + `WavelengthToRgb` (Bruton approximation). Band energy = per-bin-average RMS of FFT bins in range, normalised by `reference`. Bass hit (above dynamic threshold derived from Sensitivity) reshuffles all assignments; rate-limited to once per second.
2. **Ninject bug fixed:** `AudioCaptureReader` is `internal sealed` — Ninject.Extensions.Conventions 3.3.0 `SelectAllClasses()` skips non-public classes. Fixed with explicit `kernel.Bind<ILightingMode>().To<AudioCaptureReader>()` binding in `App.xaml.cs`.
3. **Frequency range extended to 20 kHz:** `BandLowFrequency` and `FrequencyToWavelength` `fMax` raised from 10 000 to 20 000 Hz. Upper bands now capture high-frequency content (cymbals, hi-hats) and display blue/violet.
4. **Brightness reference lowered:** `reference` constant in `ApplyToSpots` reduced from `0.25f` → `0.02f` → `0.01f` across two iterations, calibrated for real WASAPI loopback levels at typical listening volume.
5. **RGB Colour Gain sliders:** `SoundToLightRedGain` (0.6), `SoundToLightGreenGain` (1.05), `SoundToLightBlueGain` (1.50) added to `IUserSettings`/`UserSettings`/`UserSettingsFake`. Applied per-channel with `Math.Clamp` in `AudioCaptureReader.ApplyToSpots` before byte conversion. Three sliders (range 0–2, ticks at 0.25, numeric F2 TextBox) added to `SoundToLightSetup.xaml`.
6. **Tray mode menu:** `AddModeMenuItems()` helper added to `App.xaml.cs`. Adds a separator + `ToolStripMenuItem` per `LightingMode` value with `Checked` state; updated live via `_modeManager.PropertyChanged`.
7. **Spurious 'no pipeline' warning suppressed:** `ModeManager.SetMode()` no longer warns when `_activeMode == ScreenCapture` — `DesktopDuplicatorReader` manages itself via `PropertyChanged` and intentionally has no `ILightingMode` entry.
8. **Diagnostics Copy log button:** `CopyToClipboardCommand` added to `DiagnosticsViewModel`; copies all `FilteredEntries` (oldest-first, full timestamp/level/logger/message) to clipboard. "Copy log" button added to filter toolbar in `Diagnostics.xaml`.
9. **AudioCaptureReaderTests updated:** `MakeSettings` mock sets up gain properties; `FrequencyToWavelength_20kHz_Returns400nm` replaces 10 kHz variant; old band-model tests (`BuildBands_Returns32Bands`, `BandBinLo_NonDecreasingAcrossBands`, `LowBand_HasWarmColor`, `HighBand_HasCoolColor`, `BurstAtAssignedBand_LightsUpSpot`, `HighSensitivity_BrighterThanLowSensitivity`) added. Total tests: 86/86.

### 2026-03-27 — Dual-display spanning (v3.7.1)
1. **`SpanningEnabled`** (bool, default `false`), **`AdapterIndex2`** / **`OutputIndex2`** (int, default 0) added to `IUserSettings`, `UserSettings`, `UserSettingsFake`. `SpanningEnabled = false` means zero behaviour change for single-monitor users.
2. **`DesktopDuplicatorReader`** extended:
   - Fields: `_desktopDuplicator2`, `_rawBitmap1`, `_rawBitmap2`, `_stitchedBitmap`.
   - `PropertyChanged` handler: `SpanningEnabled`/`AdapterIndex2`/`OutputIndex2` changes dispose and null `_desktopDuplicator2`; next `GetNextFrame()` call reconstructs it.
   - `GetNextFrame()`: when `SpanningEnabled` is off, existing single-monitor path unchanged. When on, constructs `_desktopDuplicator2`, captures both frames, returns `StitchBitmaps(frame1, frame2)`.
   - `StitchBitmaps()`: reusable `_stitchedBitmap` (width = w1+w2, height = max(h1,h2)); row-by-row `SharpDX.Utilities.CopyMemory` for left half then right half. O(height) memcpy passes at 1/8 scale — negligible cost.
   - `finally` block: disposes `_rawBitmap1`, `_rawBitmap2`, nulls `_stitchedBitmap`, disposes `_desktopDuplicator2`.
3. **`SettingsViewModel`**: `SelectedMonitor2` two-way property added — same pattern as `SelectedMonitor`, reads/writes `AdapterIndex2`/`OutputIndex2`.
4. **`GeneralSetup.xaml`**: `BooleanToVisibilityConverter` declared in `UserControl.Resources`. Capture Display card extended with a "Span two displays" `ToggleButton` and a second `ComboBox` (visibility bound to `SpanningEnabled`).
5. No new tests — stitching is hardware-bound (requires two live DXGI outputs); existing 101 tests unaffected.
6. Version bumped to 3.7.1.

### 2026-03-27 — Multi-monitor support (v3.7.0)
1. **`MonitorInfo`** model added (`Util/MonitorInfo.cs`) — carries `AdapterIndex`, `OutputIndex`, `DisplayLabel`; `ToString()` returns the label for WPF binding.
2. **`MonitorEnumerator`** static helper added (`Util/MonitorEnumerator.cs`) — enumerates DXGI adapters and outputs via SharpDX `Factory1`, filters to `IsAttachedToDesktop == true`, cross-references with `System.Windows.Forms.Screen.AllScreens` by `DeviceName` to obtain the primary flag and pixel dimensions. Labels: "Display N — W×H (Primary)" or "Display N — W×H". Falls back to a single default entry on any DXGI error.
3. **`AdapterIndex`** and **`OutputIndex`** int properties (default `0`) added to `IUserSettings`, `UserSettings`, `UserSettingsFake`. Defaults preserve existing behaviour for single-monitor users; no migration needed.
4. **`DesktopDuplicatorReader`**: `GetNextFrame()` passes `UserSettings.AdapterIndex` / `UserSettings.OutputIndex` to `new DesktopDuplicator(...)` instead of `(0, 0)`. `PropertyChanged` handler nulls `_desktopDuplicator` when either index changes, triggering reconstruction on the next frame.
5. **`SettingsViewModel`**: `AvailableMonitors` (`IReadOnlyList<MonitorInfo>`) populated once at startup via `MonitorEnumerator.Enumerate()`; `SelectedMonitor` two-way property reads `AdapterIndex/OutputIndex` from settings and writes them back on selection change.
6. **`GeneralSetup.xaml`**: New "Capture Display" card with a `ComboBox` bound to `AvailableMonitors` / `SelectedMonitor`. Placed between Lighting Mode and Limit FPS cards. Icon: `MonitorMultiple`.
7. No new tests — `MonitorEnumerator` calls DXGI hardware (same exclusion boundary as `DesktopDuplicator`); existing 101 tests unaffected.
8. Version bumped to 3.7.0.

### 2026-03-26 — Piecewise colour mapping, band spread, smooth reshuffles (v3.6.5)
1. **`FrequencyToWavelength` redesigned** (piecewise): 20 Hz–10 kHz → logarithmic, 700 nm→490 nm (red→cyan); 10 kHz–14 kHz → linear, 490 nm→440 nm (cyan→blue); 14 kHz–20 kHz → linear, 440 nm→380 nm (blue→violet). Mathematically continuous at both breakpoints.
2. **`WavelengthToRgb` range extended** 400 nm → 380 nm: lower bound changed from `nm < 400f` to `nm < 380f`; red-ramp divisor changed from `40f` to `60f` (ramp now spans the full 380–440 nm range). Bruton perceptual dimming factor was never present in this implementation. At 380 nm: (1, 0, 1) = magenta-violet.
3. **`SoundToLightBandSpread` setting** added (`bool`, default `false`) to `IUserSettings`, `UserSettings`, `UserSettingsFake`. `ComputeSpread(float[] levels)` added as `internal static` pure helper in `AudioCaptureReader` — 5-tap max kernel (±1 band = 50%, ±2 bands = 15%). Applied in `ApplyToSpots` when setting is enabled. Toggle added to `SoundToLightSetup.xaml` between Smoothing and BPM cards.
4. **Smooth reshuffle transitions**: `ShuffleSpotAssignments()` previously reset `_spotSmoothed = new float[n]` on every call, causing all LEDs to snap to black and ramp up through the attack envelope on every bass hit (visible as steps). Now preserves the existing array when length matches: `_spotSmoothed = (existing?.Length == n) ? existing : new float[n]`.
5. **Benchmarks project removed from solution** (`adrilight.sln`): the `adrilight.benchmarks` project targeted .NET 4.7.2 with `packages.config` HintPath references pointing to a non-existent `packages/` folder — it was never loadable and caused a persistent "project failed to load" warning in VS Code. Project and configuration entries removed from `.sln`; folder left on disk.
6. **Tests**: `FrequencyToWavelength_20kHz_Returns380nm` (renamed from 400 nm), `FrequencyToWavelength_10kHz_Returns490nm`, `FrequencyToWavelength_14kHz_Returns440nm` (boundary continuity), `FrequencyToWavelength_MonotonicallyDecreasing` extended to include 14 kHz and 20 kHz, `WavelengthToRgb_400nm_IsViolet` updated (R now 40/60 ≈ 0.667), `WavelengthToRgb_380nm_IsMagentaViolet` added, `ComputeSpread_IsolatedBand_SpreadsToNeighbours`, `ComputeSpread_AllZero_ReturnsAllZero`. Total: 101/101.
7. Version bumped to 3.6.5.

### 2026-03-25 — Auto BPM detection (v3.6.4)
1. **`IBpmDetector` interface** added (`Util/IBpmDetector.cs`, `public`) — `DetectedBpm` (int), `BpmConfidence` (float 0..1), `BpmStatusText` (string), extends `INotifyPropertyChanged`.
2. **`BpmDetectorFake`** added (`Fakes/BpmDetectorFake.cs`) for design-time DI.
3. **`SoundToLightAutoBpm` setting** (bool, default `true`) added to `IUserSettings`, `UserSettings`, `UserSettingsFake`. Persisted as JSON. Controls whether auto-detected BPM is used for the reshuffle rate limit.
4. **`AudioCaptureReader`** now inherits from `ObservableObject` and implements `IBpmDetector`:
   - `ComputeBandRms` extracted from `ApplyToSpots` and called once per frame; result shared by both onset computation and colour pipeline.
   - **Onset strength buffer** (256-frame circular buffer, `OnsetBufferSize = 256`): each frame computes spectral flux (sum of positive band-energy increases vs. previous frame) via `ComputeOnsetStrength` and appends to the buffer.
   - **`RunBpmDetection(sr)`** fires every `AnalysisInterval = 43` frames (~1 s) after `WarmupFrames = 86` (~2 s). Extracts linear signal from circular buffer, calls `ComputeAutocorrelation` (mean-subtracted normalised, lags for 30–240 BPM), finds peak lag, computes `ComputeConfidence` (σ-score of peak prominence) and `ComputeStability` (coefficient-of-variation of last 4 estimates). Combined score drives `BpmStatusText` / `DetectedBpm` / `BpmConfidence` properties.
   - **`GetEffectiveBpm()`** returns detected BPM (capped 30–240) when `SoundToLightAutoBpm && BpmConfidence >= 0.5`, else falls back to `SoundToLightMaxBpm`. Hard 240 BPM ceiling always enforced.
   - Six new `internal static` pure helpers: `ComputeOnsetStrength`, `ComputeAutocorrelation`, `ComputeConfidence`, `ComputeStability`, `LagToBpm`, `BpmToLag`.
5. **`SettingsViewModel`** gains `IBpmDetector BpmDetector { get; }` (injected) and `string BpmSliderLabel` computed from `SoundToLightAutoBpm` ("Fallback BPM: X" or "Max BPM: X"); `PropertyChanged` handler fires `BpmSliderLabel` updates on both setting changes.
6. **`App.xaml.cs`** DI: runtime branch creates `AudioCaptureReader` explicitly and binds it as both `ILightingMode` and `IBpmDetector` (same singleton); design-time branch binds `BpmDetectorFake`. Old `if (!isInDesignMode)` ILightingMode binding block removed.
7. **`SoundToLightSetup.xaml`** Max BPM card updated: auto-detect toggle (`SoundToLightAutoBpm`), dynamic label (`BpmSliderLabel`), existing slider, status row with `BpmStatusText` + `ProgressBar` (confidence 0–1), updated description.
8. **`IBpmDetector` accessibility** set to `public` (required because `SettingsViewModel` is public).
9. **Tests**: `MakeSettings` gains `autoBpm` parameter; 10 new tests — `ComputeOnsetStrength` (3 cases), `ComputeAutocorrelation` (2 cases, periodic peak test uses search range [15,30] to avoid second-harmonic ambiguity), `ComputeConfidence` (2 cases), `ComputeStability` (2 cases), `LagToBpm`/`BpmToLag` round-trip. Total: 96/96.
10. Version bumped to 3.6.4.

### 2026-03-25 — Max BPM slider + settings diagnostics + beat/channel fixes (v3.6.3)
1. **Max BPM slider:** `SoundToLightMaxBpm` int property (default 120) added to `IUserSettings`, `UserSettings`, `UserSettingsFake`. New 440-wide card on `SoundToLightSetup.xaml` between Smoothing and Colour Channel Gain — snap-to-tick slider (30–240, steps of 5), dynamic label `"Max BPM: {value}"`. `AudioCaptureReader` replaces hardcoded `ReshuffleRateLimitMs = 1000` with `60000 / Math.Max(1, _settings.SoundToLightMaxBpm)` (computed each beat check). Default 120 BPM = 500 ms interval.
2. **Settings changes logged to Diagnostics:** `UserSettings.PropertyChanged` subscription added in `App.xaml.cs` after startup version-write. Logs `Setting changed: {PropertyName} = {value}` at Info level (appears in Diagnostics tab). Excludes `AdrilightVersion`, `ConfigFileVersion`, `InstallationId`.
3. **Beat detection log demoted to Debug:** Was at Info (fired every ~second during music — too noisy). Changed to `_log.Debug`.
4. **Surround device mono-mix fix:** `useCh = Math.Min(ch, 2)` in `OnAudioData` prevents 7.1/surround devices diluting the signal across all 8 channels. `reference` raised `0.01f` → `0.04f` to compensate for the now-correct amplitude.
5. **Beat detection fixed threshold:** Replaced dynamic `rawBass > smoothedBass × multiplier` (self-defeating after ~300 ms) with `beatThresh = max(0.005f / sensScale, 0.0005f)`.
6. **AudioCaptureReaderTests:** `MakeSettings` gains `maxBpm` parameter; priming-frame pattern added to two tests; `Beat_TriggersReshuffle` comment updated. Total tests: 86/86.
7. Version bumped to 3.6.3.

### 2026-03-24 — Diagnostics polish (v3.6.2, local only)
1. **Beat detection log demoted to Debug:** `"Beat detected"` was at Info so it appeared in the Diagnostics tab every second during music — too noisy. Changed back to `_log.Debug`.
2. **All settings changes logged to Diagnostics tab:** `UserSettings.PropertyChanged` subscription added in `App.xaml.cs` immediately after the startup version-write, so that internal startup mutations are never captured. For every subsequent user-initiated change, logs `Setting changed: {PropertyName} = {value}` at Info level using reflection to read the new value. Three internal properties are excluded: `AdrilightVersion`, `ConfigFileVersion`, `InstallationId`.
3. Version bumped to 3.6.2 (local build only, no GitHub release).

### 2026-03-24 — Beat detection fix + surround device mono mix fix (v3.6.0 continued)
1. **Beat detection replaced:** Dynamic threshold (`rawBass > smoothedBass × multiplier`) failed because the smoother adapted to `rawBass` within ~300 ms; after warmup the threshold was always ≥ rawBass so reshuffles never fired. Replaced with a simple sensitivity-scaled fixed floor: `beatThresh = max(0.005 / sensScale, 0.0005)`. Rate limiting (1000 ms) prevents over-triggering.
2. **Surround device mono mix fixed:** WASAPI loopback on 7.1/surround devices reports 8 channels but stereo content (YouTube, Spotify) only populates channels 0 and 1 (Front L/R). Averaging all 8 channels diluted the mono signal to ¼ amplitude, making `rawBass` too small to cross any beat threshold. Fix: cap mono mix at 2 channels (`useCh = Math.Min(ch, 2)`) while still striding by the full channel count so the sample pointer stays aligned. `reference` raised from `0.01f` → `0.04f` (×4) in `ApplyToSpots` to compensate for the now-higher amplitude and maintain the same LED brightness.
3. **Beat detection log promoted to Info:** `"Beat detected (rawBass=...)"` now logs at Info level so it appears in the Diagnostics tab. Previously Debug-only so it was invisible there.
4. **AudioCaptureReaderTests stabilised:** `BurstAtAssignedBand_LightsUpSpot` and `HighSensitivity_BrighterThanLowSensitivity` updated with a priming-frame pattern — one frame is fed before reading `SpotBins[0]` to consume any reshuffle triggered by the new low threshold; the 1000 ms rate limit then keeps the assignment stable for all measurement frames. Total tests: 86/86.

---

## ModeManager Architecture (v3.5.0)

### IModeManager / ModeManager (`Util/IModeManager.cs`, `Util/ModeManager.cs`)

`ModeManager` is the sole writer of `IUserSettings.TransferActive`. All code that needs to pause or resume LEDs calls `AddInhibitor` / `RemoveInhibitor` rather than writing `TransferActive` directly.

**Inhibitor model:**
- When the first inhibitor is added: user intent (`TransferActive`) is saved, `TransferActive` is forced to `false`
- When a subsequent inhibitor is added: tracked silently (no state change)
- When the last inhibitor is removed: `TransferActive` is restored to the saved user intent
- Multiple inhibitors can be active simultaneously; the saved intent is never overwritten by a later inhibitor

**Inhibitor sources:**

| Source | Added by | Removed by |
|---|---|---|
| `"sleep"` | `SleepWakeController.OnSuspend()` | `SleepWakeController.OnResume()` |
| `"lock"` | `App.xaml.cs` `SessionLock` event | `App.xaml.cs` `SessionUnlock` event |
| `"screensaver"` | `App.xaml.cs` screen saver timer | `App.xaml.cs` screen saver timer |

**Mode model:**
- `ActiveMode` defaults to `ScreenCapture` on every launch; never persisted
- `SetMode()` stops the outgoing `ILightingMode` pipeline (if running), updates `ActiveMode`, raises `PropertyChanged`, then starts the incoming pipeline
- `ILightingMode` implementations are discovered by Ninject convention binding and injected as `IEnumerable<ILightingMode>`
- `DesktopDuplicatorReader` subscribes to `IModeManager.PropertyChanged` and only runs when `ActiveMode == ScreenCapture`

**External code interaction:**
- Tray icon toggle, UI toggle, TCP ON/OFF: write `TransferActive` directly to `IUserSettings`
- `ModeManager.OnSettingsPropertyChanged` hears the write and updates `_userTransferActive`
- If currently inhibited, it immediately snaps `TransferActive` back to `false`

**TCP extensions (`TcpControlServer`):**
- `STATUS` now returns `{"status":"on/off","mode":"screen/sound/gamer"}`
- New commands: `MODE SCREEN`, `MODE SOUND`, `MODE GAMER`, `MODE STATUS`

---

## Outstanding Bugs

*(none)*

---

## Development Principles

- **Extract pure functions from pipeline logic.** When adding behaviour to the capture pipeline, pull any self-contained computation into a `static` (or otherwise side-effect-free) method that operates only on its parameters. This is what makes pipeline logic unit-testable without hardware: `DetectBlackBars` and `GetSamplingRectangle` are `internal static` methods on `DesktopDuplicatorReader` that operate on a `BitmapData` struct; `ParseRegistryData` is a `static` method on `NightLightDetection` that operates on a `byte[]`. Both are fully covered by tests that supply synthetic input data, with no GPU, no registry, and no running app required.

---

## Working Preferences

- **Discuss before implementing.** When given an instruction, briefly outline the proposed approach and flag any concerns or trade-offs before writing code. If there is a better way, say so — pushback is welcome. Do not blindly execute instructions.
- **Rebuild and restart after every push.** After pushing to GitHub, republish to the `publish/adrilight-X.Y.Z/` folder and restart adrilight so the local build stays current with the repository.

---

## Development Notes

- The app starts minimized to the system tray by default — check the tray if the window doesn't appear
- `StartMinimized` is a user setting on the General Setup tab; the settings window always opens on first run or after a version change
- `UserSettings.BaudRate` defaults to 1,000,000. Arduino sketch must be flashed to match.
- Night Light detection reads byte 18 of the CloudStore REG_BINARY blob at `HKCU\Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate`. Known ON values: `0x15` (some Windows builds), `0x12` (others). Known OFF values: `0x13`, `0x10`. Different Windows versions produce different base byte values at position 18 for the same Night Light state; in all observed cases ON = OFF + 2. Absent/too-short data returns Unknown. The full blob is logged (Debug) on every change to aid future diagnosis.
- SharpDX assemblies are referenced directly from the NuGet cache via HintPath — not via PackageReference — because the netstandard build lacks `AcquireNextFrame`
- The TCP control server listens on `127.0.0.1:5080`
- Log files are written to `logs\` next to `adrilight.exe` — NLog is configured programmatically in `App.xaml.cs` (not `App.config`, which .NET 8 ignores for NLog)
- **Multi-monitor — `AvailableMonitors` is populated once at startup.** If the user connects a new display after launch, the list will not update until adrilight is restarted. This is documented in the UI card description. Live hotplug via `SystemEvents.DisplaySettingsChanged` is not implemented.
- **Spanning — `SpanningEnabled = false` is a strict no-op.** The non-spanning code path in `GetNextFrame()` is entirely unchanged. Only enable spanning code paths when `UserSettings.SpanningEnabled` is true. `_desktopDuplicator2`, `_rawBitmap1`, `_rawBitmap2`, and `_stitchedBitmap` are all null in single-monitor mode.
- **Spanning — set `SpotsX` to the total LED count across both monitors' combined top and bottom runs.** The stitched frame is treated as a single wide screen by `SpotSet`; spot rectangles map proportionally across both displays.
- **Sound to Light — beat detection must use a fixed threshold, not a dynamic one.** A dynamic threshold (`rawBass > smoothedBass × multiplier`) is self-defeating: the smoother adapts to the current level within ~300 ms so the threshold ends up ≥ rawBass permanently. Use `beatThresh = max(K / sensScale, floor)` where K is a fixed constant. Rate-limiting (once per second) handles over-triggering.
- **Sound to Light — WASAPI multi-channel devices.** `WasapiLoopbackCapture` may report 6 or 8 channels on surround devices, but stereo content only populates channels 0 (Front-L) and 1 (Front-R). Always cap the mono mix at `useCh = Math.Min(ch, 2)` and divide by `useCh`, NOT by `ch`. Averaging all channels dilutes amplitude by ch/2, making beat detection and brightness both fail. `reference` in `ApplyToSpots` is calibrated for a 2-channel mono mix.
