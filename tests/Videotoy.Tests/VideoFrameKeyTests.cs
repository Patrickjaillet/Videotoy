using Videotoy.Ffmpeg;
using Xunit;

namespace Videotoy.Tests;

public class VideoFrameKeyTests
{
    [Fact]
    public void DifferentTargetResolutions_ProduceDifferentKeys()
    {
        // Régression : VideoTextureLoader/VideoFrameCache sont partagés (DI
        // singleton) entre le renderer d'aperçu (basse résolution) et le
        // renderer d'export (résolution choisie par l'utilisateur) — sans la
        // résolution cible dans la clé, une frame décodée pour l'aperçu
        // serait réutilisée telle quelle pour un export à une résolution
        // différente (voir VideoFrameKey.cs).
        var previewKey = new VideoFrameKey("clip.mp4", 10, 960, 540);
        var exportKey = new VideoFrameKey("clip.mp4", 10, 3840, 2160);

        Assert.NotEqual(previewKey, exportKey);
    }

    [Fact]
    public void SameParameters_ProduceEqualKeys()
    {
        var a = new VideoFrameKey("clip.mp4", 10, 1920, 1080);
        var b = new VideoFrameKey("clip.mp4", 10, 1920, 1080);

        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentFrameIndex_ProducesDifferentKeys()
    {
        var a = new VideoFrameKey("clip.mp4", 10, 1920, 1080);
        var b = new VideoFrameKey("clip.mp4", 11, 1920, 1080);

        Assert.NotEqual(a, b);
    }
}
