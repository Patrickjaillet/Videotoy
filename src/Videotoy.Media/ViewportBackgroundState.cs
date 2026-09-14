namespace Videotoy.Media;

public enum ViewportBackgroundMode
{
    Checkerboard,
    SolidColor,
    ReferenceImage
}

/// <summary>
/// Serializable snapshot of the user's preview viewport background
/// preference, persisted in
/// <c>%AppData%\Videotoy\viewport-background.json</c> — a whole-application
/// UI preference (not per-shader), mirroring <see cref="OnboardingState"/>.
/// Lets a shader exporting with a straight alpha channel (Phase v2.1.0) be
/// checked visually in the live preview against a transparent checkerboard,
/// a solid color, or a reference image, instead of always compositing over
/// the fixed opaque viewport background.
/// </summary>
public sealed class ViewportBackgroundState
{
    public ViewportBackgroundMode Mode { get; set; } = ViewportBackgroundMode.Checkerboard;

    /// <summary>ARGB hex string (e.g. "#FF203040"), used when <see cref="Mode"/> is <see cref="ViewportBackgroundMode.SolidColor"/>.</summary>
    public string SolidColorHex { get; set; } = "#FF2A2D31";

    /// <summary>Absolute file path, used when <see cref="Mode"/> is <see cref="ViewportBackgroundMode.ReferenceImage"/>.</summary>
    public string ReferenceImagePath { get; set; } = "";
}
