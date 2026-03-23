using Microsoft.UI.Xaml;

namespace ProductListApp;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        this.InitializeComponent();
    }

    private void InitializeComponent()
    {
        throw new NotImplementedException();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
