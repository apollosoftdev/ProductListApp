using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using WinRT.Interop;

namespace LedgerDesk.KeyGen;

public sealed partial class MainWindow : Window
{
    // Must match LicenseService salts in the main app
    private const string Salt = "LedgerDesk-2026-License-Salt";

    public MainWindow()
    {
        this.InitializeComponent();
        this.Title = "LedgerDesk Key Generator";

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new Windows.Graphics.SizeInt32(520, 500));
    }

    private void Generate_Click(object sender, RoutedEventArgs e)
    {
        var sn = SnInput.Text?.Trim() ?? "";

        if (string.IsNullOrEmpty(sn))
        {
            ShowError("Please enter a serial number.");
            return;
        }

        // Clean SN: remove dashes/spaces, validate hex
        var clean = sn.ToUpperInvariant().Replace("-", "").Replace(" ", "");

        if (clean.Length != 16 || !clean.All(c => "0123456789ABCDEF".Contains(c)))
        {
            ShowError("Invalid serial number. Expected format: XXXX-XXXX-XXXX-XXXX (16 hex characters).");
            return;
        }

        var key = GenerateKey(clean);
        ResultKey.Text = key;
        ResultPanel.Visibility = Visibility.Visible;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(ResultKey.Text);
        Clipboard.SetContent(package);
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        ResultPanel.Visibility = Visibility.Collapsed;
    }

    private static string GenerateKey(string serialNumber)
    {
        var keyBytes = Encoding.UTF8.GetBytes(Salt);
        var snBytes = Encoding.UTF8.GetBytes(serialNumber);

        var hmac = HMACSHA256.HashData(keyBytes, snBytes);

        var sb = new StringBuilder();
        for (int i = 0; i < 25; i++)
        {
            sb.Append(hmac[i % hmac.Length] % 10);
            if (i % 5 == 4 && i < 24)
                sb.Append('-');
        }

        return sb.ToString();
    }
}
