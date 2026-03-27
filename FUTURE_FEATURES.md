# adrilight — Future Features

This file documents planned features for future versions of adrilight. Each entry includes a description, origin, complexity estimate, and technical notes to inform implementation planning.

---

## ~~Feature: Dual-Monitor Spanning Mode~~ — Done (v3.7.1)

**Description:** A continuous LED strip running around the combined perimeter of two side-by-side monitors, treated as a single wide ambient lighting surface. Both displays are captured simultaneously and stitched into one wide frame before spot sampling. The spot layout covers the full combined width as if the two monitors were one screen.

**Origin:** Natural extension of v3.7.0 monitor selection

**Complexity:** Medium

**Prerequisites:**
- v3.7.0 `AdapterIndex` / `OutputIndex` in `UserSettings` (done) — these become the primary monitor pair
- No changes needed to `SpotSet`, `SerialStream`, black bar detection, or the colour pipeline — they operate on whatever bitmap dimensions they receive

**Technical Notes:**

### Settings additions
Two new scalar settings alongside the existing v3.7.0 pair:
- `SpanningEnabled` (bool, default `false`) — gates all spanning logic; when off the pipeline is identical to v3.7.0
- `AdapterIndex2` / `OutputIndex2` (int, default 0) — the second display; exposed via a second monitor dropdown that appears only when `SpanningEnabled` is on

### Frame stitching
`DesktopDuplicatorReader` holds a second `DesktopDuplicator` instance (`_desktopDuplicator2`) when spanning is enabled. Each frame:
1. Capture left monitor → small bitmap at 1/8 scale (existing pipeline)
2. Capture right monitor → small bitmap at 1/8 scale (new)
3. Stitch side-by-side into a single wide bitmap: width = `w1 + w2`, height = `max(h1, h2)`. For equal-height monitors this is a simple row-by-row memcpy of two halves.
4. Pass stitched bitmap into the existing pipeline unchanged — `DetectBlackBars`, spot sampling, colour corrections all run on the combined frame without modification.

Temporal synchronisation is intentionally loose: the two captures fire sequentially on the same thread, so there may be a sub-frame gap between them. For ambient lighting this is imperceptible.

### Spot layout
`SpotsX` already represents the total horizontal LED count. No change needed. The stitched frame is wider, so spot rectangles proportionally map across both displays — the left half of the rectangle space falls on monitor 1's content, the right half on monitor 2's. The user configures `SpotsX` to match the total number of LEDs across the combined top/bottom runs.

### Aspect ratio awareness
`SpotSet.BuildSpots` derives spot dimensions from screen width/height. With a stitched frame the screen is wider relative to height, so horizontal spots become shallower and vertical spots stay the same. No code change is needed — it is purely a calibration concern: the user should set `SpotsX` proportionally to the combined physical width (e.g. if each monitor is 1920 wide and they have 20 top-run LEDs per monitor, set `SpotsX = 40`).

### Monitor height mismatch
If the two monitors have different vertical resolutions (e.g. 1080p + 1440p), the stitched bitmap uses `max(h1, h2)` height, with the shorter monitor's content left-aligned within its column. The empty region at the bottom of the shorter monitor's column will be sampled as black — black bar detection will naturally clamp those spots to the content edge, which is the correct behaviour.

### What does NOT change
- `SerialStream` — unchanged; receives colour data from `SpotSet` as always
- `SpotSet` — unchanged; operates on whatever `ExpectedScreenWidth/Height` it is given
- `DetectBlackBars` / `GetSamplingRectangle` — unchanged; work on any bitmap dimensions
- `AudioCaptureReader` / Sound to Light — unchanged; independent of the capture pipeline

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
