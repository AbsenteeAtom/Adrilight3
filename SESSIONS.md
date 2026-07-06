# adrilight — Session History

Chronological log of Claude Code work sessions: what was built, root causes found, fixes applied, and test counts per version. Moved out of CLAUDE.md (2026-07-06) so it is not loaded into every session's context. New entries are appended by the `/ship` skill (newest releases should go at the top of the list below).

Note: entry order below is as historically appended and is not strictly chronological.

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

### 2026-07-01 — Locked bitmap crash fix (v3.7.5)
1. **Root cause:** The startup dark-frame skip added in v3.7.3 used `continue` inside the `lock (SpotSet.Lock)` block to skip dark frames. `continue` jumped back to the top of the `while` loop before `image.UnlockBits(bitmapData)` (line 241, after the lock block). On the next iteration, `GetLatestFrame` called `image.LockBits` again inside `ProcessFrame` — which threw `InvalidOperationException: "Bitmap region is already locked"`. The retry policy caught and retried forever, flooding Diagnostics with errors and preventing LED updates for the entire session.
2. **Fix:** Replaced `continue` with a `bool skipFrame` flag set inside the lock. `image.UnlockBits(bitmapData)` is now always called after the lock block, then `if (skipFrame) continue` skips the frame time sleep. `SpotSet.IsDirty`, `_logNextFrame`, and `PreviewSpots` are only set when `!skipFrame`.
3. No new tests — the crash path is inside the hardware-bound capture loop.
4. Version bumped to 3.7.5.

### 2026-06-26 — SerialPort close crash fix (v3.7.4)
1. **Root cause:** `System.IO.Ports.SerialStream.Dispose()` throws `OperationCanceledException` when closed while async I/O is still in-flight — a known .NET quirk. In `SerialStream.DoWork()`, the `catch (OperationCanceledException)` handler at the inner try/catch level would call `return`, which triggered the `finally` block. Inside the `finally`, `serialPort.Close()` threw a second `OperationCanceledException`. Exceptions thrown from a `finally` block bypass the enclosing `catch` blocks (those had already run), so it escaped `DoWork` as an unhandled exception on the background thread and crashed the process (`CurrentDomain.UnhandledException`).
2. **Fix:** Wrapped the `finally` block body in its own `try/catch (OperationCanceledException)` to swallow the port teardown exception. Any other exception type still propagates normally.
3. No new tests — the crash path is inside the hardware-bound serial port teardown.
4. Version bumped to 3.7.4.

### 2026-03-31 — Screensaver error suppressed (v3.7.3)
1. **Root cause identified:** A spurious `GetNextFrame() failed` ERROR appeared in the Diagnostics log each time the screensaver activated. DXGI raises `DXGI_ERROR_ACCESS_LOST` (0x887A0026) the instant the display session changes — before the 5-second screensaver poll fires the inhibitor. The error is expected and self-recovering.
2. **Fix:** Both `catch` blocks in `GetNextFrame()` (single-monitor and spanning paths) now inspect `ex.InnerException as SharpDXException` for HRESULT `0x887A0026`. If matched, the log is demoted to `Debug` (invisible in Diagnostics tab). Any other HRESULT still logs at `Error`.
3. No new tests — entirely in the hardware-bound capture path.
4. Version bumped to 3.7.3.

### 2026-03-30 — Spanning toggle bug fix (v3.7.2)
1. **Root cause identified:** Toggling `SpanningEnabled` on or off caused `GetNextFrame() failed` errors on the next capture cycle. Creating or destroying a second `DuplicateOutput` session on the same DXGI adapter invalidates the primary session, so `_desktopDuplicator` would throw on the next frame even though its indices hadn't changed.
2. **Fix:** Split `SpanningEnabled` into its own `case` in `DesktopDuplicatorReader.PropertyChanged`. When `SpanningEnabled` changes, both `_desktopDuplicator` and `_desktopDuplicator2` are disposed and nulled, so both are rebuilt cleanly on the next `GetNextFrame()` call. `AdapterIndex2`/`OutputIndex2` changes continue to dispose only the secondary duplicator (the primary is unaffected when only the second monitor's indices change).
3. No new tests — the fix is entirely in the hardware-bound `GetNextFrame()` path.
4. Version bumped to 3.7.2.

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

## Four Architecture Refactoring Fixes (applied before v3.1.0 feature work)

### 1 — BGR colour order documented
`SerialStream.cs` had silent BGR→RGB conversion with no comments. Added named constants and in-line comments at both write sites so future maintainers cannot accidentally introduce RGB writes.

### 2 — Baud rate moved to `IUserSettings`
Previously hardcoded as a local `const` in `SerialStream.DoWork()`. Now stored in `UserSettings.BaudRate` (default 1,000,000) so it can be changed without a rebuild. `SerialStream` tracks `openedBaudRate` and reopens the port when the value changes.

### 3 — `IsDirty` flag cleared atomically
Previously `IsDirty = false` was set *outside* the `SpotSet.Lock` after reading spot colours. This created a race window where a new frame could set the flag between the read and the clear, causing that frame to be skipped. Fixed by moving the clear *inside* the lock, before the colour read.

### 4 — Version migration moved to `UserSettingsManager`
Migration logic (v1→v2 SpotsY adjustment) had lived in `App.xaml.cs` alongside startup code. Extracted into `UserSettingsManager.ApplyMigrations(IUserSettings settings)`, called from `LoadIfExists()` after deserialization. Future migrations go in the same method as a clear chain of `if (ConfigFileVersion == N)` blocks.
