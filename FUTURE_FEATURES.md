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

---

# Maintenance Recommendations

Deferred items from the 2026-07-06 csproj review and Claude Code usage analysis. None are urgent; each should be its own session with the stated verification.

## Package updates (dedicated session, hardware test required)

Current versions are pinned and working; updates change runtime behaviour, so bundle them into one session ending with a full manual test on the physical LED setup (screen capture, Sound to Light, serial output, tray, Diagnostics).

| Package | Current | Notes |
|---|---|---|
| MaterialDesignThemes | 4.9.0 | **5.x is a breaking upgrade** (theme resource renames) — the riskiest item; do last, expect XAML churn similar to the 2.x→4.x migration |
| MaterialDesignColors | 2.1.4 | Move in lockstep with MaterialDesignThemes |
| NLog | 5.3.4 | 6.x available; programmatic config API mostly stable, verify file + Diagnostics targets still write |
| CommunityToolkit.Mvvm | 8.3.2 | 8.4+ available; low risk |
| Polly | 8.4.1 | Low risk; only used for the capture retry policy |
| Microsoft.Xaml.Behaviors.Wpf | 1.1.77 | Low risk |
| System.Reactive / MoreLinq | 6.0.1 / 3.4.0 | Only used by `FpsLogger`; alternatively rewrite `FpsLogger` with a plain timer and drop both packages |

## SharpDX contingency

SharpDX (4.2.0) has been unmaintained since 2019. It works today and there is no reason to migrate pre-emptively. **If** DXGI breakage ever appears on a future Windows version, the successor to evaluate is **Vortice.Windows** (actively maintained SharpDX descendant, near-identical API surface: `IDXGIOutputDuplication.AcquireNextFrame` exists). Migration touches only `DesktopDuplicator.cs` and `MonitorEnumerator.cs`.

## Claude Code setup (from UsageAnalysis.md, 2026-07-06)

- **Scope the graphify hook** — the global PreToolUse hook in `~/.claude/settings.json` launches Python on every Read/Glob in every project, but the script is hardcoded to the Remote Checkout pi graph, so in adrilight it is a guaranteed no-op process launch. Move the hook into `Remote Checkout/.claude/settings.json` (or add an immediate-exit CWD guard), and verify it still fires in the pi project afterwards.
- **`/crash` triage skill** — sessions here often open with a hand-pasted crash dialog. A small skill could instead read the newest `publish/adrilight-*/logs/` files, extract recent ERROR/exception entries, correlate stack frames with source, and propose a root cause. Build it if crash-triage friction recurs now that `/ship` exists.
