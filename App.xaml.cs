using Microsoft.UI.Xaml;
using LedgerDesk.Services;

namespace LedgerDesk;

public partial class App : Application
{
    private Window? _window;

    public static DatabaseService Database { get; private set; } = null!;
    public static SettingsService Settings { get; private set; } = null!;
    public static LicenseService License { get; private set; } = null!;
    public static AuthService Auth { get; private set; } = null!;
    public static LocalizationService Localization { get; private set; } = null!;

    public App()
    {
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Database = new DatabaseService();
        Settings = new SettingsService(Database);
        License = new LicenseService(Database);
        Auth = new AuthService(Database);
        Localization = new LocalizationService(Database);
        Localization.Initialize(Settings.Get("language", "en"));

        _window = new MainWindow();
        _window.Activate();
    }
}
