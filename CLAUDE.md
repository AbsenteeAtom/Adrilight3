# adrilight — Claude Code Notes

## Project Overview

**adrilight** is a Windows desktop app (WPF, .NET 8.0, x64) that drives ambient LED lighting by capturing the screen via SharpDX/DXGI and sending colour data over a serial port to an Arduino-based LED controller.

### Key technologies
- WPF + Windows Forms, targeting `net8.0-windows`
- CommunityToolkit.Mvvm (ObservableObject, RelayCommand)
- Ninject for dependency injection
- NLog for logging
- SharpDX (DXGI / Direct3D11) for screen capture
- System.Reactive
- Microsoft.ML for Night Light mode detection
- MaterialDesignThemes v4

### Project layout
```
adrilight/
  App.xaml / App.xaml.cs         — entry point, DI setup
  DesktopDuplication/            — SharpDX screen capture
  Extensions/                    — ArrayExtensions (Swap)
  Fakes/                         — Design-time fake implementations
  Messaging/                     — (currently empty after cleanup)
  Settings/                      — IUserSettings interface + UserSettings impl
  Spots/                         — ISpot / Spot / ISpotSet / SpotSet
  Util/                          — SerialStream, NightLightDetection, FakeSerialPort, etc.
  ValidationRules/               — WPF validation
  View/                          — XAML views + SettingsWindowComponents
  ViewModel/                     — SettingsViewModel, ViewModelLocator
```

---

## Change History

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
- `SettingsViewModel`: `SpotsXMaximum` / `SpotsYMaximum` getters were mutating state (`_field = Math.Max(...)`) — moved the mutation into the `Settings.PropertyChanged` handler where the notifications already fire; getters now just return the backing field
- `Spot`: removed empty `IDisposable` / `Dispose()` — `ISpot` does not require it
- `SerialStream`: tightened bare `catch {}` to `catch (UnauthorizedAccessException)`

**Commented-out code removed:**
- `FakeSerialPort.cs` — stale `_log.Warn(...)` line
- `NightLightDetection.cs` — stale `[VectorType(43)]` attribute

**Boilerplate using cleanup:**
- Stripped 12 unused template-generated `using` statements from all 7 `SettingsWindowComponents` codebehind files (`GeneralSetup`, `ComPortSetup`, `LedSetup`, `SpotSetup`, `LightingModeSetup`, `Whitebalance`, `Preview`)
- Cleaned unused usings in `FakeSerialPort`, `NightLightDetection`, `StartupManager`, `UserSettingsFake`

**Build result:** 0 warnings, 0 errors after all changes.

**To revert:** `git checkout main`

---

### Earlier changes (Perry Edition, v2.1.0)

| Commit | Summary |
|--------|---------|
| `9906f0c` | Fix MaterialDesign v4 Accent compatibility across all XAML pages |
| `a1f62a7` | Final v2.1.0 - Perry Edition |
| `f664af7` | Add session lock and screen saver LED auto off/on |
| `964c28d` | Auto-copy icon to output directory on build |
| `6262534` | Add About page, fix StartMinimized, set version 2.1.0 |
| `81b1468` | Add dirty flag optimisation, reduce unnecessary serial writes and GPU usage |
| `b482434` | Performance improvements to SerialStream and DesktopDuplicatorReader |
| `b0f3092` | Migrate to .NET 8, replace MvvmLight, add TCP control server on port 5080 |

---

## Development Notes

- The app may start minimized to the system tray — check the tray if the window doesn't appear
- `StartMinimized` is a user setting controlled from the General Setup tab
- Serial baud rate is hardcoded at `1,000,000` in `SerialStream.cs`
- The ML model for Night Light detection is embedded as a resource (`Resources/NightLightDetectionModel.zip`)
- SharpDX assemblies are referenced directly from the NuGet cache (not via PackageReference) — paths use `$(USERPROFILE)\.nuget\packages\sharpdx\...`
- The TCP control server listens on port 5080
