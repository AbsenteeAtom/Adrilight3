# adrilight

![adrilight logo](assets/adrilight_icon.jpg)

> An Ambilight clone for Windows — lights up LEDs behind your screen in real time by sampling screen colours

**adrilight 3.6.2 — AbsenteeAtom Edition**
Forked from [fabsenet/adrilight](https://github.com/fabsenet/adrilight) v2.0.9 (the final upstream release).
The original author retired the project; this fork modernises it for .NET 8 and adds new features.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-blueviolet)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%2B%20x64-blue)]()

---

## Contents

- [What does it do?](#what-does-it-do)
- [What's new](#whats-new)
- [Requirements](#requirements)
- [Hardware Setup](#hardware-setup)
- [Software Setup](#software-setup)
- [TCP Control Server](#tcp-control-server)
- [Building from Source](#building-from-source)
- [Known Limitations](#known-limitations)
- [Credits](#credits)

---

## What does it do?

adrilight reads your Windows screen content using the Desktop Duplication API (DXGI), calculates the average colour in each zone (spot) around the edge of the screen, and sends those colours to an Arduino over USB. The Arduino drives a WS2812b LED strip attached to the back of your monitor or TV:

```
PC (adrilight.exe)  →  USB  →  Arduino (adrilight.ino)  →  WS2812b LED strip
```

The result is a responsive ambient lighting effect that matches whatever is on screen in real time.

---

## What's new

### 3.6.2

- **All settings changes logged to Diagnostics tab** — every user setting change now appears in the Diagnostics tab in real time, showing the property name and new value

### 3.6.1

- **Sound to Light: beat-triggered reshuffles now fire reliably** — the previous beat detection compared the raw bass level against a smoothed running average. The smoother caught up within ~300 ms, after which the threshold was always above the signal and reshuffles never fired after the first few seconds of music. Replaced with a simple sensitivity-scaled fixed floor threshold; the rate limit (once per second) prevents over-triggering
- **Sound to Light: surround/7.1 audio devices now work correctly** — WASAPI loopback on surround devices reports multiple channels (e.g. 8 for 7.1), but stereo content from browsers and media players only populates the front-left and front-right channels. Mixing all channels was diluting the mono signal by up to 4×, making beat detection fail silently. The mono mix is now capped at 2 channels (front-L + front-R) regardless of the device channel count
- **Beat events visible in Diagnostics tab** — each reshuffle logs a `Beat detected` entry at Info level, visible in the Diagnostics tab in real time

### 3.6.0

- **Sound to Light mode** — new audio-reactive lighting pipeline using WASAPI loopback capture (no microphone required). A 1024-sample Hann-windowed FFT divides audio into 32 logarithmically-spaced bands from 20 Hz to 20 kHz. Each LED is randomly assigned a frequency band; band colours follow the visible spectrum — bass glows red and orange, mids shift through yellow and green, treble lights up cyan and blue, and the highest frequencies show violet. A strong bass hit reshuffles all LED assignments for a dynamic, ever-changing pattern
- **Sensitivity and Smoothing controls** — new Sound to Light tab with sliders for Sensitivity (audio-to-brightness gain), Smoothing (attack/decay envelope), and per-channel RGB Gain (Red, Green, Blue; range 0–2) to tune colour balance for your specific LED strip. All settings persist across sessions
- **Lighting mode selector** — General Setup now has a mode selector card (Screen Capture / Sound to Light). The active mode is also switchable from the system tray context menu — all available modes are listed with a check mark on the current one
- **Diagnostics: Copy log button** — the Diagnostics tab now has a "Copy log" button that copies all visible log entries to the clipboard as plain text

### 3.5.0

- **LEDs now resume correctly in all sleep/lock/screen-saver combinations** — a bug in earlier versions could leave the LEDs off after unlocking Windows if the screen saver had also activated while the session was locked. The session-lock and screen-saver inhibitors previously shared a single saved-state variable; whichever fired second would overwrite what the first had saved. Each inhibitor source is now tracked independently, so all of them must clear before the LEDs come back on, and the original on/off state is always preserved correctly
- **Mode manager foundation** — an internal mode manager has been introduced to serve as the clean switching point for future lighting modes. The app continues to run in Screen Capture mode as it always has; the architecture is now in place to add Sound to Light and Gamer Mode without restructuring existing code
- **TCP STATUS includes active mode** — the `STATUS` command now returns `{"status":"on","mode":"screen"}` so external integrations (such as Remote 7) can read both state and mode in one call. New `MODE SCREEN`, `MODE SOUND`, `MODE GAMER`, and `MODE STATUS` commands are also available for future use

### 3.4.1

- **White balance slider corruption fix** — all six white balance setters now clamp values to [1, 100]; a MaterialDesign discrete-slider layout quirk could write back out-of-range values in some sessions. A settings migration automatically repairs any previously saved corrupt value on first launch
- **White Balance page icon alignment** — mode icons (Forced On / Auto detect / Forced Off) are now correctly aligned with their label text; the grid row heights were changed from proportional to auto and icon margins unified
- **Test isolation** — `UserSettingsManager` and `App.SetupDependencyInjection` now accept an optional settings folder parameter so tests run in isolated temp directories and never touch the real settings file

### 3.4.0

- **Night Light detection rewritten** — state is now read directly from the Windows registry (byte 18 of the CloudStore REG_BINARY blob); result is a definitive ON / OFF / Unknown with no ML model, no probability scores, and no uncertainty warnings
- **Unknown state on missing key** — if the registry key is absent (Night Light has never been configured), the Diagnostics tab shows "Night Light: Unknown" and logs a warning rather than silently assuming Off
- **Removed Microsoft.ML dependency** — 8+ ML DLLs removed from the publish folder; significantly smaller download

### 3.3.1

- **Status indicator visibility fix** — toolbar indicator changed from a coloured icon to a solid circle with a black outline, ensuring it is visible against the orange UI background
- **Corrected baud rate release note** — baud rate is an internal setting, not yet exposed in the UI; release notes and About page corrected

### 3.3.0

- **Diagnostics tab** — new in-app log viewer showing the last 200 log entries, filterable by All / Warnings+ / Errors+; Mark as read button resets the indicator
- **Toolbar status indicator** — unobtrusive icon in the top bar turns amber on warnings or red on errors; tooltip describes the state; click jumps straight to the Diagnostics tab
- **Night Light detection status** — Diagnostics tab shows the current Night Light state in real time

### 3.2.1

- **Logging fix** — log files were not being created in the published build because .NET 8 silently ignores the NLog configuration in `App.config`. Logging is now configured in code and confirmed working. Log files are written to a `logs\` folder next to `adrilight.exe`

### 3.2.0

- **Sleep / wake awareness** — LEDs pause automatically when the PC sleeps or hibernates and restore when it wakes; toggled independently via the Sleep/Wake Awareness setting

### 3.1.0

- **Black bar detection** — letterbox and pillarbox bars detected each frame using a fast sparse edge scan; LEDs over black bar regions are remapped to sample the nearest picture content edge rather than being turned off — all LEDs remain active and reflect real colours at all times
- **Serial baud rate refactored** — baud rate moved from a hardcoded constant to an internal user setting (default 1,000,000); the Arduino sketch must still be flashed to match
- **Settings migration centralised** — version upgrade logic extracted to `UserSettingsManager.ApplyMigrations()`; future migrations go in one place, not in startup code

### 3.0.0

Compared to the original fabsenet v2.0.9:

- **.NET 8** — migrated from .NET Framework 4.7.2; faster startup, lower CPU and GPU usage
- **TCP control server** — send `ON`, `OFF`, `TOGGLE`, `STATUS` or `EXIT` commands to `127.0.0.1:5080` from any local app or script
- **Session lock / unlock** — LEDs turn off automatically when you lock Windows and restore when you unlock
- **Screen saver detection** — LEDs turn off when the screen saver starts and restore when it stops
- **Dirty flag optimisation** — serial data is only sent to the Arduino when screen colours have actually changed, eliminating redundant writes during static content
- **Default 30 fps** — reduced from 60 fps; significantly lower GPU usage with no visible difference on LEDs
- **Removed Squirrel auto-updater** — eliminated source of antivirus false positives
- **MaterialDesignThemes 4.9.0** — updated from 2.x; modernised UI throughout
- **CommunityToolkit.Mvvm** — replaced abandoned MvvmLight
- **About page** — replaces the old browser-based What's New view with a static in-app page

---

## Requirements

**PC:**
- Windows 10 or later (x64)
- [.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/8.0)

**Hardware:**
- Arduino UNO or compatible
- WS2812b LED strip (length to suit your screen)
- 5V DC power supply — at least 1A per 50 LEDs
- USB cable (Arduino to PC)

---

## Hardware Setup

> A more comprehensive installation guide — covering power supply sizing, LED corner connectors, wiring best practices, and step-by-step Arduino configuration — can be found in [INSTALLATION.md](INSTALLATION.md).

1. Attach the LED strip to the back of your monitor or TV. The data arrows must form a continuous loop around the screen.
2. Divide the strip into four sides: Top, Right, Bottom, Left — opposite sides must have equal LED counts (Top = Bottom, Left = Right).
3. Solder corners together but **do not** close the data loop — it must have a clear start and end point.
4. For longer strips, add additional power injection wires at intervals to avoid colour shift near the end of the strip.
5. Connect the strip's **Data In** and **Ground** to the Arduino (default data pin: **D3**).
6. Connect the **5V power supply directly to the strip** — do not power the strip through the Arduino.

> **Warning:** Never power more than a handful of LEDs directly from the Arduino's 5V pin. Always use an external supply.

---

## Software Setup

### 1. Flash the Arduino

1. Open the Arduino IDE.
2. Install the **FastLED** library (`Sketch → Include Library → Manage Libraries → search FastLED`).
3. Open `Arduino/adrilight/adrilight.ino` from this repository.
4. Edit the constants at the top of the sketch:

```cpp
#define NUM_LEDS (2*73+2*41)   // adjust to match your actual LED count per side
#define LED_DATA_PIN 3          // change if you used a different pin
#define BRIGHTNESS 255          // 0–255; 255 = full brightness
```

5. Upload to the Arduino.

### 2. Run adrilight on your PC

1. Download the latest release or [build from source](#building-from-source).
2. Run `adrilight.exe` — it starts minimised to the system tray.
3. Double-click the tray icon (or right-click → Open) to open the settings window.
4. **Serial Communication Setup** — select your Arduino's COM port and enable Transfer.
5. **Physical LED Setup** — enter the number of LEDs across the top/bottom (`SpotsX`) and down each side (`SpotsY`).
6. **Spot Detection Setup** — adjust border distance and spot size to match your screen.
7. **White Balance** — tune the RGB balance of your LEDs if colours look off.
8. Adjust the `Offset LED` value until the colours align with the correct physical LED positions.

The app saves settings automatically. On subsequent launches it starts silently in the tray (configurable in General Setup).

---

## TCP Control Server

adrilight listens on `127.0.0.1:5080` (loopback only) for plain-text commands. This lets external applications, home automation tools, or scripts control the LEDs without any UI interaction.

Send a newline-terminated ASCII command; receive a JSON response:

| Command       | Effect                              | Response                              |
|---------------|-------------------------------------|---------------------------------------|
| `ON`          | Turn LEDs on                        | `{"status":"on"}`                     |
| `OFF`         | Turn LEDs off                       | `{"status":"off"}`                    |
| `TOGGLE`      | Toggle current on/off state         | `{"status":"on"}` or `{"status":"off"}` |
| `STATUS`      | Query state and active mode         | `{"status":"on","mode":"screen"}`     |
| `MODE SCREEN` | Switch to screen capture mode       | `{"status":"ok","mode":"screen"}`     |
| `MODE SOUND`  | Switch to sound reactive mode       | `{"status":"ok","mode":"sound"}`      |
| `MODE GAMER`  | Switch to gamer mode                | `{"status":"ok","mode":"gamer"}`      |
| `MODE STATUS` | Query active mode only              | `{"mode":"screen"}`                   |
| `EXIT`        | Gracefully shut down adrilight      | `{"status":"exiting"}`                |

Commands are case-insensitive. Mode values in responses are always lowercase: `screen`, `sound`, `gamer`.

> **Note:** `MODE SOUND` switches to the Sound to Light pipeline (implemented in 3.6.0). `MODE GAMER` switches the reported mode but the LEDs will continue to use screen capture until Gamer Mode is implemented.

**Example (PowerShell):**
```powershell
$client = New-Object System.Net.Sockets.TcpClient("127.0.0.1", 5080)
$stream  = $client.GetStream()
$writer  = New-Object System.IO.StreamWriter($stream)
$reader  = New-Object System.IO.StreamReader($stream)
$writer.WriteLine("STATUS")
$writer.Flush()
$reader.ReadLine()   # returns {"status":"on","mode":"screen"}
$client.Close()
```

---

## Building from Source

**Requirements:** [.NET 8 SDK (x64)](https://dotnet.microsoft.com/download/dotnet/8.0)

```bash
git clone https://github.com/AbsenteeAtom/Adrilight3.git
cd Adrilight3
dotnet build adrilight/adrilight.csproj --configuration Release
```

**Run tests:**
```bash
dotnet test adrilight.Tests/adrilight.Tests.csproj
```

**Publish a local executable:**
```bash
dotnet publish adrilight/adrilight.csproj -c Release --self-contained false -o ./publish/adrilight-3.6.0
```

Output goes to `publish/adrilight-3.6.0/adrilight.exe` (~24 MB). Requires .NET 8 Desktop Runtime x64 on the target machine.

---

## Known Limitations

- Resolution changes after launch are not detected automatically — restart adrilight if you change display resolution.
- Some applications block Desktop Duplication capture and will appear black:
  - Netflix Windows Store app (browser-based Netflix works fine)
  - UAC prompts
  - Some DRM-protected content
- Arduino clones that cannot reliably reach **1,000,000 baud** will not work. The high baud rate is required for smooth LED updates.
- Only the primary monitor is captured.

---

## Credits

- [fabsenet/adrilight](https://github.com/fabsenet/adrilight) — original project (MIT), versions up to 2.0.9
- [MrBoe/Bambilight](https://github.com/MrBoe/Bambilight) — the original ambilight clone adrilight was forked from
- [jasonpang/desktop-duplication-net](https://github.com/jasonpang/desktop-duplication-net) — Desktop Duplication API sample code

---

## Licence

[MIT](LICENSE) — original copyright © 2016 Fabian Wetzel.
