using Videotoy.Core;

namespace Videotoy.App.ViewModels;

public sealed record VideoTimeMappingOption(string Key, string DisplayName, VideoTimeMapping.VideoTimeMappingMode Value)
{
    public static readonly VideoTimeMappingOption Linear = new("Linear", "Linear", VideoTimeMapping.VideoTimeMappingMode.Linear);
    public static readonly VideoTimeMappingOption Looped = new("Looped", "Looped", VideoTimeMapping.VideoTimeMappingMode.Looped);
    public static readonly VideoTimeMappingOption FrozenOnLastFrame = new(
        "FrozenOnLastFrame", "Frozen on last frame", VideoTimeMapping.VideoTimeMappingMode.FrozenOnLastFrame);

    public static readonly IReadOnlyList<VideoTimeMappingOption> All = [Linear, Looped, FrozenOnLastFrame];

    public static VideoTimeMappingOption FromValue(VideoTimeMapping.VideoTimeMappingMode value) =>
        All.FirstOrDefault(option => option.Value.Equals(value)) ?? Looped;
}
