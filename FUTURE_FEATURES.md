# adrilight — Future Features

This file documents planned features for future versions of adrilight. Each entry includes a description, origin, complexity estimate, and technical notes to inform implementation planning.

---

## Feature: Multi-Monitor Support

**Description:** Allow the user to select which monitor drives the LEDs rather than always defaulting to the first DXGI adapter and output.

**Origin:** Community request

**Complexity:** Medium

**Technical Notes:**
- `DesktopDuplicator` is currently hardcoded to `new DesktopDuplicator(0, 0)` — adapter index 0, output index 0.
- Both indices need to be exposed as `UserSettings` properties (e.g. `AdapterIndex`, `OutputIndex`) with appropriate defaults.
- A monitor selection UI is needed — ideally a dropdown on the General Setup tab listing available monitors by friendly name, populated by enumerating DXGI adapters and outputs at startup.
- `DesktopDuplicatorReader` constructs `DesktopDuplicator` directly; it will need to read the new settings and reconstruct the duplicator when the selection changes (similar to how `SerialStream` reopens the COM port when settings change).

---

## Feature: Sound to Light

**Description:** An alternative LED mode that uses WASAPI loopback audio capture via NAudio, performing FFT frequency analysis and mapping frequency bands to LED zones for an audio-reactive lighting effect.

**Origin:** Community request

**Complexity:** Medium

**Technical Notes:**
- Completely independent of the screen capture pipeline — no screen frames are needed in this mode.
- ~~Requires a **mode manager** to switch cleanly between screen capture mode and audio reactive mode; both pipelines cannot run simultaneously without contention over the `SpotSet`.~~ **Done (v3.5.0):** `IModeManager` / `ModeManager` implemented. `SetMode(LightingMode.SoundToLight)` already accepted by the TCP API. The next step is implementing `AudioCaptureReader` as an `ILightingMode` and wiring it into `ModeManager.SetMode()`.
- NAudio (WASAPI loopback) captures system audio output without requiring a microphone.
- FFT maps frequency bands to LED positions: low bass frequencies → bottom LEDs, mid-range → sides, high frequencies → top, or user-configurable zone mapping.
- **Future consideration** — make the warm-bottom/cool-top colour tinting toggleable, offering a White Only mode where all zones drive pure white at varying brightness. To be implemented if requested by the community.

---

## Feature: Gamer Mode

**Description:** An advanced event-driven LED mode that classifies audio events in real time and combines them with full-frame screen analysis to produce contextually appropriate LED responses. Designed to make lighting feel like a live commentary on what is happening in the game, not just a passive mirror of screen colours.

**Origin:** Original idea

**Complexity:** High

**Prerequisites:** `IModeManager` / `ModeManager` implemented in v3.5.0. Sound to Light (the simpler audio mode) should be built and validated first before tackling Gamer Mode's blending and temporal sequencing.

**Technical Notes:**

### Audio Event Classification

Each gaming event has a distinct audio signature defined by its frequency profile, attack time, decay time, and total energy. The classifier operates on a rolling short-time FFT window and triggers LED sequences when a signature matches.

| Event | Audio Signature | LED Response |
|---|---|---|
| **Gunshot** | Sharp high-frequency transient; short attack, short decay | Brief full white flash, fast decay |
| **Explosion** | Low-frequency boom; longer duration, high total energy | Sustained warm orange/red brightening, slow fade |
| **Car crash** | Mid-frequency impact + metallic high-frequency scrape | Sharp white flash followed by flickering orange |
| **Thunder** | Low-frequency rumble building slowly (originating from lightning not necessarily visible on screen), followed by sharp high-frequency crack | Speckled low-intensity grey pattern with random LEDs flickering, intensity increasing as rumble builds; crack triggers blinding white flash in detected screen region, then rapid decay |

### Blending Model

Screen capture and audio are complementary signals — they should collaborate, not compete.

- When lightning is visible on screen, screen capture leads naturally and audio enhances with the rumble build pattern.
- When the event is off-screen or the screen centre is dark (no visible border contribution from the event), audio leads and must compensate entirely — screen capture contributes nothing useful in this case.
- The system should assess both signal strengths in real time and blend accordingly. A simple approach: compute a "screen event strength" from the variance of border pixel colours over the last N frames, and an "audio event strength" from the current classifier confidence; blend the two LED outputs proportionally.

### Wider Pixel Capture for Event Detection

The current pipeline samples only the border pixels for LED colour. This makes it blind to centre-screen events (e.g. a lightning flash that does not reach the border).

Gamer Mode requires a separate **low-resolution full-frame sample** — approximately 32×18 pixels — for event detection only. This sample is not used for LED colour calculation.

The existing mipmap chain in `DesktopDuplicator` already generates downscaled frames (mipmap level 3 is used for the current border sampling). The 32×18 detection frame may already exist in the mipmap chain and simply need routing to a detection pipeline rather than requiring additional GPU work. This should be confirmed during implementation.

LED colour calculation continues to use border sampling unchanged.

### Temporal Sequencing

Gamer Mode must track audio building over time to **anticipate** events rather than simply react to them. A pure reactive system would catch the lightning crack but miss the slow thunder rumble that precedes it and gives it narrative context.

The temporal model should maintain a short rolling history of frequency band energy (e.g. 1–2 seconds) and recognise multi-stage patterns:
1. **Onset detection** — sustained low-frequency energy rising over multiple frames signals a building event.
2. **Peak detection** — a sharp transient following a build phase confirms the event type.
3. **Decay tracking** — the LED sequence continues and fades naturally over the event's expected decay duration, even if the audio transient is brief.

The thunder sequence (slow rumble → flickering grey pattern → crack → white flash → rapid decay) is the canonical example of a multi-stage temporal sequence. The classifier must hold state across frames to produce this correctly.
