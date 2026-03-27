# adrilight — Future Features

This file documents planned features for future versions of adrilight. Each entry includes a description, origin, complexity estimate, and technical notes to inform implementation planning.

---

## Feature: Gamer Mode

**Description:** An advanced event-driven LED mode that classifies audio events in real time and combines them with full-frame screen analysis to produce contextually appropriate LED responses. Designed to make lighting feel like a live commentary on what is happening in the game, not just a passive mirror of screen colours.

**Origin:** Original idea

**Complexity:** High

**Prerequisites:** `IModeManager` / `ModeManager` implemented in v3.5.0. Sound to Light (the simpler audio mode) built and validated in v3.6.x. Multi-monitor support added in v3.7.0.

**Technical Notes:**

### Architecture consideration

Gamer Mode is an overlay on Screen Capture, not a replacement mode. Screen Capture runs continuously as the base layer; an audio event classifier runs in parallel and briefly overrides spot colours during detected events before handing back. This differs from the mutual-exclusivity assumption in the current `ModeManager.SetMode()` — the overlay concept must be designed as a prerequisite before any classifier work begins.

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
