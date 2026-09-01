# Videotoy

**Videotoy** is a Windows 11 desktop application that turns a
[Shadertoy](https://www.shadertoy.com)-style GLSL, WGSL, or HLSL shader into
a video or animated image file, rendered frame by frame through a fully
**deterministic** pipeline — no dependency on the host machine's real-time
performance — and encoded with an **embedded** copy of `ffmpeg.exe`, so no
external tool or dependency needs to be installed separately.

![Videotoy screenshot](docs/scs/screenshot.png)

## Features

### Shader loading

- Open a shader via **File → Open Shader...** or by dragging a file onto the
  preview viewport
- Supports raw GLSL, WGSL, and HLSL source (`.glsl`, `.frag`, `.wgsl`,
  `.hlsl`, `.hlsli`) as well as full Shadertoy JSON exports (`.json`,
  `.shadertoy`), including multi-pass projects (`Image`, `Buffer A/B/C/D`,
  `Common`)
- Automatic source-language detection (by extension, then by a syntax
  heuristic for ambiguous files) with a status-bar indicator and a manual
  override if detection ever gets it wrong
- Line-numbered parsing/compilation errors and warnings surfaced in a
  dedicated **Shader Issues** panel, filterable by severity (error/warning)
  and by pass (Image/Buffer A-D)
- Standard Shadertoy uniforms: `iResolution`, `iTime`, `iTimeDelta`,
  `iFrame`, `iMouse`, `iDate`, `iChannel0-3`, `iSampleRate`
- Recently opened shaders remembered across sessions

### Inputs

- Static image textures as `iChannel` inputs
- Video files as `iChannel` inputs, decoded deterministically frame-by-frame
  by timestamp (never real-time playback), with a configurable time-mapping
  (linear, looped, or frozen on last frame) between render time and the
  video's own playback position
- Audio files (WAV / MP3 / OGG) as `iChannel` audio sources — the
  spectrum/waveform texture Shadertoy shaders expect is regenerated
  **deterministically** from `iTime` for every rendered frame, not sampled
  from real-time playback
- Custom uniforms declared via a lightweight comment convention in the
  shader source (`// uniform: float Speed = 1.0 [0.0, 5.0] "Speed"`),
  exposed as live sliders in the render settings panel for interactive
  preview tuning (the exported video always uses the shader's declared
  defaults)

### Rendering

- GLSL, WGSL, and HLSL sources are all transpiled or compiled down to the
  HLSL the offscreen Direct3D 11 renderer consumes, with automatic WARP
  (software) fallback when no compatible GPU is available
- Deterministic render clock: `iTime` is derived purely from the frame
  index and the target frame rate, so the exported video is identical
  regardless of the machine's speed
- Full multi-pass support with ping-pong render targets for
  self-referencing feedback buffers (Buffer A/B/C/D)
- Live, real-time preview in a fixed 800×450 viewport with play/pause/stop
  and a scrubbable timeline

### Video export

- Direct export via an embedded, integrity-checked `ffmpeg.exe` (SHA-256
  verified at every startup) — frames are streamed straight into FFmpeg's
  stdin pipe, with no intermediate frame files ever written to disk
- Video containers: MP4, WebM, and MOV/ProRes, with H.264, H.265, VP9, and
  ProRes codecs depending on container, plus optional hardware encoding
  (NVIDIA NVENC, Intel Quick Sync, AMD AMF) where available
- Animated image export: GIF (with palette generation and dithering
  options) and animated WebP (lossy or lossless), as an alternative to
  video export
- Configurable resolution (Preview / SD / HD / Full HD / 4K UHD, dedicated
  4:3, 16:9 and 9:16 (portrait) presets, or fully custom), frame rate
  (24/25/30/60 or custom), and rate control (CRF or target bitrate)
- **Manual duration** mode (fixed length in seconds or frames) or
  **seamless loop** mode: enter a loop period and Videotoy computes the
  exact frame count for a perfect, jump-free `last frame → first frame`
  loop, with an optional assisted rounding suggestion when the requested
  duration doesn't divide evenly into whole frames, and an optional
  heuristic detection of a shader's own native loop period
- Loop seam preview: side-by-side comparison of the loop's first and last
  rendered frame, to validate the seam before committing to a full export
- Live estimate of the total frame count and output file size as settings
  change
- Progress bar with current/total frame counter and remaining-time
  estimate, plus clean cancellation (FFmpeg process is killed and cleaned
  up immediately)
- "Low-spec mode": throttled, strictly sequential rendering with no
  dropped or reordered frame, for modest hardware
- Save and reload named export presets (resolution, frame rate, duration
  mode, codec/container, low-spec mode)
- A history log of past exports (settings used, result, encoding time),
  browsable from its own panel
- Automatic audio muxing when the shader declares an audio `iChannel`: the
  source audio track is encoded and muxed together with the video in a
  single FFmpeg pass, strictly aligned on the same deterministic timeline
  (same `t = 0` origin, same duration, including at a seamless loop's seam)

### Render queue (batch export)

- Queue up multiple exports — each pairing a shader with its own full
  export settings — and let them render one after another, a single FFmpeg
  pipeline at a time
- Drag-and-drop reordering, a thumbnail per queued item, and both per-item
  and overall progress
- A failed item (invalid shader, encoding error) never stops the rest of
  the queue; pause, resume, and cancel are available per item or for the
  whole queue
- The queue persists to disk, so pending items survive closing and
  reopening the app

### Undo/redo

- A shared history stack covers every render/export setting (resolution,
  frame rate, duration/loop, codec/container, encoding options) and
  custom-uniform slider values — never the shader file's own content
- Cascading changes (e.g. switching codec also resetting the video
  profile) always undo/redo together as a single step
- Dragging a slider or editing a numeric field is coalesced into one
  history entry per gesture, not one per tick or keystroke
- `Ctrl+Z` / `Ctrl+Y`, active while the render settings panel has focus,
  plus undo/redo buttons in the quick-action toolbar

### Interface

- Custom, chromeless window with the Windows 11 Mica backdrop, rounded
  corners, and a single light theme
- The render settings panel is organized into thematic, collapsible
  sections (Resolution/FPS, Duration/Loop, Codec/Container, Inputs, Render
  Queue), and can itself be retracted entirely
- A quick-action toolbar for the most frequent actions (open, export,
  undo/redo, panel toggles), keyboard accessibility (visible focus, a
  consistent tab order) across every window, and a first-launch guided
  onboarding overlay (replayable anytime from the Help menu)
- Toast notifications, an animated splash screen, and an animated
  Edit ↔ Export mode transition
- Full French / English localization with hot language switching (no
  restart required), selectable from the **About** window
- **About** window with the application name, current SemVer version,
  copyright, contact e-mail and website

## Installation

Download the latest installer from the
[Releases](https://github.com/patrickjaillet/Videotoy/releases) page and run
it. FFmpeg is bundled with the installer — no additional dependency needs to
be installed separately. Videotoy targets **Windows 11** only.

## Usage

1. Launch Videotoy and open a shader (**File → Open Shader...**, or drag a
   `.glsl` / `.frag` / `.wgsl` / `.hlsl` / `.json` / `.shadertoy` file onto
   the preview viewport).
2. Use the live preview (play/pause, scrub the timeline) to check the
   shader, and adjust any custom uniforms exposed by it.
3. Open the **Render Settings** panel — organized into collapsible sections
   — to configure resolution, frame rate, codec/container (or an animated
   image format), and the export duration: either a manual length or a
   seamless loop period.
4. Pick an output folder and file name, then start the export from the
   toolbar, the **Render** menu, or the panel's export button — optionally
   queuing it in the render queue instead, to batch it with other exports.
   Progress, remaining time and cancellation are available while the export
   renders.

## Building from source

See [COMPILATION.md](COMPILATION.md) for build prerequisites, the FFmpeg
embedding step, and how to generate the Inno Setup installer.

## Website

https://patrickjaillet.github.io/Videotoy

## License

MIT — see [LICENSE](LICENSE).

## Author

Patrick JAILLET — sandefjord.development@proton.me
