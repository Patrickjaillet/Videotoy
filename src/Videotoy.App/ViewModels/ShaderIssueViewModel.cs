namespace Videotoy.App.ViewModels;

public sealed class ShaderIssueViewModel
{
    public required string PassName { get; init; }

    public required int Line { get; init; }

    public required string Message { get; init; }

    public required bool IsError { get; init; }

    public static ShaderIssueViewModel FromIssue(Videotoy.Core.ShaderModel.ShaderIssue issue)
    {
        return new ShaderIssueViewModel
        {
            PassName = issue.PassName,
            Line = issue.Line,
            Message = issue.Message,
            IsError = issue.IsErrorIssue
        };
    }
}
