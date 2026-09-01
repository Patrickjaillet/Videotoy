namespace Videotoy.Transpiler;

/// <summary>
/// Levée lorsque <c>tint.exe</c> est simplement absent (jamais vendu) —
/// distincte de <see cref="TintIntegrityException"/> (binaire présent mais
/// corrompu/altéré). Le support WGSL étant entièrement optionnel
/// (contrairement à FFmpeg, requis pour tout export), cette exception ne
/// doit jamais faire planter l'application : elle est convertie en
/// <c>ShaderIssue</c> par l'appelant (voir le routeur de transpileurs dans
/// <c>Videotoy.App</c>), jamais propagée jusqu'à un niveau fatal.
/// </summary>
public sealed class TintNotAvailableException : Exception
{
    public TintNotAvailableException(string message)
        : base(message)
    {
    }

    public TintNotAvailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
