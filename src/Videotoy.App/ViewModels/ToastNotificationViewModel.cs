namespace Videotoy.App.ViewModels;

public enum ToastSeverity
{
    Success,
    Error
}

/// <summary>
/// A single non-intrusive toast notification (export success/failure, etc.),
/// shown for a limited duration in a corner overlay and then dismissed
/// automatically. Instances are immutable: the auto-dismiss timer and manual
/// close both operate on the owning <see cref="MainWindowViewModel.Toasts"/>
/// collection rather than on mutable state here, since a toast carries no
/// state beyond its message once created.
/// </summary>
public sealed class ToastNotificationViewModel
{
    public required Guid Id { get; init; }

    public required ToastSeverity Severity { get; init; }

    public required string Title { get; init; }

    public required string Message { get; init; }
}
