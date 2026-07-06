# adrilight — Claude Code Notes

## Project Overview

**adrilight** is a Windows desktop app (WPF, .NET 8.0, x64) that drives ambient LED lighting by capturing the screen via SharpDX/DXGI and sending colour data over a serial port to an Arduino-based LED controller.

This is **adrilight — AbsenteeAtom Edition** (see `adrilight/Properties/AssemblyInfo.cs` for the current version), forked from [fabsenet/adrilight](https://github.com/fabsenet/adrilight) v2.0.9.

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

### Releasing — use the `/ship` skill
The full release procedure (tests → version bump → README/WhatsNew.xaml/SESSIONS.md updates → commit + push → publish → zip → GitHub release) is encoded in `.claude/skills/ship/SKILL.md`. Invoke it with `/ship` rather than performing the steps by hand.

> **Important:** The release zip must always include `Arduino/adrilight/adrilight.ino` (subfolder preserved). End users need it to flash their Arduino — without it they cannot use the application. The `/ship` skill handles this.

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

Per-session change log (root causes, fixes, test counts per version) lives in [SESSIONS.md](SESSIONS.md) — deliberately kept out of this file so it is not loaded into context every session. Read it when investigating a regression, writing a release entry, or wondering why something is the way it is. The `/ship` skill appends a new entry there for each release.

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

## Development Principles

- **Extract pure functions from pipeline logic.** When adding behaviour to the capture pipeline, pull any self-contained computation into a `static` (or otherwise side-effect-free) method that operates only on its parameters. This is what makes pipeline logic unit-testable without hardware: `DetectBlackBars` and `GetSamplingRectangle` are `internal static` methods on `DesktopDuplicatorReader` that operate on a `BitmapData` struct; `ParseRegistryData` is a `static` method on `NightLightDetection` that operates on a `byte[]`. Both are fully covered by tests that supply synthetic input data, with no GPU, no registry, and no running app required.

---

## Working Preferences

- **Discuss before implementing.** When given an instruction, briefly outline the proposed approach and flag any concerns or trade-offs before writing code. If there is a better way, say so — pushback is welcome. Do not blindly execute instructions.
- **Rebuild and restart after every push.** After pushing to GitHub, republish to the `publish/adrilight-X.Y.Z/` folder and restart adrilight so the local build stays current with the repository. The `/ship` skill does this as part of its procedure; if you commit and push outside `/ship`, republish manually.

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
