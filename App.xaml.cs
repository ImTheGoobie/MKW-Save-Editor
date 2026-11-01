using Microsoft.UI.Xaml;
namespace MKWWinUI;
public partial class App : Application
{
    public App() => InitializeComponent();
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow();
        window.Activate();
    }
}
