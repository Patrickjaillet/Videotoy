using System.Windows;
using Videotoy.App.Localization;
using Videotoy.App.ViewModels;

namespace Videotoy.App.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        DataContext = new AboutViewModel(LocalizationRuntime.Service);
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
