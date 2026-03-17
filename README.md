# adrilight

![adrilight logo](assets/adrilight_icon.jpg)

> An Ambilight clone for Windows — lights up LEDs behind your screen in real time by sampling screen colours

**adrilight 3.0.0 — AbsenteeAtom Edition**
Forked from [fabsenet/adrilight](https://github.com/fabsenet/adrilight) v2.0.9 (the final upstream release).
The original author retired the project; this fork modernises it for .NET 8 and adds new features.

---

## What does it do?

adrilight reads your Windows screen content using the Desktop Duplication API, calculates the average colour in each zone (spot) around the edge of the screen, and sends those colours to an Arduino over USB. The Arduino drives a WS2812b LED strip attached to the back of your monitor or TV:

```
PC (adrilight.exe)  →  Arduino (adrilight.ino)  →  WS2812b LED strip
```

---

## What's new in 3.0.0

Compared to the original fabsenet v2.0.9:

- **.NET 8** — migrated from .NET Framework 4.7.2; faster startup, lower CPU and GPU usage
- **TCP control server** — send `ON`, `OFF`, `TOGGLE`, `STATUS` or `EXIT` commands to `127.0.0.1:5080` from any local app or script
- **Session lock / unlock** — LEDs turn off automatically when you lock Windows and back on when you unlock
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
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64)

**Hardware:**
- Arduino UNO or compatible
- WS2812b LED strip (length to suit your screen)
- 5V DC power supply — at least 1A per 50 LEDs
- USB cable (Arduino to PC)

---

## Hardware Setup

1. Attach the LED strip to the back of your monitor or TV. The data arrows must form a continuous loop around the screen.
2. Divide the strip into four sides: Top, Right, Bottom, Left — each side must have an equal number of LEDs on opposite sides (Top = Bottom count, Left = Right count).
3. Solder corners together but **do not** close the data loop — it must have a clear start and end point.
4. For longer strips, add additional power injection wires at intervals.
5. Connect the strip's **Data In** and **Ground** to the Arduino.
6. Connect the **5V power supply** to the strip (not through the Arduino).

> ⚠️ Never power more than a small number of LEDs directly from the Arduino's 5V pin.

---

## Software Setup

### 1. Arduino

1. Open the Arduino IDE.
2. Install the **FastLED** library (`Sketch → Include Library → Manage Libraries`).
3. Open `Arduino/adrilight.ino` from this repository.
4. Edit the constants at the top of the sketch to match your LED count and data pin.
5. Upload to the Arduino.

### 2. adrilight (PC)

1. Download the latest release or build from source (see below).
2. Run `adrilight.exe` — it starts in the system tray.
3. Double-click the tray icon (or right-click → Open) to open the settings window.
4. **Serial Communication Setup** — select your Arduino's COM port and enable Transfer.
5. **Physical LED Setup** — enter the number of LEDs across the top/bottom (`SpotsX`) and down each side (`SpotsY`).
6. **Spot Detection Setup** — adjust border distance and spot size to match your screen.
7. **Lighting Mode** — choose between normal and linear lighting.
8. **White Balance** — tune the RGB balance of your LEDs.
9. Adjust the `Offset LED` value until the colours on screen align with the correct physical LED positions.

The app saves settings automatically. On next launch it starts in the system tray without opening the settings window (configurable in General Setup).

---

## TCP Control Server

adrilight listens on `127.0.0.1:5080` for plain-text commands. This lets external applications (home automation, remote control software, scripts) control the LEDs without any UI interaction.

Send a newline-terminated ASCII command; receive a JSON response:

| Command  | Effect                              | Response               |
|----------|-------------------------------------|------------------------|
| `ON`     | Turn LEDs on                        | `{"status":"on"}`      |
| `OFF`    | Turn LEDs off                       | `{"status":"off"}`     |
| `TOGGLE` | Toggle current on/off state         | `{"status":"on/off"}`  |
| `STATUS` | Query current state                 | `{"status":"on/off"}`  |
| `EXIT`   | Gracefully shut down adrilight      | `{"status":"exiting"}` |

**Example (PowerShell):**
```powershell
$client = New-Object System.Net.Sockets.TcpClient("127.0.0.1", 5080)
$stream = $client.GetStream()
$writer = New-Object System.IO.StreamWriter($stream)
$reader = New-Object System.IO.StreamReader($stream)
$writer.WriteLine("STATUS")
$writer.Flush()
$reader.ReadLine()   # returns {"status":"on"} or {"status":"off"}
$client.Close()
```

---

## Building from Source

Requirements: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (x64), Visual Studio 2022 or later (optional).

```bash
git clone <your-repo-url>
cd adrilight
dotnet build adrilight/adrilight.csproj --configuration Release
```

**Run tests:**
```bash
dotnet test adrilight.Tests/adrilight.Tests.csproj
```

---

## Known Limitations

- Screen resolution changes after launch are not detected automatically — restart adrilight if you change resolution.
- Some applications block Desktop Duplication capture and will appear black:
  - Netflix Windows Store app (browser-based Netflix works fine)
  - UAC prompts
  - Some DRM-protected content
- Arduino clones that cannot reliably reach **1,000,000 baud** will not work. The high baud rate is required to achieve smooth LED updates and cannot be reduced.
- Only the primary monitor is captured.

---

## Credits

- [fabsenet/adrilight](https://github.com/fabsenet/adrilight) — original project (MIT), versions up to 2.0.9
- [MrBoe/Bambilight](https://github.com/MrBoe/Bambilight) — the original ambilight clone adrilight was forked from
- [jasonpang/desktop-duplication-net](https://github.com/jasonpang/desktop-duplication-net) — Desktop Duplication API sample code

---

## Licence

[MIT](LICENSE) — original copyright © 2016 Fabian Wetzel.
