using System.Collections.ObjectModel;
using LedgerDesk.Models;
using LedgerDesk.Services;

namespace LedgerDesk.ViewModels;

public class SettingsViewModel : BaseViewModel
{
    private readonly DatabaseService _db;
    private readonly AuthService _auth;
    private readonly LicenseService _license;
    private readonly SettingsService _settings;

    // --- Password ---
    private string _oldPassword = string.Empty;
    private string _newPassword = string.Empty;
    private string _confirmPassword = string.Empty;
    private string _passwordMessage = string.Empty;
    private bool _passwordMessageIsError;

    public string OldPassword { get => _oldPassword; set => SetProperty(ref _oldPassword, value); }
    public string NewPassword { get => _newPassword; set => SetProperty(ref _newPassword, value); }
    public string ConfirmPassword { get => _confirmPassword; set => SetProperty(ref _confirmPassword, value); }
    public string PasswordMessage { get => _passwordMessage; set => SetProperty(ref _passwordMessage, value); }
    public bool PasswordMessageIsError { get => _passwordMessageIsError; set => SetProperty(ref _passwordMessageIsError, value); }

    // --- Categories ---
    private ObservableCollection<Category> _categories = [];
    private string _newCategoryName = string.Empty;

    public ObservableCollection<Category> Categories { get => _categories; set => SetProperty(ref _categories, value); }
    public string NewCategoryName { get => _newCategoryName; set => SetProperty(ref _newCategoryName, value); }

    // --- Theme ---
    private string _selectedTheme = "Default";
    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (SetProperty(ref _selectedTheme, value))
                _settings.Set("theme", value);
        }
    }

    // --- License ---
    private string _serialNumber = string.Empty;
    private string _licenseKey = string.Empty;

    public string SerialNumber { get => _serialNumber; set => SetProperty(ref _serialNumber, value); }
    public string LicenseKey { get => _licenseKey; set => SetProperty(ref _licenseKey, value); }

    public SettingsViewModel(DatabaseService db, AuthService auth, LicenseService license, SettingsService settings)
    {
        _db = db;
        _auth = auth;
        _license = license;
        _settings = settings;
    }

    public void Load()
    {
        // Categories
        var cats = _db.GetAllCategories();
        Categories.Clear();
        foreach (var c in cats)
            Categories.Add(c);

        // Theme
        _selectedTheme = _settings.Get("theme", "Default");
        OnPropertyChanged(nameof(SelectedTheme));

        // License
        SerialNumber = _license.GetSerialNumber();
        LicenseKey = _db.GetSetting("license_key") ?? "";

        // Reset password fields
        OldPassword = string.Empty;
        NewPassword = string.Empty;
        ConfirmPassword = string.Empty;
        PasswordMessage = string.Empty;
    }

    // --- Password Actions ---

    public bool TryChangePassword()
    {
        if (string.IsNullOrEmpty(OldPassword))
        {
            PasswordMessage = "Enter your current password.";
            PasswordMessageIsError = true;
            return false;
        }

        if (NewPassword.Length < 4)
        {
            PasswordMessage = "New password must be at least 4 characters.";
            PasswordMessageIsError = true;
            return false;
        }

        if (NewPassword != ConfirmPassword)
        {
            PasswordMessage = "New passwords do not match.";
            PasswordMessageIsError = true;
            return false;
        }

        if (!_auth.ChangePassword(OldPassword, NewPassword))
        {
            PasswordMessage = "Current password is incorrect.";
            PasswordMessageIsError = true;
            return false;
        }

        PasswordMessage = "Password changed successfully.";
        PasswordMessageIsError = false;
        OldPassword = string.Empty;
        NewPassword = string.Empty;
        ConfirmPassword = string.Empty;
        return true;
    }

    // --- Category Actions ---

    public bool AddCategory()
    {
        var name = NewCategoryName?.Trim() ?? "";
        if (string.IsNullOrEmpty(name)) return false;

        if (Categories.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return false;

        var maxOrder = Categories.Count > 0 ? Categories.Max(c => c.SortOrder) + 1 : 0;
        _db.AddCategory(name, maxOrder);
        NewCategoryName = string.Empty;

        RefreshCategories();
        return true;
    }

    public void RenameCategory(int id, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;
        _db.RenameCategory(id, newName.Trim());
        RefreshCategories();
    }

    public void DeleteCategory(int id, string? reassignTo)
    {
        var cat = Categories.FirstOrDefault(c => c.Id == id);
        if (cat is null) return;

        if (!string.IsNullOrEmpty(reassignTo))
            _db.ReassignCategory(cat.Name, reassignTo);

        _db.DeleteCategory(id);
        RefreshCategories();
    }

    private void RefreshCategories()
    {
        var cats = _db.GetAllCategories();
        Categories.Clear();
        foreach (var c in cats)
            Categories.Add(c);
    }

    // --- License Actions ---

    public void Deactivate()
    {
        _license.Deactivate();
    }
}
