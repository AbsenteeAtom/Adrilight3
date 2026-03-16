# adrilight — Claude Code Notes

## Project Overview

**adrilight** is a Windows desktop app (WPF, .NET 8.0, x64) that drives ambient LED lighting by capturing the screen via SharpDX/DXGI and sending colour data over a serial port to an Arduino-based LED controller.

This is **adrilight 3.0.0 — AbsenteeAtom Edition**, forked from [fabsenet/adrilight](https://github.com/fabsenet/adrilight) v2.0.9.

### Key technologies
- WPF + Windows Forms, targeting `net8.0-windows`
- CommunityToolkit.Mvvm (ObservableObject, RelayCommand)
- Ninject for dependency injection
- NLog for logging
- SharpDX 4.2.0 (DXGI / Direct3D11) for screen capture — net45 DLL variants via direct HintPath
- System.Reactive
- Microsoft.ML for Night Light mode detection
- MaterialDesignThemes v4.9.0

### Project layout
```
adrilight/
  App.xaml / App.xaml.cs         — entry point, DI setup, session/screensaver hooks
  DesktopDuplication/            — SharpDX screen capture
  Extensions/                    — ArrayExtensions (Swap)
  Fakes/                         — Design-time fake implementations
  Settings/                      — IUserSettings interface + UserSettings impl
  Spots/                         — ISpot / Spot / ISpotSet / SpotSet
  Util/                          — SerialStream, TcpControlServer, NightLightDetection, FakeSerialPort, etc.
  ValidationRules/               — WPF validation
  View/                          — XAML views + SettingsWindowComponents
  ViewModel/                     — SettingsViewModel, ViewModelLocator

adrilight.Tests/
  SpotsetTests.cs                — BoundsWalker, BuildSpots, LED offset, mirroring (8 tests)
  DependencyInjectionTests.cs    — DI container setup, design-time and runtime (2 tests)
  UserSettingsManagerTests.cs    — Settings save/load/migrate (3 tests)
```

### Running tests
```
dotnet test adrilight.Tests/adrilight.Tests.csproj
```

### Building a local executable
```
dotnet publish adrilight/adrilight.csproj -c Release --self-contained false -o ./publish/adrilight-3.0.0
```
Output goes to `publish/adrilight-3.0.0/adrilight.exe` (~24MB, requires .NET 8 Desktop Runtime x64).
The `publish/` folder is excluded from git via `.gitignore`.

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
- Added `AssemblyVersion` and `AssemblyFileVersion` to `AssemblyInfo.cs` (now 3.0.0)
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

- Hooks directly into `UserSettings.TransferActive`
- Started in `App.OnStartup`, stopped in `App.OnExit`

**Session Lock / Unlock** (`App.xaml.cs`)
- `SystemEvents.SessionSwitch` handler automatically sets `TransferActive = false` on lock and restores previous state on unlock

**Screen Saver Detection** (`App.xaml.cs`)
- `DispatcherTimer` polls every 5 seconds using `SystemParametersInfo(SPI_GETSCREENSAVERRUNNING)`
- Automatically turns LEDs off when screen saver starts, restores state when it stops

### Performance Improvements
- Default `LimitFps` reduced from 60 to 30 in `UserSettings.cs`
- `SerialStream.DoWork()` — `minTimespan` now calculated once per port open rather than every frame
- `SerialStream.DoWork()` — removed unnecessary `GC.Collect()` from exception handler
- `DesktopDuplicatorReader` — removed unnecessary `GC.Collect()` from `GetNextFrame()` exception handler
- `DesktopDuplicatorReader` — `Parallel.ForEach` only used for spot counts ≥ 40; sequential loop used below threshold
- `DesktopDuplicatorReader` — `ProcessSpot()` extracted as separate method
- **Dirty Flag Optimisation** — `IsDirty` bool property added to `ISpotSet`, `SpotSet`, and `SpotSetFake`. Set to `true` by `DesktopDuplicatorReader` after each frame colour update. `SerialStream` only sends to the Arduino when `IsDirty` is `true`, then clears the flag — eliminates redundant serial writes when screen content is unchanged

---

## Session History

### 2026-03-16 — Dead code cleanup (branch: `cleanup/remove-dead-code`)

**Deleted files (fully unused):**
- `Messaging/TraceFrameMessage.cs` — unused struct, leftover from old implementation
- `DesktopDuplication/MovedRegion.cs` — unused struct, never instantiated
- `Extensions/MathExtensions.cs` — contained only `Clamp<T>()`, never called anywhere

**Removed unused methods:**
- `SpotSet.Offset()` — duplicated logic already inlined in `BuildSpots()`
- `StartUpManager.AddApplicationToAllUserStartup()` — only CurrentUser variants are used
- `StartUpManager.RemoveApplicationFromAllUserStartup()` — same
- `StartUpManager.IsUserAdministrator()` — never called
- `ViewModelLocator.Cleanup()` — was an empty TODO stub

**Removed stale properties from `UserSettingsFake`:**
- `LastUpdateCheck`, `LedsPerSpot`, `OffsetX`, `OffsetY` — not present in `IUserSettings` interface

**Bug fixes / code smell corrections:**
- `SettingsViewModel`: removed dead `PropertyChanged` handler that assigned `name` but never used it
- `SettingsViewModel`: `SpotsXMaximum` / `SpotsYMaximum` getters were mutating state — moved mutation into `Settings.PropertyChanged` handler; getters now just return the backing field
- `Spot`: removed empty `IDisposable` / `Dispose()` — `ISpot` does not require it
- `SerialStream`: tightened bare `catch {}` to `catch (UnauthorizedAccessException)`

**Commented-out code removed:**
- `FakeSerialPort.cs` — stale `_log.Warn(...)` line
- `NightLightDetection.cs` — stale `[VectorType(43)]` attribute

**Boilerplate using cleanup:**
- Stripped unused template-generated `using` statements from all 7 `SettingsWindowComponents` codebehind files
- Cleaned unused usings in `FakeSerialPort`, `NightLightDetection`, `StartupManager`, `UserSettingsFake`

**Build result:** 0 warnings, 0 errors.

---

### 2026-03-16 — Test project migrated to .NET 8 (branch: `cleanup/remove-dead-code`)

The test project (`adrilight.Tests`) was on legacy .NET 4.7.2 and could no longer run against the .NET 8 main project.

**Changes:**
- Rewrote `adrilight.Tests.csproj` as SDK-style targeting `net8.0-windows`
- Updated packages: `MSTest.TestAdapter/Framework` → 3.6.1, `Moq` → 4.20.72, added `Microsoft.NET.Test.Sdk` 17.11.1
- Deleted `app.config`, `packages.config`, `Properties/AssemblyInfo.cs`
- Removed leftover commented-out debug block in `SpotsetTests.cs`

**Test result:** 13/13 passed.

---

### 2026-03-16 — Rename Perry Edition → AbsenteeAtom Edition; bump to v3.0.0

- `WhatsNew.xaml` version string updated
- `AssemblyInfo.cs` version bumped 2.1.0 → 3.0.0, company `fabsenet` → `AbsenteeAtom`
- `UserSettingsFake` default `AdrilightVersion` updated to `3.0.0`
- `README.md` fully rewritten — hardware setup, software setup, TCP API docs, build instructions, credits

---

### 2026-03-16 — About page restructured (`WhatsNew.xaml`)

- Removed "Remote 7" product reference from TCP server description
- Added brief description of what adrilight does to the header card
- Added credits line (fabsenet, jasonpang)
- Reorganised changes into sections: Platform, New Features, Performance, Reliability
- Added TCP Control API quick-reference table as a third card

---

### 2026-03-16 — XAML theme resource fixes

**`LedSetup.xaml`**
- `Complete LED Count` number `TextBlock`: removed explicit `Foreground="{DynamicResource MaterialDesignSecondaryLightBrush}"` — was resolving to black in the active theme; now inherits `MaterialDesignBody` from the UserControl

**`LightingModeSetup.xaml`**
- Heart icon: `{StaticResource PrimaryHueLightBrush}` → `{DynamicResource PrimaryHueLightBrush}` — `StaticResource` for a theme brush resolves once at startup and won't follow theme changes

**`Whitebalance.xaml`**
- Night Light mode hyperlink: hardcoded `Foreground="#FF84C1FF"` → `{DynamicResource PrimaryHueMidBrush}` — light blue was invisible on light backgrounds

**Intentionally left unchanged:**
- Decorative icon colours in `Whitebalance.xaml` (NightSky/AutoAwesome blue, Brain/WhiteBalanceSunny gold) — semantic colour choices
- Preview canvas gradient (`#686868` → `#C2C2C2`) — functional backdrop for the LED overlay display
- "EXPERIMENTAL!" red warning text (`#FFFD7474`) — intentional

---

## Development Notes

- The app starts minimized to the system tray by default — check the tray if the window doesn't appear
- `StartMinimized` is a user setting on the General Setup tab; the settings window always opens on first run or after a version change
- Serial baud rate is hardcoded at `1,000,000` in `SerialStream.cs`
- The ML model for Night Light detection is embedded as a resource (`Resources/NightLightDetectionModel.zip`)
- SharpDX assemblies are referenced directly from the NuGet cache via HintPath — not via PackageReference — because the netstandard build lacks `AcquireNextFrame`
- The TCP control server listens on `127.0.0.1:5080`
- To revert all cleanup changes to the pre-session state: `git checkout main`
