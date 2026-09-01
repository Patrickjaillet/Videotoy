namespace Videotoy.Transpiler;

public sealed class TintIntegrityException : Exception
{
    public TintIntegrityException(string message)
        : base(message)
    {
    }

    public TintIntegrityException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
