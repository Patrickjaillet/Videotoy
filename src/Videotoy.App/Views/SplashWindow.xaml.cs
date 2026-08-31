using System.Windows;

namespace Videotoy.App.Views;

/// <summary>
/// Animated splash screen shown immediately on startup, while the FFmpeg
/// integrity check and the rest of the DI-registered services finish
/// initializing in the background. Closed by <c>App.OnStartup</c> right
/// before <see cref="MainWindow"/> is shown.
/// </summary>
public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }
}
