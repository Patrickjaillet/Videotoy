using System;
using System.IO;
using Videotoy.Ffmpeg;
using Xunit;

namespace Videotoy.Tests;

public class TransientFfmpegErrorClassifierTests
{
    [Fact]
    public void IsTransient_PlainIOException_IsNeverTransient()
    {
        // Régression : un disque plein remonte comme IOException (pipe stdin
        // rompu) avant que FfmpegService ne le traduise en
        // FfmpegEncodingException diagnostiquée ; une IOException brute qui
        // atteint malgré tout ce classifieur ne doit jamais être retentée
        // automatiquement (voir TransientFfmpegErrorClassifier.cs).
        var exception = new IOException("The pipe has been ended.");

        Assert.False(TransientFfmpegErrorClassifier.IsTransient(exception));
    }

    [Fact]
    public void IsTransient_DiskFullDiagnosis_IsNeverTransient()
    {
        var diagnosis = new FfmpegStderrDiagnosis(FfmpegErrorCategory.DiskFull, "No space left on device", string.Empty);
        var exception = new FfmpegEncodingException(1, diagnosis);

        Assert.False(TransientFfmpegErrorClassifier.IsTransient(exception));
    }

    [Theory]
    [InlineData(FfmpegErrorCategory.Unknown)]
    [InlineData(FfmpegErrorCategory.InvalidInputStream)]
    public void IsTransient_RetryableCategories_AreTransient(FfmpegErrorCategory category)
    {
        var diagnosis = new FfmpegStderrDiagnosis(category, "summary", string.Empty);
        var exception = new FfmpegEncodingException(1, diagnosis);

        Assert.True(TransientFfmpegErrorClassifier.IsTransient(exception));
    }

    [Fact]
    public void IsTransient_UnrelatedException_IsNeverTransient()
    {
        Assert.False(TransientFfmpegErrorClassifier.IsTransient(new InvalidOperationException()));
    }
}
