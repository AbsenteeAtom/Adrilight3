# Adrilight Installation Guide

This guide walks you through everything you need to get Adrilight running — from wiring your LED strip to configuring the software. No prior experience required.

---

## Contents

- [What you need](#what-you-need)
- [LED strip layout](#led-strip-layout)
- [Wiring](#wiring)
- [Setting up the Arduino](#setting-up-the-arduino)
- [Finding your COM port](#finding-your-com-port)
- [Installing and running Adrilight](#installing-and-running-adrilight)
- [Basic configuration](#basic-configuration)
- [Troubleshooting](#troubleshooting)

---

## What you need

**Hardware:**

- **Arduino** — An Arduino Mega or UNO are both suitable. The Mega is a popular choice as it has more pins and is easy to work with.
- **WS2812B LED strip** — Available from most electronics retailers. Buy a density of 30 or 60 LEDs per metre; 60/m gives better colour blending. Make sure it is specifically **WS2812B** (individually addressable RGB).
- **5V power supply** — The LED strip draws up to **40mA per LED** at full white brightness. To size your power supply:
  > **Number of LEDs × 40mA = minimum current rating**
  >
  > Example: 200 LEDs × 40mA = 8,000mA = **8A minimum**
  >
  > Round up and buy the next standard size (e.g. a 10A supply for 200 LEDs). A supply that is too small will cause flickering, colour shifts, or a tripped overload.
- **PC running Windows 10 or later (64-bit)**
- **USB cable** — to connect the Arduino to your PC
- A few short lengths of wire for the data and power connections

**Software (free):**

- [.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/8.0) — required to run Adrilight
- [Arduino IDE](https://www.arduino.cc/en/software) — required to flash the Arduino sketch

---

## LED strip layout

The LED strip runs around the back of your TV or monitor in a single continuous loop, close to the outer edge. The LEDs face inward toward the wall so the light reflects off the surface behind the screen.

**Planning the layout:**

1. Measure each side of your screen and calculate how many LEDs fit at your chosen density.
2. The top and bottom must have the **same number of LEDs**. The left and right sides must also have the **same number of LEDs**. Adrilight mirrors opposite sides, so unequal counts will cause misalignment.
3. Decide where the strip will **start and end** — the bottom-left corner is a common choice. The start and end of the strip are the only point where the data signal enters; they do not connect to each other.

**Turning corners:**

Bending a WS2812B strip sharply can crack the copper traces and kill LEDs from that point onward. **90-degree corner connector pieces made specifically for WS2812B strips** are available from LED strip suppliers and are highly recommended. They clip onto the strip pads without soldering, make a neat right angle, and eliminate the risk of damage. Search for *"WS2812B corner connector"* or *"LED strip L-shaped connector"*.

---

## Wiring

> **Warning: never attempt to power the LED strip from the Arduino's 5V pin.** The Arduino can only supply around 500mA. Even a small number of LEDs at full brightness will exceed this, potentially damaging the Arduino and the USB port on your PC.

**Power wiring:**

Connect the 5V power supply directly to the LED strip using short, adequately rated wire. To ensure even voltage across the entire strip and prevent a brightness drop toward the far end:

- Connect **positive (+5V) and ground (GND) leads at both ends of the strip**, or at opposite corners if running around a screen.
- This is called *power injection* — it ensures current reaches all LEDs from two directions rather than travelling the full length of the strip from one end.

You should also connect the **GND of the power supply to the GND pin of the Arduino** so they share a common reference.

**Data wiring:**

- Connect the **data wire from Arduino pin D3** (the default) to the **Data In pad at the start of your strip**.
- Connect **only at one point**. The strip has a directional data signal; it must enter at one end only.
- **Important:** There must be a **break in the copper trace** at the point where your strip ends, to prevent the data signal from looping back around the strip in the wrong direction. If you are using corner connectors, the break happens naturally at the join. If your strip is one continuous piece, cut it cleanly at the end point and do not reconnect the data line.
- A **330–470 ohm resistor** in series on the data wire (between the Arduino pin and the strip) is good practice — it protects against signal reflections and static damage.

---

## Setting up the Arduino

1. **Download and install the Arduino IDE** from [arduino.cc/en/software](https://www.arduino.cc/en/software).

2. **Install board support** (if using a non-UNO board):
   - Open the IDE, go to *Tools → Board → Boards Manager*
   - Search for your board (e.g. *Arduino AVR Boards* covers the UNO and Mega) and install it.

3. **Install the FastLED library:**
   - Go to *Sketch → Include Library → Manage Libraries*
   - Search for **FastLED** and install it.

4. **Open the Adrilight sketch:**
   - In the release zip you downloaded, open the file at `Arduino\adrilight\adrilight.ino` in the Arduino IDE.

5. **Configure the sketch** — edit the constants at the very top of the file:

   ```cpp
   // Number of LEDs across the top (and bottom — must be equal)
   #define LEDS_ON_TOP    30

   // Number of LEDs down each side (left and right — must be equal)
   #define LEDS_ON_SIDE   18

   // Arduino data pin connected to the strip's Data In
   #define LED_DATA_PIN   3

   // Maximum brightness (0 = off, 255 = full). Start at 255 and
   // fine-tune later using the White Balance settings in Adrilight.
   #define BRIGHTNESS     255
   ```

   > The total LED count is calculated automatically from these values. Do not hard-code a total — edit the top and side counts individually.

6. **Select your board and port:**
   - *Tools → Board* — choose your Arduino model
   - *Tools → Port* — choose the COM port your Arduino is connected to (see [Finding your COM port](#finding-your-com-port) below)

7. **Upload the sketch** — click the Upload button (right-pointing arrow). Wait for *"Done uploading"*. The LEDs may flash briefly to confirm the flash was successful.

---

## Finding your COM port

Windows assigns a COM port number to your Arduino automatically when you plug it in.

1. Plug the Arduino into your PC via USB.
2. Right-click the **Start** button and choose **Device Manager**.
3. Expand the **Ports (COM & LPT)** section.
4. You should see an entry like **USB Serial Device (COM3)** or **Arduino Mega 2560 (COM4)**. The number in brackets is your COM port.

> If you do not see it, try a different USB cable or USB port. Some cheap Arduino clones use a CH340 USB chip — you may need to install a [CH340 driver](https://www.wch-ic.com/downloads/CH341SER_EXE.html) for Windows to recognise it.

---

## Installing and running Adrilight

1. Download **adrilight-3.2.1.zip** from the [Releases page](https://github.com/AbsenteeAtom/Adrilight3/releases).
2. Extract the zip to a permanent location — for example `C:\Program Files\adrilight` or a folder on your desktop. Avoid extracting to the Downloads folder directly, as files there may be cleaned up automatically.
3. Install [.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/8.0) if you have not already.
4. Run **adrilight.exe**. The application starts minimised to the **system tray** (the icon area in the bottom-right corner of the taskbar next to the clock).
5. **Double-click the tray icon** to open the settings window.

> On first run the settings window opens automatically regardless of the Start Minimised setting.

---

## Basic configuration

Open the settings window and work through each tab:

**Serial Communication Setup**
- Select the **COM port** you identified in Device Manager.
- Click the **Enable Sending** toggle to start sending colour data to the Arduino. The LEDs should light up.

**Physical LED Setup**
- Set **SpotsX** to the number of LEDs across the **top** of your screen.
- Set **SpotsY** to the number of LEDs down each **side** of your screen.
- Set the **Offset LED** value to shift the starting position around the strip until the colours on-screen match the correct physical LED positions.

**Spot Detection Setup**
- Adjust **Border Distance** to move the sampling zones closer to or further from the screen edge.
- Adjust **Spot Width** and **Spot Height** to change the size of each sampling zone.

**White Balance**
- Use the sliders to correct any colour cast. If whites look warm, reduce Red slightly. If blues are too strong, reduce Blue.
- The **Brightness** setting in the Arduino sketch (`#define BRIGHTNESS`) sets the hardware ceiling; the white balance sliders fine-tune within that.

**General Setup**
- Enable **Start Minimised** once everything is working so Adrilight loads silently at startup.
- Enable **Autostart with Windows** to have Adrilight start automatically when you log in.
- **Black Bar Detection** — leave this on. It prevents LEDs over letterbox/pillarbox bars from going dark during widescreen content.
- **Sleep/Wake Awareness** — leave this on. LEDs will pause when your PC sleeps and restore when it wakes.

---

## Troubleshooting

**No LEDs light up at all**
- Check that **Enable Sending** is toggled on in Serial Communication Setup.
- Confirm the correct COM port is selected — unplug and replug the Arduino and check Device Manager again.
- Verify the data wire is connected to the correct Arduino pin (default: D3) and to the **Data In** end of the strip (not Data Out).
- Check that the power supply is switched on and connected correctly.

**Only the first few LEDs light up, then nothing**
- The power supply current rating is too low. Check your calculation (LEDs × 40mA) and replace with a higher-rated supply.
- Check your solder joints or connector clips at the point where the LEDs stop.

**LEDs light up but colours are wrong or shifted**
- Adjust the **Offset LED** value in Physical LED Setup to rotate the starting point around the strip.
- If red and blue are swapped, the strip may be RGB rather than BGR — try enabling **Use Linear Lighting** or adjust White Balance.

**Colours lag noticeably behind the screen**
- Increase **Limit FPS** in General Setup (default is 30; try 60).
- Check that no other application is using the COM port.

**LEDs stay on when the screen is off or PC is locked**
- Enable **Sleep/Wake Awareness** and confirm session lock detection is working (it is always on).

**The settings window does not appear**
- Look in the **system tray** — double-click the adrilight icon.
- If the icon is not visible, click the **^** arrow on the taskbar to reveal hidden tray icons.

**Adrilight crashes on startup**
- Confirm you have installed [.NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/8.0).
- Check that no other instance is already running in the tray.

---

*For further help, open an issue at [github.com/AbsenteeAtom/Adrilight3/issues](https://github.com/AbsenteeAtom/Adrilight3/issues).*
