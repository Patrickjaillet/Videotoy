# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Fixed

- `Directory.Build.props` / `Videotoy.Core.Version` were still stamped
  `0.3.0`, three completed roadmap phases (v0.5.0–v0.8.0) behind the
  actually shipped feature set (the last fully-`[x]` phase in
  `ROADMAP.md`), silently violating this project's own "automatic SemVer
  serialization" convention: the installer's `AppVersion` (read from
  `Videotoy.exe`'s version resource) and the in-app "À propos" version
  were both wrong. Bumped `VersionPrefix`/`FileVersion`/`AssemblyVersion`/
  `InformationalVersion` in `Directory.Build.props` and `Major`/`Minor` in
  `Videotoy.Core.Version` to `0.8.0`

### Added

- SemVer validation and display in the installer — "Signature/vérification
  de version affichée dans l'installateur (SemVer)" roadmap item:
  - `installer/Videotoy.iss`: version is now read via the preprocessor's
    `GetVersionComponents` (integer Major/Minor/Revision/Build fields)
    instead of the previous `GetVersionNumbersString` + string-split, so
    an unreadable version resource is caught explicitly (`""` return)
    rather than silently producing a malformed `AppVersion`. Added a
    build-time guard (`#error`) rejecting an unstamped `0.0.0.x` resource,
    which would otherwise let a build with a forgotten
    `Directory.Build.props` bump ship under a fake version. `MyAppVersion`
    is rebuilt as a strict three-part `X.Y.Z` string (the resource's
    fourth, Windows-only `Build` field is dropped, matching this project's
    three-part SemVer, not Windows' four-part file version)
  - `[Code]` `InitializeWizard`: appends the verified `Version : X.Y.Z`
    line to the Welcome page body (`WelcomeLabel2`), so the SemVer is
    shown as plain, readable text before any file is installed — not only
    inside the wizard's window title / "Ready to Install" summary, which
    `AppVerName={#MyAppName} {#MyAppVersion}` already drove beforehand and
    still does

- `tools/ffmpeg/generate-hash.ps1`: this script was referenced by
  `.gitignore` (`!/tools/ffmpeg/generate-hash.ps1`), `COMPILATION.md`, and
  earlier `CHANGELOG.md` entries as already existing, but was missing from
  the repository — regenerates `tools/ffmpeg/ffmpeg.exe.sha256` (the hash
  `FfmpegIntegrityVerifier` checks at every startup) from the embedded
  `ffmpeg.exe`, in the `sha256sum`-compatible format the verifier already
  parses
- `tools/ffmpeg/README.md`: local setup notes for embedding `ffmpeg.exe` —
  where to obtain a Windows build, where to place it, and how
  `Videotoy.App.csproj` (copy to build output), `installer/Videotoy.iss`
  (embed in the installer, with a build-time guard if missing), and
  `FfmpegIntegrityVerifier` (startup SHA-256 check) each depend on it;
  tracked in Git via a `.gitignore` exception alongside the other
  `tools/ffmpeg` scaffolding files, closing out the v1.0.0 "Embarquement de
  `ffmpeg.exe` dans le package d'installation" roadmap item — the packaging
  path itself (`.csproj` conditional copy + `.iss` `[Files]` recursive
  bundling + `#error` guard) was already in place since v0.5.0/v0.9.0, the
  only missing piece was this documented local setup step and the
  `generate-hash.ps1` script it depends on

### Fixed

- `Videotoy.App` failed to build (`CS9135`, `CS1061` — F#/C# discriminated
  union interop): two spots in `MainWindowViewModel.cs` treated F# union
  cases the way a C# `enum` would, which doesn't compile — each union case
  (with or without data) is a distinct nested type in the C#-visible API,
  not a constant:
  - `DescribeFirstValidationIssue`'s `switch` on
    `Videotoy.Core.ExportSettingsValidator.ExportSettingsIssue` used its
    no-data cases (`InvalidResolution`, `InvalidFrameRate`, ...) as bare
    constant patterns (`ExportSettingsIssue.InvalidResolution =>`, `CS9135:
    a constant value is expected`); switched to type patterns with a
    discard (`ExportSettingsIssue.InvalidResolution _ =>`), the correct
    C# idiom for matching a data-less F# union case. The data-carrying
    cases (`OutOfRangeConstantRateFactor of value: int`,
    `OutOfRangeTargetBitrate of value: int`) also read the captured
    instance's field as `.Item` (`CS1061`, no such member); an F# union
    case with a *named* field exposes it under that field's own
    (capitalized) name, not `.Item` — fixed to `.Value`, matching the
    `value:` field name declared in `ExportSettingsValidator.fs`
  - `ApplyLoopPeriodDetection` read `detection.SuggestedCandidate is { }
    candidate` and then accessed `candidate.PeriodSeconds` /
    `.SourceExpression` / `.PassName` directly (`CS1061`, no such members):
    `SuggestedCandidate` is a `LoopPeriodCandidate option`, i.e. a C#-side
    `FSharpOption<LoopPeriodCandidate>` — `is { }` only proved the
    *option* itself non-null, not that it held a value, so `candidate` was
    bound to the option wrapper rather than the `LoopPeriodCandidate`
    inside it. Fixed to the nested property pattern
    `is { Value: { } candidate }`, which both checks for `Some` and
    unwraps `.Value` into `candidate` in one step — the same manual
    `is null` + `.Value` idiom already used correctly elsewhere in this
    file for `Videotoy.Core.ShaderModel.firstAudioChannelPath`'s
    `string option`
- `Videotoy.App` failed to build (`MC1000`/`MC3024`, "the property
  'Button.Style' has already been set and can only be set once"):
  `MainWindow.xaml`'s `TogglePanelButton` set `Style` twice — once as a
  plain attribute (`Style="{StaticResource IconButtonStyle}"`) and again
  as a `<Button.Style>` element (added by the v0.8.0 tooltip pass, an
  inline style with a `DataTrigger` switching the tooltip text and
  already `BasedOn="{StaticResource IconButtonStyle}"`) — the attribute
  form was never removed when the element form was introduced. Removed
  the now-redundant `Style=` attribute; the button keeps its icon-button
  appearance unchanged through the remaining `<Button.Style>`'s
  `BasedOn`
- `Videotoy.Ffmpeg` failed to build (`CS0101`, `CS0111`, `CS8863`, `CS0708`):
  two pairs of types were each declared twice across different files,
  left over from an earlier refactor that was never fully cleaned up:
  - `FfmpegIntegrityException`: a second, narrower definition (single
    constructor) had been accidentally left at the top of
    `FfmpegIntegrityVerifier.cs`, duplicating the real one in
    `FfmpegIntegrityException.cs` (two constructors, including the
    `innerException` overload). Removed the duplicate from
    `FfmpegIntegrityVerifier.cs`
  - `FfmpegErrorCategory` / `FfmpegStderrDiagnosis` / `FfmpegStderrParser`:
    `FfmpegStderrDiagnosis.cs` held an entire unused, incompatible second
    implementation (an instance-based parser with `AppendLine`/`Diagnose()`
    and a list-valued `RawTailLines`), conflicting with the actual
    implementation in `FfmpegStderrParser.cs` (a static parser taking the
    stderr tail directly, `Diagnose(IReadOnlyList<string>)`, string-valued
    `RawTail`) that `FfmpegService.FinishAsync` and
    `EffectiveMaxTailLines` actually call. Deleted the unused
    `FfmpegStderrDiagnosis.cs` file entirely; `FfmpegStderrParser.cs`
    remains the single source of truth for FFmpeg stderr diagnosis

### Added

- Inno Setup 7 installer script finalized with custom icons and shortcuts —
  first item of the v1.0.0 "Installateur & release" roadmap:
  - `installer/Videotoy.iss`: `AppVersion` is now read directly from the
    compiled `Videotoy.exe`'s version resource via the ISPP
    `GetVersionNumbersString` function, instead of a hardcoded string that
    could silently drift from the actual build (`OutputBaseFilename` and
    the wizard's displayed `AppVerName` follow automatically)
  - `[Icons]`: added a Start Menu uninstall shortcut
    (`{group}\Uninstall Videotoy`, `IconFilename={uninstallexe}` so it
    automatically reuses `installer.ico` — the icon Inno Setup already
    applies to the generated uninstaller — reading as distinct from the
    app shortcut) alongside the existing Start Menu and Desktop
    application shortcuts (both already using the installed
    `Videotoy.exe`'s embedded `app.ico`); the Desktop shortcut is now
    gated behind a new, unchecked-by-default `desktopicon` `[Tasks]` entry
    (standard Inno Setup "Additional icons" wizard page) instead of always
    being created
  - Compile-time guards (`#error`) added for a missing `app.ico` or
    `installer.ico`, alongside the pre-existing checks for the Release
    build output and the embedded `ffmpeg.exe`, so a broken icon reference
    fails the installer build immediately instead of silently shipping
    without one
  - `[Setup]`: added `AppVerName`, `AppSupportURL`, `AppUpdatesURL`,
    `UninstallDisplayName`, and `MinVersion=10.0.22000` (Windows 11 only,
    per this project's strict Windows-11-only support policy)

- Software icon and dedicated installer icon — fourth and final item of the
  v0.8.0 "Polish UI/FX" roadmap:
  - `Assets/Icons/app.ico` (replaced): regenerated as a proper
    multi-resolution Windows icon (16/20/24/32/40/48/64/128/256px, PNG-
    compressed frames, transparent background) matching the app's actual
    logo mark — the `IconLoop` glyph already used in the title bar — on a
    rounded-square accent-blue tile, instead of the previous unrelated
    "play button" glyph on an opaque black square. Small sizes (≤48px) use
    a thicker stroke weight than the 64-256px frames so the loop shape
    stays legible at taskbar/Explorer scale. Referenced unchanged by
    `Videotoy.App.csproj`'s existing `ApplicationIcon`
  - `installer/installer.ico` (new): same multi-resolution set, reusing
    the app's logo mark with a small green "download into tray" badge in
    the bottom-right corner so the installer's icon reads as distinct from
    the running application's in the taskbar/Explorer while still being
    immediately recognizable as Videotoy's installer
  - `installer/Videotoy.iss`: `[Setup]` section now sets
    `SetupIconFile=installer.ico` (the Inno Setup 7 wizard/executable
    icon) and `UninstallDisplayIcon={app}\{#MyAppExeName}` (Add/Remove
    Programs entry icon); `[Icons]` entries (Start Menu + Desktop
    shortcuts) now specify `IconFilename` explicitly for clarity, pointing
    at the installed `Videotoy.exe`

- Custom scrollbar, stylized tooltips, and contextual cursors — third item
  of the v0.8.0 "Polish UI/FX" roadmap:
  - `Theme.xaml`: new `CustomScrollBarStyle` (+ `CustomScrollBarThumbStyle`,
    `CustomScrollBarPageButtonStyle`) — a slim (10px), borderless, rounded
    thumb replacing the native Windows scrollbar chrome, tinted with
    `BorderBrush` at rest and `AccentBrush`/`AccentPressedBrush` on
    hover/drag. Applied globally via an implicit `Style TargetType="ScrollBar"`
    so every `ScrollViewer` in the app (issues panel, render settings
    panel, combo box popups) picks it up automatically, no per-usage
    change required
  - `Theme.xaml`: new `StyledToolTipStyle` — a small elevated dark card
    (rounded corners, `PopupShadow`, fade + slide-down entrance animation)
    replacing the flat system tooltip, applied globally via an implicit
    `Style TargetType="ToolTip"`
  - `MainWindow.xaml`: added tooltips to previously unlabeled icon-only
    buttons (title bar minimize/maximize/close, issues panel close button,
    render settings panel collapse/expand toggle — the latter switching
    text via a `DataTrigger` on `IsSettingsPanelOpen`) and to the preview
    viewport's drop zone; new `window.minimize`, `window.maximize`,
    `window.close`, `panel.toggle.collapse`, `panel.toggle.expand`
    resource keys in `en.json` / `fr.json` (also wired the previously
    unused `issues.panel.close` key as that button's tooltip)
  - `FieldTextBoxStyle` (`Theme.xaml`): `Cursor="IBeam"` while enabled,
    reverting to the default arrow when disabled (e.g. output file name
    during export) so the cursor never implies text entry is possible
  - Preview viewport drop zone: `Cursor="Hand"` while no shader is loaded,
    signaling the area accepts a dropped/clicked file

- Animated progress ring and continuous pulse shown while a video export is
  rendering — first item of the v0.8.0 "Polish UI/FX" roadmap:
  - `Converters/ProgressRingArcConverter.cs` (new): converts
    `ExportProgressPercent` (0-100) into a `PathGeometry` circular arc,
    sweeping clockwise from the top and reaching a full circle at 100%
  - `MainWindow.xaml`: the export-mode viewport overlay now shows a
    36px progress ring (dim static track + animated indicator arc bound to
    `ExportProgressPercent` via `ProgressRingArcConverter`) with the
    `IconExport` glyph centered inside it, replacing the previously static
    icon-only overlay
  - `Theme.xaml`: new `RenderPulseStoryboard` — a slow, continuous
    breathing opacity/scale animation (`SineEase`, 1.1s, auto-reversing)
    applied to a halo ring behind the progress ring, so the overlay reads
    as "actively working" even during long gaps between progress callbacks
    on a low-spec render; `ExportProgressBarStyle` (side panel progress
    bar) gained the same breathing pulse on its filled indicator for
    consistency between the two progress displays
- Non-intrusive toast notifications for export outcomes — second item of
  the v0.8.0 "Polish UI/FX" roadmap:
  - `ViewModels/ToastNotificationViewModel.cs` (new): immutable
    `ToastSeverity` (Success/Error) + toast record (Id, Severity, Title,
    Message)
  - `MainWindowViewModel`: new `Toasts` observable collection,
    `ShowToast(severity, title, message)` (adds a toast and schedules its
    removal after 5 seconds via `DispatcherTimer`), `DismissToastCommand`
    for manual early dismissal. Wired into `ExportVideoAsync`: a success
    toast on completion, an error toast on pre-export settings-validation
    failure, `FfmpegEncodingException`, and any other exception.
    Cancellation remains status-bar-only (no toast), consistent with it
    being a user-initiated, non-error outcome
  - `Converters/ToastSeverityBrushConverter.cs`,
    `Converters/ToastSeverityIconConverter.cs` (new): map `ToastSeverity`
    to a color (green/red) and a small check/cross glyph respectively
  - `MainWindow.xaml`: new bottom-right floating `ItemsControl` stack
    bound to `Toasts`, each rendered via the new `ToastCardStyle`
    (`Theme.xaml`: slide-in-from-right + fade entrance animation,
    `PopupShadow`, rounded card) so notifications never block interaction
    with the rest of the window
  - `en.json` / `fr.json`: new `toast.export.success.title`,
    `toast.export.error.title`, `toast.dismiss` resource keys

- Animated transition between the "Edit" mode (interactive shader preview,
  all render settings editable) and the "Export" mode (settings locked,
  deterministic frame-by-frame encoding in progress) — second item of the
  v0.8.0 "Polish UI/FX" roadmap:
  - `MainWindow.xaml`: new `ExportModeOverlayStyle`, applied to a scrim
    (`ExportModeOverlay`) layered over the preview viewport. Driven purely
    by a `DataTrigger` on the existing `IsExporting` property (no new
    ViewModel state): fades in a dark overlay with the export icon and
    live progress percentage, easing outward from a slight zoom so
    entering/leaving export mode reads as a deliberate transition rather
    than an instant visibility flip. Reuses the existing `IconExport`
    glyph (until now defined but unused) and the `FastDuration` /
    `MediumDuration` timings already used elsewhere in the window
  - Fixed several strings in the export progress/error card and its
    "Export to MP4" button that had been left hard-coded in English when
    the rest of `MainWindow.xaml` was wired to `{loc:Loc}` /
    `{loc:LocFormat}` in the previous i18n pass — all now resolve through
    resource keys that already existed in `fr.json` / `en.json` but were
    unused (`export.progress.title`, `export.progress.percent`,
    `export.progress.eta`, `export.error.title`, `action.cancelExport`,
    `action.exportToMp4`, `statusBar.frameCount.label`,
    `statusBar.frameCount.separator`)
  - `MainWindowViewModel.FormatRemainingTime`: now takes the newly
    injected `LocalizationService` and resolves its previously hard-coded
    `"Estimating..."` fallback through the (likewise previously unused)
    `export.progress.estimating` resource key, so the export ETA label is
    fully localized in every state

- Animated splash screen shown immediately on application startup, while
  the FFmpeg integrity check and service initialization run in the
  background — first item of the v0.8.0 "Polish UI/FX" roadmap:
  - `Videotoy.App.Views.SplashWindow`: chromeless, transparent window
    reusing the existing card/shadow style (`CardBorderStyle`,
    `PopupShadow`) and the application's `IconLoop` badge, with three
    looping animations (continuous logo rotation, opacity pulse on the
    badge, a sweeping indeterminate progress indicator) plus a fade-in on
    load; its title and loading text are localized
    (`{loc:Loc Key=app.title}` / `{loc:Loc Key=splash.loading}`) and
    switch immediately if the language changes
  - `App.OnStartup`: now shows the splash first, then resolves
    `LocalizationRuntime`/`FfmpegIntegrityVerifier`/`MainWindowViewModel`
    from the DI container and runs the FFmpeg integrity check behind it;
    enforces a 900 ms minimum splash duration (`MinimumSplashDuration`) so
    the animation stays visible even when startup itself is near-instant,
    then shows `MainWindow` and closes the splash. The integrity-check
    failure dialog still shows exactly as before, with the splash closed
    first
  - New symmetrical resource key `splash.loading` (fr: "Chargement...",
    en: "Loading...") added to `fr.json` / `en.json`

- Runtime wiring of the i18n resources introduced previously (hot language
  switching, system language detection on first launch, language selector
  in the About window), completing the remaining v0.7.0 roadmap items:
  - `Videotoy.Media.LocalizationService`: loads `fr.json` / `en.json` from
    `Resources/Localization` next to the executable, exposes
    `CurrentLanguage` and `GetString(key)` / `GetFormattedString(key, args)`,
    and raises `LanguageChanged` from `SetLanguage(...)` so bound UI
    refreshes immediately, with no application restart
  - `Videotoy.Media.AppLanguage`: the set of supported UI languages
    (`English`, `French`), with `ToCode()` / `FromCode(...)` conversion to
    the two-letter codes used as JSON resource file names
  - `Videotoy.Media.LanguageSettingsEntry` +
    `LocalizationService`'s private storage: persists the active language
    in `%AppData%\Videotoy\language-settings.json`, mirroring the existing
    `RecentFilesService` / `LoopSettingsService` storage pattern; the
    persisted entry distinguishes an explicit user selection from the
    auto-detected default, so a system-language change on a machine where
    the user never opened the language selector still updates on the next
    launch
  - System language detection: on first launch (no persisted selection
    yet), the initial language is derived from
    `CultureInfo.InstalledUICulture`, falling back to English when the
    system UI language isn't one of the languages Videotoy ships
    translations for
  - `Videotoy.App.Localization.LocExtension` (`{loc:Loc Key=...}`) and
    `LocFormatExtension` (`{loc:LocFormat Key=..., Path=...}`): XAML markup
    extensions resolving a localization key — plain or as a composite
    format string applied to a bound value (e.g. `CurrentFrame`) — through
    bindings that re-evaluate automatically on `LanguageChanged`, replacing
    every hard-coded string previously in `MainWindow.xaml`
  - `Videotoy.App.Localization.LocalizationRuntime` /
    `LocalizedStrings`: bridges the DI-registered `LocalizationService`
    singleton (attached once from `App.OnStartup`, before any window is
    constructed) to the XAML markup extensions, which are instantiated by
    the XAML parser outside of dependency injection
  - Language selector added to the About window: a combo box
    (`AboutViewModel.AvailableLanguages` / `SelectedLanguage`) listing each
    supported language by its own native name ("Français" / "English"),
    switching the active language immediately on selection
  - Two new symmetrical resource keys, `about.language.label` (fr: "Langue",
    en: "Language"), added to `fr.json` / `en.json`

- `fr.json` / `en.json` localization resource files
  (`Videotoy.App/Resources/Localization/`) now cover every user-facing
  string in the interface: menus, panel labels and descriptions, status
  bar, export progress/errors, diagnostics messages, the About window
  (including its window title), and the FFmpeg startup integrity-check
  dialog. Both files are kept symmetrical (138 keys in each, after the
  language-selector additions above).
- Persisted "Duration Mode" per shader: the last-used duration mode (manual
  vs. seamless loop) and the last loop duration are now remembered
  per-shader-file and restored automatically the next time that exact
  shader is opened, rather than always falling back to the application
  defaults:
  - `Videotoy.Media.LoopSettingsEntry`: serializable per-shader snapshot
    (shader file path, `IsSeamlessLoopModeEnabled`, `LoopDurationSeconds`,
    last-updated timestamp)
  - `Videotoy.Media.LoopSettingsService`: JSON persistence in
    `%AppData%\Videotoy\loop-settings.json`, keyed on the shader's absolute
    file path (case-insensitive match), mirroring the existing
    `RecentFilesService` / `ExportPresetService` storage pattern; entries
    beyond 200 are pruned, oldest first
  - `MainWindowViewModel.RestoreLoopSettings`: looked up on every
    `LoadShaderFile` call and applied to `IsSeamlessLoopModeEnabled` /
    `LoopDurationSeconds` when a saved entry exists for that file; a no-op
    the first time a given shader is opened
  - `MainWindowViewModel.PersistLoopSettings`: saved on every change to
    either `LoopDurationSeconds` or `IsSeamlessLoopModeEnabled` for the
    currently loaded shader, so the last state is captured even if the user
    never actually exports

- Assisted detection of a shader's native loop period (optional, heuristic):
  the render settings panel can now suggest a loop duration derived
  directly from the shader's own source code, for shaders that animate
  through simple periodic expressions on `iTime`:
  - `Videotoy.Core.LoopPeriodDetector`: recognizes `sin(iTime * K)`,
    `cos(K * iTime)`, `mod(iTime, K)` and `fmod(iTime, K)` (free spacing,
    free operand order for `sin`/`cos`) in the shader's `Common` code and
    every pass; converts each match to an implied period (`2*pi / K` for
    `sin`/`cos`, `K` directly for `mod`/`fmod`); deliberately restricted to
    these simple, unambiguous forms — anything more complex (`iTime`
    combined with other variables, nested functions, custom uniforms,
    etc.) is silently left undetected rather than risking a false
    suggestion. When several independent periodic patterns are found, the
    longest detected period is proposed as the default suggestion, since it
    most often matches the overall visual loop perceived by the user
  - `MainWindowViewModel.ApplyLoopPeriodDetection`: run once per shader
    load (`LoadShaderFile`), populating `HasDetectedLoopPeriod` /
    `DetectedLoopPeriodSeconds` / `HasMultipleDetectedLoopPeriods` /
    `DetectedLoopPeriodSourceText` — purely informational, never itself
    touching `LoopDurationSeconds` or the selected duration mode
  - `ApplyDetectedLoopPeriodCommand`: the only way the detected period ever
    reaches `LoopDurationSeconds`, mirroring the existing
    `ApplyAssistedLoopRoundingCommand` pattern — an explicit, user-triggered
    action, never applied automatically
  - Render settings panel: new "Detected native loop period" hint under the
    seamless-loop fields, showing the source expression/pass it was found
    in, a note when multiple candidates were found, and an `Apply` button

- Three aspect-ratio resolution presets in the "Resolution" combo box,
  alongside the existing Preview/SD/HD/Full HD/4K UHD/Custom entries:
  `Screen4By3` (1440 x 1080), `Screen16By9` (1920 x 1080), and
  `Smartphone9By16` (1080 x 1920, portrait) — plain additions to
  `ResolutionPresetOption.All`, so no other code change was needed: the
  combo box is already bound to `ResolutionPresets` (`=> All`), and preset
  persistence already round-trips any `Key` through `FromKey`

- Loop seam preview: a "Loop Seam Preview" card in the render settings
  panel, "Seamless loop" duration mode only, renders the loop's first frame
  (t = 0) and its actual last exported frame side by side for visual
  validation before committing to a full export:
  - `MainWindowViewModel.GenerateLoopSeamPreviewCommand`: walks the shared
    live-preview `MultiPassRenderer` through the *entire* loop timeline
    (`Videotoy.Core.LoopCalculator.computeFrameCount` /
    `buildFrameTimeline`, same construction as the real export), capturing
    frame 0 and the final frame — deliberately not just those two frames in
    isolation, since a shader with a self-referencing buffer (Buffer
    A/B/C/D ping-pong feedback) accumulates state frame by frame and a
    direct jump to the last timestamp would show a buffer state the actual
    export never produces. Runs synchronously on the UI thread (the shared
    D3D11 device isn't thread-safe), re-rendering the current live-playback
    frame afterward so the viewport isn't left showing the loop's last
    frame once the comparison is generated
  - `LoopSeamStartFrameImageSource` / `LoopSeamEndFrameImageSource`:
    dedicated `WriteableBitmap`s (preview resolution, BGRA32) holding the
    two captured frames; `HasLoopSeamPreview` drives the comparison's
    visibility and is reset to false whenever a setting that would
    invalidate it changes (shader reload, resolution/frame rate/duration
    mode/loop duration/exclusive-end-frame); `IsGeneratingLoopSeamPreview`
    disables the button and swaps its label to "Generating..." while the
    walk is in progress
  - `LoopSeamPreviewButtonTextConverter`: swaps the generate button's label
    based on `IsGeneratingLoopSeamPreview`

- "Exclusive end frame" toggle, seamless loop mode: the previously implicit
  "no duplicated frame at the loop seam" behavior is now an explicit,
  user-facing option (`IsLoopEndFrameExclusive`, on by default) rather than
  an unconditional rule:
  - `Videotoy.Core.Domain.DurationMode.SeamlessLoop` now carries an
    `excludeEndFrame: bool` alongside `loopSeconds`
  - `LoopCalculator.computeFrameCount`: in `SeamlessLoop` mode, `FrameCount`
    is `round(loopSeconds * fps)` when `excludeEndFrame` is true (default —
    the frame at `t = loopSeconds`, identical to `t = 0`, is never
    rendered), or that same count `+ 1` when false, deliberately including
    that duplicated end frame
  - `MainWindowViewModel.IsLoopEndFrameExclusive`: new panel toggle, defaults
    to `true`; feeds `ResolveDurationMode`'s
    `DurationMode.NewSeamlessLoop(loopSeconds, excludeEndFrame)` call and
    triggers `RecalculateExportPreview` (frame count / file size estimate)
    like every other duration-affecting input
  - `ExportPreset.IsLoopEndFrameExclusive`: persisted with the rest of the
    duration-mode settings; not `required` (defaults to `true`) so presets
    saved by an earlier version still deserialize correctly
  - Render settings panel: new "Exclusive end frame" checkbox under the
    "Seamless loop" duration fields, with an explanatory note

- Seamless loop assisted rounding: when the requested loop duration doesn't
  divide evenly into whole frames at the selected export frame rate
  (`HasLoopRoundingMismatch`), the render settings panel now also shows the
  nearest loop duration that does, alongside an `Apply` button to adopt it
  in one click:
  - `Videotoy.Core.LoopCalculator.suggestAssistedLoopSeconds`: pure
    `loopSeconds -> frameRate -> float` helper returning
    `round(loopSeconds * frameRate) / frameRate` (never less than one
    frame), i.e. the closest loop duration whose exact frame count is a
    whole number
  - `MainWindowViewModel.SuggestedLoopDurationSeconds`: recomputed inside
    `RecalculateExportPreview` whenever `HasLoopRoundingMismatch` is true
    (equal to `LoopDurationSeconds` otherwise); `ApplyAssistedLoopRoundingCommand`
    copies it onto `LoopDurationSeconds`, which eliminates the mismatch
    entirely rather than just reducing it. Never applied automatically —
    purely an explicit, user-triggered suggestion
  - Render settings panel: the existing rounding-mismatch warning now also
    shows "Suggested: X s (exact frame count)" with an `Apply` button,
    disabled while an export is running

- Roadmap check-off: "Manual duration" mode (fixed duration entered directly
  in seconds or frames) confirmed complete — already fully implemented as
  part of the v0.6.0 render settings panel work (`ManualDurationValue` /
  `ManualDurationUnit`, `ResolveDurationMode`, `ExportSettingsValidator`'s
  `InvalidDuration` check, preset persistence, and the "Manual duration"
  radio button + seconds/frames combo in the panel); no code change
  required for this entry, only the `ROADMAP.md` check-off

- Live custom uniforms preview with dynamically generated sliders: the
  render settings panel now exposes a "Custom Uniforms" card whenever the
  loaded shader declares at least one custom uniform, letting the user
  adjust values in real time against the live preview only (never the
  exported video, which always renders from the shader's declared
  defaults):
  - `Videotoy.Core.CustomUniformParser`: recognizes a dedicated GLSL
    comment convention — `// uniform: float MySpeed = 1.0 [0.0, 5.0]
    "Speed"` or `// uniform: vec3 TintColor = (1.0, 0.5, 0.2) [0.0, 1.0]`
    — in the `Common` code or any pass, supporting `float`/`vec2`/`vec3`/
    `vec4`, an optional `[min, max]` range applied per component (defaults
    to `[-10.0, 10.0]` when omitted), and an optional quoted display label
    (falls back to the uniform's name); malformed declarations are simply
    ignored rather than raised as shader errors, since the convention lives
    entirely inside an ordinary GLSL comment and never affects Shadertoy
    compatibility of the source file itself
  - `Videotoy.Core.GlslToHlslTranspiler`: emits a second HLSL constant
    buffer (`cbuffer CustomUniforms : register(b1)`) declaring every custom
    uniform detected in a given pass (Common + that pass's own source),
    alongside the existing `ShadertoyUniforms` (`register(b0)`); skipped
    entirely when the shader exposes none. `TranspileResult.CustomUniforms`
    exposes the parsed declarations for that pass; the new
    `projectCustomUniformsOf` / `projectCustomUniforms` helpers return the
    name-deduplicated union across every already-transpiled pass of a
    project, in stable first-seen order
  - `MultiPassRenderer`: allocates a dynamic `register(b1)` constant buffer
    sized from the shader's custom uniform count (a fixed 16-byte slot per
    uniform, regardless of its component count, to sidestep any per-type
    HLSL packing arithmetic), re-created on every `Initialize` call so a
    previous shader's uniforms never leak into the next one.
    `CustomUniformDeclarations` exposes the current shader's declarations;
    `SetCustomUniformComponent(name, componentIndex, value)` updates a
    single component's live value with no shader recompilation, uploaded
    to the GPU on every `RenderFrame` call (`UpdateCustomUniforms`) so the
    very next preview frame reflects the change immediately
  - `CustomUniformSliderViewModel` / `CustomUniformGroupViewModel`
    (`Videotoy.App.ViewModels`): one slider view model per scalar
    component (x/y/z/w), grouped by uniform; `MainWindowViewModel` exposes
    them as `CustomUniformGroups` (rebuilt in full — via
    `ReloadCustomUniformGroups` — every time a shader is loaded from
    `MultiPassRenderer.CustomUniformDeclarations`) and `HasCustomUniforms`,
    which drives the card's visibility. Each slider's `Value` change is
    wired straight back to `MultiPassRenderer.SetCustomUniformComponent`
  - `Theme.xaml`: new `FieldSliderStyle` (flat rounded track, accent round
    thumb) matching the existing `FieldTextBoxStyle`/`FieldComboBoxStyle`
    visual language, used by every dynamically generated uniform slider
  - Render settings panel: "Custom Uniforms" card between "Frame Rate" and
    "Duration Mode", listing one labelled group per custom uniform with one
    slider (+ live numeric readout) per component; hidden entirely when
    `HasCustomUniforms` is false

- Export presets: "Export Presets" card in the render settings panel to save
  and reload named snapshots of the export configuration
  (`Videotoy.Media.ExportPreset` / `ExportPresetService`, persisted to
  `%AppData%\Videotoy\export-presets.json`, mirroring the storage pattern
  already used by `RecentFilesService`):
  - `MainWindowViewModel.SaveExportPresetCommand`: saves resolution preset
    (+ custom width/height), frame rate preset (+ custom value), duration
    mode (manual/seamless loop) with its value/unit, and low-spec mode under
    the name typed into `NewExportPresetName`; replaces any existing preset
    with the same name (case-insensitive) rather than accumulating
    duplicates (`ExportPresetService.SaveOrReplace`). Deliberately excludes
    the output folder and file name — those stay per-export, not part of a
    reusable preset
  - `LoadExportPresetCommand`: applies `SelectedExportPreset` back onto every
    panel input it covers, through the normal properties (not the backing
    fields) so `RecalculateExportPreview` stays in sync automatically;
    `DeleteExportPresetCommand` removes a preset. Both disabled until a
    preset is selected (`SelectedExportPreset is not null`)
  - `ResolutionPresetOption.Key` / `FrameRatePresetOption.Key`: stable
    machine identifiers (`"FullHd1080"`, `"Fps30"`, ...), separate from the
    user-facing `DisplayName`, used to serialize/deserialize which preset a
    saved `ExportPreset` refers to (`FromKey`, falling back to `Custom` for
    an unrecognized key so a preset saved by a future version with more
    presets doesn't crash on load)
- `en.json` / `fr.json`: added `panel.exportPresets.*` and
  `status.exportPreset*` keys (not yet wired to a localization service,
  consistent with the other pre-existing unused keys pending v0.7.0)

- Render settings panel: full export configuration UI (v0.6.0) replacing the
  previously hardcoded preview-resolution/30 fps/`SaveFileDialog` export path:
  - **Resolution**: combo box of presets (`Preview 800x450`, `SD 854x480`,
    `HD 1280x720`, `Full HD 1920x1080`, `4K UHD 3840x2160`, `Custom...`);
    selecting `Custom...` reveals width/height text inputs
    (`MainWindowViewModel.CustomResolutionWidth` / `CustomResolutionHeight`)
  - **Frame rate**: combo box of presets (24/25/30/60/`Custom...`); selecting
    `Custom...` reveals a numeric FPS input (`CustomFrameRateValue`)
  - **Duration**: "Manual duration" now lets the user enter the export length
    directly in seconds or frames (`ManualDurationValue` /
    `ManualDurationUnit`, converted to seconds against the effective frame
    rate before being handed to `DurationMode.Manual`); "Seamless loop" keeps
    using `LoopDurationSeconds`, now with a rounding-mismatch warning shown
    when `LoopCalculator.computeFrameCount` reports
    `HasRoundingMismatch` (`HasLoopRoundingMismatch`)
  - **Frame count & file size preview**: a "Frame Count & File Size" card
    shows the exact `EstimatedTotalFrames` (from
    `Videotoy.Core.LoopCalculator.computeFrameCount`) and a rough
    `EstimatedFileSizeText` ("~12.4 MB" / "~1.8 GB"), both recalculated live
    from every input that affects them (resolution, frame rate, duration
    mode/value/unit, and whether an audio track will be muxed)
  - **Output**: a "Folder" text field plus a `Browse...` button
    (`BrowseOutputDirectoryCommand`, backed by the .NET 8 WPF
    `Microsoft.Win32.OpenFolderDialog`) replacing the old export-time
    `SaveFileDialog`, and a "File name" text field
    (`MainWindowViewModel.OutputDirectory` / `OutputFileName`); the file name
    now defaults to the loaded shader's title, sanitized against
    `Path.GetInvalidFileNameChars()` (`SanitizeAsFileName`), on every
    successful shader load
- `Videotoy.Core.ExportFileSizeEstimator` (`Videotoy.Core`): pure
  `Resolution -> FrameRate -> RateControlMode -> frameCount -> includeAudio
  -> float` byte-count estimator (`estimateFileSizeBytes`) plus a
  `"~X MB"`/`"~X GB"` formatter (`formatEstimatedFileSize`); intentionally
  approximate (a coarse CRF-to-bitrate table, documented as such) since no
  real encode has run yet at configuration time
- `MainWindowViewModel.ExportVideoCommand` now builds its `ExportSettings`
  entirely from the new panel state (resolution/frame rate/duration
  mode/output folder & file name) instead of hardcoded preview values, and
  validates it up front via `Videotoy.Core.ExportSettingsValidator.validate`
  before starting the export — an invalid configuration (e.g. missing output
  folder or file name) now surfaces immediately as an export error instead of
  failing deep inside the FFmpeg pipeline
- `Theme.xaml`: new `FieldTextBoxStyle`, `FieldComboBoxStyle` and
  `ComboBoxItemFieldStyle` control styles (rounded, bordered, accent-colored
  focus/selection states matching the existing card/button styling), used by
  every new input in the render settings panel
- `en.json` / `fr.json`: added `panel.resolution.*`, `panel.frameRate.custom`,
  `panel.durationMode.unit.*`, `panel.durationMode.loop*`,
  `panel.framePreview.*`, and `panel.output.*` keys (not yet wired to a
  localization service, consistent with the other pre-existing unused keys
  pending v0.7.0)

- "Include audio track" export option: when the loaded shader declares an
  `iChannel` audio source, `MainWindowViewModel.HasAudioChannel` (recomputed
  on every successful shader load, via the same
  `ResolveExportAudioSourceFilePath` resolution already used for muxing)
  drives a new, otherwise-hidden "Audio" card in the render settings panel
  with an "Include audio track" checkbox bound to
  `IncludeAudioInExport` (defaults to `true` on every new shader load).
  `ExportVideoCommand` now only passes the resolved audio source path down
  to `VideoExportPipeline.RunAsync` when both `HasAudioChannel` and
  `IncludeAudioInExport` are true; unchecking it produces a silent,
  video-only export without touching any other export parameter
- Render settings panel: "Audio" card (checkbox + explanatory caption),
  shown only when `HasAudioChannel` is true, disabled while an export is
  already running — same visibility/disable pattern already used by the
  export progress and error cards
- `en.json` / `fr.json`: added `panel.audio`, `panel.audio.includeAudioTrack`,
  and `panel.audio.includeAudioTrack.description` keys (not yet wired to a
  localization service, consistent with the other pre-existing unused keys
  pending v0.7.0)

- Strict audio/render-timeline alignment: `VideoExportPipeline.RunAsync` no
  longer takes a pre-built `FfmpegAudioTrackOptions`, but a plain
  `audioSourceFilePath` string; it now computes the audio track's muxed
  duration itself, from the frame count actually rendered rather than the
  raw requested duration
- `Videotoy.Core.LoopCalculator.effectiveDurationSeconds`: pure
  `FrameCountResult -> FrameRate -> float` helper returning
  `frameCount / frameRate`, i.e. the duration truly covered by the frames
  that will be rendered and written, as opposed to the requested
  `DurationMode` seconds value. The two can differ very slightly in
  `SeamlessLoop` mode whenever `loopSeconds * fps` isn't a whole number
  (`FrameCountResult.HasRoundingMismatch`), since the frame count is then
  rounded; muxing audio against the requested duration instead of this
  effective one could leave a few extra silent milliseconds — or cut the
  last audio samples short — right at the loop seam
- `MainWindowViewModel.ResolveExportAudioSourceFilePath` (renamed from
  `ResolveExportAudioTrack`): now resolves and returns only the audio
  source file path, deliberately not a duration — `VideoExportPipeline` is
  the only place with access to the actual rendered frame count, so it
  alone builds the final `FfmpegAudioTrackOptions` with the correct
  effective duration
- `Videotoy.Core.ExportSettingsValidator.resolveDurationSeconds`'s doc
  comment now explicitly warns against using the requested duration to
  align a muxed audio track, pointing to `effectiveDurationSeconds` instead

- Audio track muxing on export: when the loaded shader declares an
  `iChannel` of type `Music`/`MusicStream`,
  `MainWindowViewModel.ResolveExportAudioTrack` resolves the declared
  channel path (`Videotoy.Core.ShaderModel.firstAudioChannelPath`, new
  helper picking the first audio channel across every pass) against the
  shader's own directory — the same resolution logic already used by
  `ShaderFileService` at load time — and passes it down to
  `VideoExportPipeline.RunAsync` as an optional `FfmpegAudioTrackOptions`
  (source file path + export duration in seconds). No audio track is
  produced when the shader has no audio `iChannel`, or when the resolved
  source file no longer exists on disk
- `FfmpegEncodingOptions.AudioTrack` (`FfmpegAudioTrackOptions`,
  `Videotoy.Ffmpeg`): carries the resolved audio source path and the
  target export duration through to `FfmpegService`
- `FfmpegService.Start`'s argument list now conditionally adds the audio
  source file as a second FFmpeg input (`-i <path>`) alongside the raw
  video stream already piped through stdin (`-f rawvideo ... -i pipe:0`
  as input 0); both streams are muxed and encoded by the same FFmpeg
  process via `-map 0:v -map 1:a`, `-c:a aac -b:a 192k`, in a single
  encoding pass — no intermediate file, no second FFmpeg invocation. The
  audio track is encoded to AAC (standard for the MP4 container)
  regardless of its original format (WAV/MP3/OGG), which FFmpeg decodes
  itself
- Audio/video alignment: `-t <exportDurationSeconds>` combined with
  `-shortest` on the FFmpeg command line caps the muxed output at the
  export's exact duration and the same `t = 0` origin as the deterministic
  render timeline, whether the source audio file is longer (truncated) or
  shorter (output ends with the shorter stream, no silence padding added)
  than the exported video
- `Videotoy.Core.ExportSettingsValidator.resolveDurationSeconds`: pure
  `ExportSettings -> float` helper unwrapping `DurationMode` (`Manual` or
  `SeamlessLoop`) into a plain duration in seconds, following the same
  C#-friendly boundary style already used by the rest of this module,
  instead of letting `Videotoy.App` pattern-match directly on the
  `DurationMode` discriminated union

- `Videotoy.Core.Domain.PerformanceMode` (`Normal` / `LowSpec of
  throttleMillisecondsPerFrame: int`), added as a new field on
  `ExportSettings`; `ExportSettingsValidator.resolveThrottleMilliseconds`
  resolves it to a plain `int` (0 for `Normal`) for the C# side, following
  the existing boundary style, and `validate` rejects a negative
  `LowSpec` throttle as `InvalidThrottleDuration`
- `VideoExportPipeline.RunAsync` now awaits `Task.Delay(throttleMs,
  cancellationToken)` after writing each frame when `PerformanceMode` is
  `LowSpec`, deliberately slowing the render/encode cadence so a modest
  machine's CPU/GPU is never pegged for the whole export; the render loop
  was already strictly sequential (render frame → write to FFmpeg's stdin
  pipe → next frame, all awaited in order) so this mode changes only the
  pacing, never drops or reorders a frame, and stays independent of any
  real-time playback loop — consistent with `FrameSequenceRenderer`'s
  deterministic, frame-index-driven timeline. The delay observes the same
  cancellation token as the rest of the export, so `CancelExport` remains
  immediately responsive even mid-throttle
- `MainWindowViewModel.IsLowSpecModeEnabled`: toggles `PerformanceMode`
  between `Normal` and `LowSpec` (fixed 50 ms/frame throttle pending a
  configurable value in the future export settings panel) on every new
  `ExportVideoCommand` run
- Render settings panel: "Low-spec mode" checkbox with an explanatory
  caption, disabled while an export is already running
- `en.json` / `fr.json`: added `panel.performance`,
  `panel.performance.lowSpecMode`, and
  `panel.performance.lowSpecMode.description` keys (not yet wired to a
  localization service, consistent with the other pre-existing unused keys
  pending v0.7.0)
- `Videotoy.Ffmpeg` project files reconstructed (`.csproj` plus every class
  it exposes — `FfmpegLocator`, `FfmpegIntegrityException`/`Verifier`,
  `FfmpegEncodingOptions`, `FfmpegEncodingException`, `FfmpegService`,
  `VideoExportProgress`, `VideoExportPipeline`) after they were found
  missing from the source tree despite being referenced by `Videotoy.sln`,
  `App.xaml.cs`, and `MainWindowViewModel.cs`; rebuilt from the FFmpeg
  process/pipe/argument-list/DI behaviour already documented for these
  types earlier in this file
- `FfmpegStderrDiagnosis` / `FfmpegStderrParser` (`Videotoy.Ffmpeg`):
  `FfmpegService` now pumps `ffmpeg.exe`'s stderr on a dedicated background
  task and accumulates it into a bounded tail buffer; on a non-zero exit
  code, the tail is matched against a table of known FFmpeg failure
  patterns (unwritable output path, disk full, unsupported codec, invalid
  resolution, invalid input stream) to produce a `FfmpegErrorCategory` plus
  a short, UI-ready `Summary` sentence, falling back to a generic message
  with the raw tail lines when no known pattern matches; surfaced end to
  end as `FfmpegEncodingException.Diagnosis`
- `FfmpegService.Cancel()`: closes the stdin pipe and kills the FFmpeg
  process tree (`Process.Kill(entireProcessTree: true)`), tolerating a
  process that has already exited or an already-broken pipe, and always
  disposes and clears the process handle afterwards so a cancelled export
  never leaves an orphaned `ffmpeg.exe` running or blocks a subsequent
  export; idempotent and safe to call when no export is in progress.
  `VideoExportPipeline.RunAsync` now calls it from every non-success exit
  path (`OperationCanceledException`, `FfmpegEncodingException`, and any
  other exception raised during rendering or writing), not just the
  success path already covered by `FfmpegService.FinishAsync`
- `MainWindowViewModel.CancelExportCommand`: cancels
  `_exportCancellationTokenSource`, enabled only while `IsExporting` is
  true; `ExportVideoCommand`'s `catch` blocks now distinguish
  `OperationCanceledException` (status message only) from
  `FfmpegEncodingException` (populates `HasExportError` /
  `ExportErrorSummary` from `Diagnosis.Summary`) from any other failure
  (falls back to `ex.Message`), resetting both on every new export attempt
- Render settings panel: "Cancel export" button in the export progress
  card, and a red-bordered error card (`HasExportError` /
  `ExportErrorSummary`) shown when an export fails
- `en.json` / `fr.json`: added `action.cancelExport`,
  `status.cancellingExport`, and `export.error.title` keys (not yet wired
  to a localization service, consistent with the other pre-existing unused
  keys pending v0.7.0)
- `ExportSettings` (`Videotoy.Core.Domain`) extended into the full set of
  export parameters: `VideoCodec` (`H264`/`H265`), `RateControlMode`
  (`ConstantRateFactor` or `TargetBitrate`, mutually exclusive), and
  `ContainerFormat` (`Mp4`) replace the previous free-form `CodecName`
  string and fixed CRF-only field
- `Videotoy.Core.ExportSettingsValidator`: validates resolution, frame
  rate, duration, output path and CRF/bitrate range (`validate`/`isValid`
  returning `ExportSettingsIssue list`), resolves the output file path from
  directory/file name/container extension, and maps `VideoCodec` /
  `RateControlMode` to their FFmpeg-facing primitives (codec library name,
  nullable CRF, nullable target bitrate) so the C# side never pattern
  matches directly on the F# discriminated unions, consistent with the
  existing `UniformBuilder` boundary style
- `FfmpegEncodingOptions.FromExportSettings`: builds encoder options
  straight from a validated `ExportSettings`, raising `ArgumentException`
  before any FFmpeg process is started if the settings are invalid
- `FfmpegService.Start` now builds the FFmpeg argument list via
  `ProcessStartInfo.ArgumentList` instead of a single concatenated
  `Arguments` string, so output paths containing spaces are passed safely
  without manual quoting; adds `-b:v` (target bitrate) as an alternative to
  `-crf`, and `-movflags +faststart` for the MP4 container
- `VideoExportPipeline.RunAsync` now takes a single `ExportSettings`
  instead of a separately constructed `FfmpegEncodingOptions` plus
  duration/frame rate, making `ExportSettings` the single source of truth
  passed down from the future export settings panel
- `VideoExportPipeline` (`Videotoy.Ffmpeg`): drives the full frame-by-frame
  export end to end — `FrameSequenceRenderer` renders each deterministic
  frame on the GPU, reads the back-buffer via `OffscreenRenderContext`
  (already in the FFmpeg-compatible BGRA byte layout, no extra pixel
  format conversion needed), and streams the pixels straight into
  `FfmpegService.WriteFrameAsync`'s stdin pipe, with no intermediate frame
  files ever written to disk; reports `VideoExportProgress`
  (current/total frame index, time) via `IProgress<T>`, propagates FFmpeg's
  exit code as `FfmpegEncodingException` on failure, and calls
  `FfmpegService.Cancel` to kill the process tree on cancellation
- `Videotoy.Ffmpeg` now references `Videotoy.Rendering` to wire the render
  and encode stages together
- Known limitation: `FrameSequenceRenderer` currently drives a single
  `IShaderRenderer` pass and does not yet go through `MultiPassRenderer`,
  so exporting a shader with Buffer A/B/C/D feedback passes is not wired
  up end to end yet
- Embedded `ffmpeg.exe` now packaged from `Videotoy.App` (moved from the
  `Videotoy.Ffmpeg` library project, which is never itself the output
  directory) so the binary is present both in local builds and in the
  Inno Setup installer output, alongside a companion `ffmpeg.exe.sha256`
  hash file
- `tools/ffmpeg/generate-hash.ps1`: PowerShell helper producing
  `ffmpeg.exe.sha256` (`sha256sum`-compatible format) from the embedded
  `ffmpeg.exe`, to be re-run whenever the binary is updated
- `Videotoy.Core.ExportProgressEstimator`: pure `estimateRemainingSeconds`
  (linear extrapolation of the average seconds-per-frame observed so far
  onto the remaining frames, returned as a C#-friendly `Nullable<float>`
  following the `ExportSettingsValidator` boundary style) and
  `progressFraction` helpers
- `VideoExportProgress` (`Videotoy.Ffmpeg`) extended with `ElapsedSeconds`,
  `EstimatedRemainingSeconds`, and the computed `CurrentFrameNumber` /
  `ProgressFraction` members; `VideoExportPipeline.RunAsync` now times the
  export with a `Stopwatch` and reports the ETA on every frame via
  `IProgress<VideoExportProgress>`
- `MainWindowViewModel.ExportVideoCommand` now runs a real export end to
  end (`SaveFileDialog` for the output path, `IShaderRenderer` init/load
  of the `Image` pass, `VideoExportPipeline.RunAsync`) and exposes
  `IsExporting`, `ExportCurrentFrame`, `ExportTotalFrames`,
  `ExportProgressPercent`, and `ExportRemainingTimeText` for data binding;
  known limitation: until the export settings panel (v0.6.0) lands, the
  export reuses the 800x450 preview resolution, a fixed 30 fps, and
  "Manual duration" driven by `LoopDurationSeconds`
- Render settings panel: export progress card (percentage, flat
  `ExportProgressBarStyle` progress bar, current/total frame counter, ETA)
  shown while `IsExporting` is true
- `en.json` / `fr.json`: added `status.exportComplete`,
  `status.exportFailed`, `status.exportCancelled`, and
  `export.progress.*` keys (not yet wired to a localization service,
  consistent with the other pre-existing unused keys pending v0.7.0)

### Fixed

- `Videotoy.Core.ShadertoyJsonParser`: `open Videotoy.Core.ShaderModel`
  brings `IssueSeverity.Error` (a nullary union case) into scope, which
  shadowed `FSharp.Core`'s `Result.Error` and made every `Error [ ... ]`
  call fail to compile (`FS0003: This value is not a function and cannot
  be applied`); all four call sites now use the fully qualified
  `Result.Error` to disambiguate
- `Videotoy.Core.Domain`: converted from a top-level `module` to a plain
  `namespace` — since the file only ever declared pure data types
  (`Resolution`, `ExportSettings`, `DurationMode`, ...) with no functions,
  compiling it as a module wrapped every type as a *nested type* of a
  generated `Domain` class, which made every `using Videotoy.Core.Domain;`
  in C# fail with `CS0138` ('Domain' is a type, not a namespace) and
  cascade into `CS0246` for `DurationMode`, `FrameRate`, `ExportSettings`,
  etc. across `FrameSequenceRenderer`, `MainWindowViewModel`,
  `VideoExportPipeline`, and `FfmpegService`
- `Videotoy.Core.ShaderModel` mixes types and functions and must stay a
  `module` (F# namespaces cannot hold `let` bindings), so instead
  `MultiPassRenderer.cs` and `ShaderIssueViewModel.cs` were switched from
  `using Videotoy.Core.ShaderModel;` + unqualified `ShaderProject` /
  `ShaderIssue` to the fully-qualified `Videotoy.Core.ShaderModel.X` form
  already used successfully elsewhere (`Videotoy.Media.ShaderFileService`)
- `Videotoy.Media.LoadedShader.HasErrors` called
  `issue.Severity.IsErrorIssue`, but `IsErrorIssue` is a member of
  `ShaderIssue` itself (not of `IssueSeverity`); fixed to
  `issue.IsErrorIssue`
- Removed `Videotoy.Ffmpeg.FfmpegEncodingService` / its `FfmpegEncodingOptions`:
  a leftover early prototype of the FFmpeg process launcher, superseded by
  `FfmpegService` (registered in DI, used by `VideoExportPipeline`) but
  never deleted, which duplicated the `FfmpegEncodingOptions` type
  (`CS0101`) already declared in `FfmpegService.cs`
- `Videotoy.Rendering` (`OffscreenRenderContext`, `MultiPassRenderer`,
  `D3D11ShaderRenderer`) mismatched the installed Vortice.Direct3D11
  bindings in several places, confirmed against the upstream
  `Mappings.xml`:
  - `Texture2DDescription.Width`/`.Height` are `uint` in this Vortice
    version; `OffscreenRenderContext.Resize` now casts from
    `RenderTargetSize`'s `int` fields instead of assigning directly
  - `SamplerDescription`'s comparison field keeps its native name
    `ComparisonFunc` (its *type* is the renamed `ComparisonFunction`
    enum, but the field itself was never renamed) and its upper mip
    bound is `MaxLOD`, not `ComparisonFunction`/`MaxLod` as originally
    written; fixed in both `MultiPassRenderer` and `D3D11ShaderRenderer`
  - `ID3D11DeviceContext.Map`'s last parameter is
    `Vortice.Direct3D11.MapFlags`, ambiguous with `Vortice.DXGI.MapFlags`
    once both namespaces are imported; `OffscreenRenderContext.ReadPixelsRgba`
    now fully qualifies it
  - `PSSetShaderResource`/`PSSetSampler` take a `uint` slot index;
    `MultiPassRenderer.RenderSlot` now casts the buffer channel index
    (`int`) accordingly
  - `OffscreenRenderContext.BindRenderTarget` also silences a
    possible-null-reference warning on `_renderTargetView` with `!`,
    since `EnsureInitialized()` already guarantees it is set
  - `MultiPassRenderer.RenderSlot`'s SRV-unbind call (`PSSetShaderResource(slot, null)`)
    silences `CS8625` with `null!`, since the generated binding doesn't
    mark the parameter nullable even though passing null to unbind is
    the intended, supported usage
- `Videotoy.App.App.ConfigureServices` registered `MainWindow` (bare,
  unqualified) for DI, but `MainWindow` lives in `Videotoy.App.Views`
  (see `OnStartup`, which correctly resolves `Views.MainWindow`); the
  unqualified reference only compiled by accident as part of the
  temporary WPF `x:Class` codegen project and failed the real build with
  `CS0246`. Fixed to `Views.MainWindow` for consistency with `OnStartup`
- `FfmpegIntegrityVerifier` (`Videotoy.Ffmpeg`): recomputes the embedded
  `ffmpeg.exe`'s SHA-256 hash via `FfmpegLocator.ComputeSha256` and compares
  it against `tools/ffmpeg/ffmpeg.exe.sha256`, throwing
  `FfmpegIntegrityException` on mismatch or on a missing hash file
- `App.OnStartup` now runs `FfmpegIntegrityVerifier` before the main window
  is shown; a failed check displays an error dialog and shuts the
  application down instead of proceeding with a potentially corrupted or
  tampered binary
- Inno Setup script (`installer/Videotoy.iss`) fails to compile with an
  explicit `#error` if `ffmpeg.exe` is missing from the `Release` build
  output, instead of silently producing an installer without it
- `FfmpegEncodingService` renamed to `FfmpegService` (`Videotoy.Ffmpeg`):
  launches the embedded `ffmpeg.exe` process and streams raw RGB/BGRA
  frames directly to its stdin pipe (`WriteFrameAsync`) with no
  intermediate frame files ever written to disk; `Start` guards against a
  second concurrent run, `WriteFrameAsync` fails fast if the process has
  already exited, and `FinishAsync` returns FFmpeg's exit code after
  closing the pipe and awaiting process completion
- GLSL Shadertoy → HLSL transpiler (`Videotoy.Core.GlslToHlslTranspiler`):
  source-level translation of vector/matrix types (`vec2`/`vec3`/`vec4`,
  `mat2`/`mat3`/`mat4`, `ivec*`, `uvec*`, `bvec*`), intrinsic renaming
  (`mix`→`lerp`, `fract`→`frac`, `mod`→`fmod`, `atan`→`atan2`,
  `inversesqrt`→`rsqrt`, `discard`→`clip`), `mainImage` → `PSMain`
  (`SV_Target`/`SV_Position`) entry-point rewriting, and `iChannelN`
  texture/sampler declarations with `texture(iChannelN, uv)` →
  `iChannelN.Sample(iChannelNSampler, uv)` rewriting
  wrapped in the Shadertoy `cbuffer` (`iResolution`, `iTime`, `iTimeDelta`,
  `iFrame`, `iMouse`, `iDate`, `iSampleRate`, `iChannelResolution`)
- Transpilation diagnostics reported as `ShaderIssue` (missing `mainImage`,
  unsupported constructs), merged into the existing shader issues panel
- `ShaderFileService` now transpiles every loaded pass to HLSL and exposes
  the per-pass `TranspileResult` (`HlslPasses`) alongside the parsed project
- Offscreen D3D11 rendering pipeline (`Videotoy.Rendering`):
  - `OffscreenRenderContext`: hardware D3D11 device creation with automatic
    WARP (software) fallback, an offscreen `Texture2D` render target plus
    matching staging texture for CPU readback, `Resize` to switch between
    the fixed 800x450 preview target and an arbitrary export resolution
    without recreating the device, and row-pitch-aware pixel readback
  - `ShadertoyUniformsBuffer`: 128-byte, 16-byte-aligned struct mirroring
    the transpiler's `cbuffer ShadertoyUniforms` layout exactly
  - `D3D11ShaderRenderer` (`IShaderRenderer` implementation): compiles a
    fullscreen-triangle vertex shader and the transpiled HLSL pixel shader
    via `Vortice.D3DCompiler`, uploads per-frame uniforms to a dynamic
    constant buffer, draws, and returns the rendered pixels
  - `RenderTargetSize`: resolution value type with an `IsValid` guard and
    an `800x450` preview default
  - `Videotoy.Core.GlslToHlslTranspiler`'s `cbuffer` layout corrected to
    explicit 16-byte register packing (128 bytes total) to match the new
    `ShadertoyUniformsBuffer` byte-for-byte
- `IShaderRenderer` now takes a `RenderTargetSize` and exposes `Resize`,
  used by `App.xaml.cs` to register `D3D11ShaderRenderer` as the default
  renderer (`NullShaderRenderer` remains available as a no-op fallback)
- `FrameSequenceRenderer` (`Videotoy.Rendering`): drives a full export pass
  frame-by-frame in memory from `Videotoy.Core.LoopCalculator`'s
  deterministic timeline (`computeFrameCount` / `buildFrameTimeline`),
  where each frame's `iTime`/`iTimeDelta` is derived purely from its frame
  index and the target frame rate, independent of wall-clock or machine
  speed; each call to `RenderedFrame` yields the rendered RGBA pixels for
  one frame with no real-time playback loop involved
  - `RenderedFrame`: immutable record (`Index`, `TimeSeconds`, `PixelsRgba`)
    carrying one rendered frame's data in memory
- Multi-pass rendering with ping-pong render targets:
  - `Videotoy.Core.PassGraph`: computes the topological execution order of
    Buffer A/B/C/D + Image passes from their `iChannelN` buffer references
    (buffer-name matching is whitespace/case-insensitive to tolerate
    Shadertoy export variants such as `"BufferA"` vs `"Buffer A"`),
    distinguishes a self-referencing pass (legitimate ping-pong feedback,
    e.g. Buffer A reading its own previous frame) from a genuine multi-pass
    circular dependency (raised as an error), and resolves per-channel
    buffer bindings for the renderer
  - `OffscreenRenderContext` now exposes a `ShaderResourceView` on its
    color texture and accepts an externally-owned `ID3D11Device` /
    `ID3D11DeviceContext`, so multiple render targets across passes can
    sample each other's output on a single shared device
  - `MultiPassRenderer` (`Videotoy.Rendering`): compiles and renders every
    pass of a `ShaderProject` in dependency order on one shared D3D11
    device; self-referencing buffers get a front/back pair of render
    targets (front = last completed frame, readable; back = current
    frame's write target), swapped after every pass has rendered so a
    buffer's feedback always samples the previous frame's stable result,
    never the frame currently being written; returns the final `Image`
    pass's RGBA pixels
- Real-time preview playback loop with a scrubbable timeline:
  - `PreviewClock` (`Videotoy.Rendering`): UI-framework-agnostic playback
    clock exposing play/pause/seek/stop and automatic looping over a
    configurable duration; advances purely from externally-supplied
    real-time deltas (`Advance(deltaSeconds)`), independent of any
    particular timer implementation
  - `MainWindow` now drives `PreviewClock` from `CompositionTarget.Rendering`
    (WPF's vsync-aligned render loop), measuring the actual elapsed
    wall-clock time via `Stopwatch` each frame
  - `MainWindowViewModel`: renders the current preview frame through
    `MultiPassRenderer` into a `WriteableBitmap` bound to the viewport,
    with `TogglePlayback`/`StopPlayback`/`BeginScrub`/`EndScrub` commands
    and a `Seek` method used while the user drags the timeline
  - Viewport UI: the static "Preview rendering will be available in a
    future version" placeholder is replaced by a live `Image` bound to
    the rendered bitmap, with a play/pause button, a stop button, a
    `Slider` timeline with progress cursor, and the current playback time
  - `PlaybackIconConverter`: swaps the play/pause button glyph based on
    `IsPlaying`
  - Timeline scrubbing pauses `PreviewClock` for the duration of the drag
    (mouse-down to mouse-up on the slider) so the user's cursor position
    is never overwritten by the ongoing playback update, and resumes
    playback afterwards only if it was already running

## [0.3.0] - 2026-08-30

### Added

- New `Videotoy.Media` project: texture loading, audio decoding, deterministic
  audio-reactive texture generation, and recent-files persistence
- Shader loading via the "File > Open Shader..." menu (`OpenFileDialog`)
- Drag & drop validation restricted to `.glsl`, `.frag`, `.json` and
  `.shadertoy` files, with visual drop-effect feedback
- Shadertoy JSON export parser (`ShadertoyJsonParser`): multi-pass wiring
  (Image, Buffer A/B/C/D, Common), `iChannel` input resolution (buffer,
  texture, video, cubemap, music/musicstream)
- Lightweight structural shader validation (`ShaderValidator`): missing
  `mainImage` entry point, unbalanced braces/parentheses, stray `#version`
  directives — surfaced as line-numbered errors/warnings
- Dedicated, collapsible "Shader Issues" panel listing parsing/validation
  errors and warnings per pass, with line numbers
- Extended Shadertoy uniform model (`UniformValues` / `UniformBuilder`):
  `iResolution`, `iTime`, `iTimeDelta`, `iFrame`, `iMouse`, `iDate`,
  `iSampleRate`, per-channel resolutions
- Texture loading for `iChannel` image inputs (`TextureLoader`, WIC-based,
  BGRA32 output)
- Audio decoding for `iChannel` audio inputs (`AudioTrackLoader`): WAV, MP3
  and OGG via NAudio / NAudio.Vorbis
- Deterministic 512x2 audio spectrum/waveform texture generation
  (`AudioSpectrumTextureGenerator`), computed from `iTime` per frame rather
  than real-time playback
- Recent shaders list: menu entries backed by JSON persistence in
  `%AppData%\Videotoy\recent-shaders.json`

## [0.2.0] - 2026-08-30

### Added

- Custom window chrome (no native title bar) with Windows 11 Mica backdrop
  and rounded corners via DWM interop
- Fixed 800x450 preview viewport, centered and non-resizable
- Top menu bar (File, Edit, Render, Help) with vector icon glyphs
- Bottom status bar showing status message, current frame, and FPS
- Collapsible settings panel with animated width transition
- Single light theme (brushes, shadows, glow effect, control styles)
- About window bound to application metadata (name, SemVer version,
  copyright, email, website)
- Basic drag & drop file handling on the main window

## [0.1.0] - 2026-08-30

### Added

- Solution structure with four projects: `Videotoy.App` (WPF, C#),
  `Videotoy.Core` (F#), `Videotoy.Rendering` (C#), `Videotoy.Ffmpeg` (C#)
- Shared MSBuild configuration (`Directory.Build.props`, `.editorconfig`)
- Automatic SemVer versioning scaffolding
- Base repository documentation: `README.md`, `CHANGELOG.md`,
  `COMPILATION.md`, `LICENSE` (MIT)
- Continuous integration workflow (Windows build)
- Base folder structure (`/src`, `/assets`, `/tools/ffmpeg`, `/docs`,
  `/installer`)
