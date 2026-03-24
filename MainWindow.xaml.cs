using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using LedgerDesk.Models;
using LedgerDesk.Services;
using LedgerDesk.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace LedgerDesk;

public sealed partial class MainWindow : Window
{
    public DashboardViewModel ViewModel { get; }
    private readonly MainViewModel _mainViewModel;
    private readonly RecordFormViewModel _formViewModel;
    private readonly RecordDetailViewModel _detailViewModel;
    private readonly FilterViewModel _filterViewModel;
    private readonly ActivationViewModel _activationViewModel;
    private LoginViewModel _loginViewModel;
    private readonly SettingsViewModel _settingsViewModel;

    public MainWindow()
    {
        ViewModel = new DashboardViewModel(App.Database);
        _mainViewModel = new MainViewModel();
        _formViewModel = new RecordFormViewModel(App.Database);
        _detailViewModel = new RecordDetailViewModel(App.Database);
        _filterViewModel = new FilterViewModel();
        _activationViewModel = new ActivationViewModel(App.License);
        _loginViewModel = new LoginViewModel(App.Auth);
        _settingsViewModel = new SettingsViewModel(App.Database, App.Auth, App.License, App.Settings);

        _filterViewModel.FilterChanged += OnFilterChanged;
        App.Localization.LanguageChanged += RefreshAllText;

        this.InitializeComponent();

        RootGrid.DataContext = ViewModel;

        SetWindowSize(1200, 780);
        SetupTitleBar();
        TrySetMicaBackdrop();
        RefreshAllText();

        // Startup flow: license → login → main
        DetermineStartupScreen();
    }

    // ============================
    //  Window / Title Bar / Mica
    // ============================

    private void SetWindowSize(int width, int height)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
    }

    private void SetupTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
    }

    private void TrySetMicaBackdrop()
    {
        if (MicaController.IsSupported())
            this.SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
        else if (DesktopAcrylicController.IsSupported())
            this.SystemBackdrop = new DesktopAcrylicBackdrop();
    }

    // ============================
    //  Navigation
    // ============================

    private void ShowPanel(string panel)
    {
        ActivationPanel.Visibility = panel == "Activation" ? Visibility.Visible : Visibility.Collapsed;
        LoginPanel.Visibility = panel == "Login" ? Visibility.Visible : Visibility.Collapsed;
        MainPanel.Visibility = panel == "Main" ? Visibility.Visible : Visibility.Collapsed;
        // Settings is an overlay — don't toggle here
        _mainViewModel.NavigateTo(panel);
    }

    private void DetermineStartupScreen()
    {
        var l = App.Localization;
        if (!App.License.IsActivated())
        {
            ActivationMacAddress.Text = _activationViewModel.SerialNumber;
            ShowPanel("Activation");
        }
        else if (!App.Auth.IsPasswordSet())
        {
            LoginTitle.Text = l.Get("login.title_setup");
            LoginSubtitle.Text = l.Get("login.subtitle_setup");
            LoginButton.Content = l.Get("login.button_setup");
            LoginConfirmPassword.Visibility = Visibility.Visible;
            ShowPanel("Login");
        }
        else
        {
            LoginTitle.Text = l.Get("login.title_welcome");
            LoginSubtitle.Text = l.Get("login.subtitle_login");
            LoginButton.Content = l.Get("login.button_login");
            LoginConfirmPassword.Visibility = Visibility.Collapsed;
            ShowPanel("Login");
        }
    }

    private void EnterMainApp()
    {
        // Apply saved theme
        var theme = App.Settings.Get("theme", "Default");
        ApplyTheme(theme);

        PopulateFilterCategories();
        ViewModel.LoadRecords();
        ShowPanel("Main");
    }

    // ============================
    //  Activation
    // ============================

    private void Activate_Click(object sender, RoutedEventArgs e)
    {
        _activationViewModel.LicenseKey = ActivationKeyInput.Text?.Trim() ?? "";

        if (_activationViewModel.TryActivate())
        {
            // Move to login/set-password
            _loginViewModel = new LoginViewModel(App.Auth);
            DetermineStartupScreen();
        }
        else
        {
            ActivationError.Text = _activationViewModel.ErrorMessage;
            ActivationError.Visibility = Visibility.Visible;
        }
    }

    // ============================
    //  Login
    // ============================

    private void Login_Click(object sender, RoutedEventArgs e)
    {
        PerformLogin();
    }

    private void LoginPassword_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
            PerformLogin();
    }

    private void PerformLogin()
    {
        _loginViewModel.Password = LoginPassword.Password;
        _loginViewModel.ConfirmPassword = LoginConfirmPassword.Password;

        if (_loginViewModel.TryLogin())
        {
            LoginPassword.Password = "";
            LoginConfirmPassword.Password = "";
            EnterMainApp();
        }
        else
        {
            LoginError.Text = _loginViewModel.ErrorMessage;
            LoginError.Visibility = Visibility.Visible;
        }
    }

    // ============================
    //  Side Panel
    // ============================

    private void OpenSidePanelDetail(int recordId)
    {
        _detailViewModel.LoadRecord(recordId);
        var record = _detailViewModel.Record;
        if (record is null) return;

        DetailTitle.Text = record.Title;
        DetailCategory.Text = record.Category;
        DetailDate.Text = record.DateDisplay;
        DetailBalanceType.Text = record.BalanceTypeDisplay;
        DetailBalanceTypeBorder.Background = record.IsExpense
            ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 239, 68, 68))
            : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 16, 185, 129));
        DetailAmount.Text = record.AmountDisplay;
        DetailAmount.Foreground = record.Amount switch
        {
            > 0 => new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 16, 185, 129)),
            < 0 => new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 239, 68, 68)),
            _ => new SolidColorBrush(Microsoft.UI.Colors.Gray),
        };

        if (string.IsNullOrWhiteSpace(record.Description))
        {
            DetailDescriptionSection.Visibility = Visibility.Collapsed;
        }
        else
        {
            DetailDescriptionSection.Visibility = Visibility.Visible;
            DetailDescription.Text = record.Description;
        }

        if (_detailViewModel.HasImages)
        {
            DetailImageGallery.Visibility = Visibility.Visible;
            DetailImageGallery.ItemsSource = _detailViewModel.Images;
        }
        else
        {
            DetailImageGallery.Visibility = Visibility.Collapsed;
        }

        SidePanelHeaderText.Text = App.Localization.Get("detail.title");
        ShowSidePanel("Detail");
    }

    private void OpenSidePanelForm()
    {
        SidePanelHeaderText.Text = _formViewModel.HeaderText;

        FormCategory.Items.Clear();
        foreach (var cat in ViewModel.Categories)
            FormCategory.Items.Add(cat);

        // Balance type toggles
        FormIncomeToggle.IsChecked = _formViewModel.BalanceType == 0;
        FormExpenseToggle.IsChecked = _formViewModel.BalanceType == 1;

        FormTitle.Text = _formViewModel.Title;
        FormCategory.Text = _formViewModel.Category;
        FormAmount.Value = _formViewModel.Amount;
        FormDate.Date = _formViewModel.Date;
        FormDescription.Text = _formViewModel.Description;

        UpdateFormImageDisplay();
        ShowSidePanel("Form");
    }

    private void ShowSidePanel(string mode)
    {
        SidePanel.Visibility = Visibility.Visible;
        SidePanelDetail.Visibility = mode == "Detail" ? Visibility.Visible : Visibility.Collapsed;
        SidePanelForm.Visibility = mode == "Form" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CloseSidePanel()
    {
        SidePanel.Visibility = Visibility.Collapsed;
        SidePanelDetail.Visibility = Visibility.Collapsed;
        SidePanelForm.Visibility = Visibility.Collapsed;
        ViewModel.SidePanelMode = "None";
    }

    private void CloseSidePanel_Click(object sender, RoutedEventArgs e) => CloseSidePanel();

    private void SidePanelBackdrop_Click(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => CloseSidePanel();

    // ============================
    //  Header Button Handlers
    // ============================

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyCurrentFilter();
    }

    private void NewRecordButton_Click(object sender, RoutedEventArgs e)
    {
        _formViewModel.ResetForAdd();
        ViewModel.SidePanelMode = "Add";
        RecordListView.SelectedItem = null;
        OpenSidePanelForm();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSettings();
    }

    private void OpenSettings()
    {
        _settingsViewModel.Load();

        // Populate UI
        SettingsCategoryList.ItemsSource = _settingsViewModel.Categories;
        SettingsMacAddress.Text = _settingsViewModel.SerialNumber;
        SettingsLicenseKey.Text = _settingsViewModel.LicenseKey;
        SettingsPasswordMessage.Visibility = Visibility.Collapsed;

        // Theme radio
        var theme = _settingsViewModel.SelectedTheme;
        foreach (var item in SettingsTheme.Items)
        {
            if (item is RadioButton rb && rb.Tag?.ToString() == theme)
            {
                rb.IsChecked = true;
                break;
            }
        }

        // Language selector
        SettingsLanguage.Items.Clear();
        foreach (var lang in LocalizationService.SupportedLanguages)
        {
            var item = new ComboBoxItem
            {
                Content = LocalizationService.GetLanguageDisplayName(lang),
                Tag = lang,
            };
            SettingsLanguage.Items.Add(item);
            if (lang == App.Localization.CurrentLanguage)
                SettingsLanguage.SelectedItem = item;
        }

        // Text customization list
        LoadTextCustomizationList();

        SettingsPanel.Visibility = Visibility.Visible;
    }

    // ============================
    //  Filtering
    // ============================

    private void PopulateFilterCategories()
    {
        FilterCategory.Items.Clear();
        FilterCategory.Items.Add("All Categories");
        foreach (var cat in ViewModel.Categories)
            FilterCategory.Items.Add(cat);
        FilterCategory.SelectedIndex = 0;
    }

    private void FilterText_Changed(object sender, TextChangedEventArgs e)
    {
        if (sender == FilterTitle)
            _filterViewModel.TitleQuery = FilterTitle.Text;
        else if (sender == FilterDescription)
            _filterViewModel.DescriptionQuery = FilterDescription.Text;
    }

    private void FilterCategory_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (FilterCategory.SelectedItem is string selected && selected != "All Categories")
            _filterViewModel.SelectedCategory = selected;
        else
            _filterViewModel.SelectedCategory = null;
    }

    private void FilterAmount_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (sender == FilterAmountMin)
            _filterViewModel.AmountMin = args.NewValue;
        else if (sender == FilterAmountMax)
            _filterViewModel.AmountMax = args.NewValue;
    }

    private void FilterDate_Changed(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (sender == FilterDateStart)
            _filterViewModel.DateStart = args.NewDate;
        else if (sender == FilterDateEnd)
            _filterViewModel.DateEnd = args.NewDate;
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        _filterViewModel.ClearFilters();

        // Reset UI controls
        FilterTitle.Text = "";
        FilterDescription.Text = "";
        FilterCategory.SelectedIndex = 0;
        FilterAmountMin.Value = double.NaN;
        FilterAmountMax.Value = double.NaN;
        FilterDateStart.Date = null;
        FilterDateEnd.Date = null;
    }

    private void OnFilterChanged()
    {
        ApplyCurrentFilter();
        ClearFiltersButton.Visibility = _filterViewModel.HasActiveFilters
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyCurrentFilter()
    {
        var filter = _filterViewModel.BuildFilter();
        if (filter.IsEmpty)
        {
            ViewModel.LoadRecords();
        }
        else
        {
            var results = App.Database.SearchRecords(filter);
            ViewModel.Records.Clear();
            foreach (var r in results)
                ViewModel.Records.Add(r);
            ViewModel.HasRecords = ViewModel.Records.Count > 0;
            ViewModel.StatusText = $"Showing {ViewModel.Records.Count} records (filtered)";
        }
    }

    // ============================
    //  Record List Click → Detail
    // ============================

    private void RecordListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Record clicked)
        {
            ViewModel.SelectedRecord = clicked;
            OpenSidePanelDetail(clicked.Id);
        }
    }

    // ============================
    //  Detail Actions
    // ============================

    private void DetailEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_detailViewModel.Record is null) return;

        _formViewModel.LoadForEdit(_detailViewModel.Record);
        ViewModel.SidePanelMode = "Edit";
        OpenSidePanelForm();
    }

    private async void DetailDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_detailViewModel.Record is null) return;

        var dialog = new ContentDialog
        {
            Title = "Delete Record",
            Content = $"Are you sure you want to delete \"{_detailViewModel.Record.Title}\"?\nThis action cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.Content.XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _detailViewModel.DeleteRecord();
            CloseSidePanel();
            ApplyCurrentFilter();
        }
    }

    // ============================
    //  Save Record
    // ============================

    private void SaveRecord_Click(object sender, RoutedEventArgs e)
    {
        _formViewModel.Title = FormTitle.Text?.Trim() ?? "";
        _formViewModel.Category = FormCategory.Text?.Trim() ?? "";
        _formViewModel.Description = FormDescription.Text?.Trim() ?? "";
        _formViewModel.Amount = double.IsNaN(FormAmount.Value) ? 0 : FormAmount.Value;
        _formViewModel.BalanceType = FormExpenseToggle.IsChecked == true ? 1 : 0;
        _formViewModel.Date = FormDate.Date ?? DateTimeOffset.Now;

        if (!_formViewModel.Validate())
        {
            if (string.IsNullOrWhiteSpace(_formViewModel.Title))
                FormTitle.Focus(FocusState.Programmatic);
            else if (string.IsNullOrWhiteSpace(_formViewModel.Category))
                FormCategory.Focus(FocusState.Programmatic);
            return;
        }

        var savedId = _formViewModel.Save();
        ApplyCurrentFilter();
        PopulateFilterCategories();
        OpenSidePanelDetail(savedId);
    }

    // ============================
    //  Balance Type Toggle
    // ============================

    private void BalanceType_Click(object sender, RoutedEventArgs e)
    {
        if (sender == FormIncomeToggle)
        {
            FormIncomeToggle.IsChecked = true;
            FormExpenseToggle.IsChecked = false;
        }
        else
        {
            FormIncomeToggle.IsChecked = false;
            FormExpenseToggle.IsChecked = true;
        }
    }

    // ============================
    //  Image Handling
    // ============================

    private static readonly string[] _imageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"];

    private async void BrowseImage_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        foreach (var ext in _imageExtensions)
            picker.FileTypeFilter.Add(ext);

        var files = await picker.PickMultipleFilesAsync();
        if (files is null) return;

        foreach (var file in files)
            await AddImageFromFile(file);
    }

    private void FormImageDropZone_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Add image(s)";
    }

    private async void FormImageDropZone_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        var items = await e.DataView.GetStorageItemsAsync();
        var imageFiles = items.OfType<StorageFile>()
            .Where(f => _imageExtensions.Contains(Path.GetExtension(f.Name).ToLowerInvariant()));

        foreach (var file in imageFiles)
            await AddImageFromFile(file);
    }

    private async Task AddImageFromFile(StorageFile file)
    {
        using var stream = await file.OpenStreamForReadAsync();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        var bytes = ms.ToArray();

        _formViewModel.AddImage(bytes);
        UpdateFormImageDisplay();
    }

    private void RemoveImage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is PendingImage img)
        {
            _formViewModel.RemoveImage(img);
            UpdateFormImageDisplay();
        }
    }

    private void UpdateFormImageDisplay()
    {
        var hasImages = _formViewModel.PendingImages.Count > 0;
        FormImageList.ItemsSource = _formViewModel.PendingImages;
        FormImageList.Visibility = hasImages ? Visibility.Visible : Visibility.Collapsed;
        FormImagePlaceholder.Visibility = hasImages ? Visibility.Collapsed : Visibility.Visible;
    }

    // ============================
    //  Settings
    // ============================

    private void SettingsBack_Click(object sender, RoutedEventArgs e)
    {
        CloseSettings();
    }

    private void SettingsBackdrop_Click(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        CloseSettings();
    }

    private void CloseSettings()
    {
        SettingsPanel.Visibility = Visibility.Collapsed;
        PopulateFilterCategories();
        ViewModel.LoadRecords();
    }

    private void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        _settingsViewModel.OldPassword = SettingsOldPassword.Password;
        _settingsViewModel.NewPassword = SettingsNewPassword.Password;
        _settingsViewModel.ConfirmPassword = SettingsConfirmPassword.Password;

        _settingsViewModel.TryChangePassword();

        SettingsPasswordMessage.Text = _settingsViewModel.PasswordMessage;
        SettingsPasswordMessage.Foreground = _settingsViewModel.PasswordMessageIsError
            ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 239, 68, 68))
            : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 16, 185, 129));
        SettingsPasswordMessage.Visibility = Visibility.Visible;

        if (!_settingsViewModel.PasswordMessageIsError)
        {
            SettingsOldPassword.Password = "";
            SettingsNewPassword.Password = "";
            SettingsConfirmPassword.Password = "";
        }
    }

    private void AddCategory_Click(object sender, RoutedEventArgs e)
    {
        _settingsViewModel.NewCategoryName = SettingsNewCategory.Text?.Trim() ?? "";
        if (_settingsViewModel.AddCategory())
        {
            SettingsNewCategory.Text = "";
            SettingsCategoryList.ItemsSource = null;
            SettingsCategoryList.ItemsSource = _settingsViewModel.Categories;
        }
    }

    private async void DeleteCategory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not Category cat) return;

        // Check if any records use this category
        var filter = new RecordFilter { CategoryFilter = cat.Name };
        var affected = App.Database.SearchRecords(filter);

        if (affected.Count > 0)
        {
            // Ask for reassignment
            var otherCats = _settingsViewModel.Categories
                .Where(c => c.Id != cat.Id)
                .Select(c => c.Name)
                .ToList();

            if (otherCats.Count == 0)
            {
                var infoDialog = new ContentDialog
                {
                    Title = "Cannot Delete",
                    Content = $"Cannot delete \"{cat.Name}\" — it has {affected.Count} records and no other categories exist.",
                    CloseButtonText = "OK",
                    XamlRoot = this.Content.XamlRoot,
                };
                await infoDialog.ShowAsync();
                return;
            }

            var combo = new ComboBox { ItemsSource = otherCats, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = $"\"{cat.Name}\" has {affected.Count} record(s). Reassign them to:", TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap });
            panel.Children.Add(combo);

            var dialog = new ContentDialog
            {
                Title = "Delete Category",
                Content = panel,
                PrimaryButtonText = "Delete & Reassign",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.Content.XamlRoot,
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                _settingsViewModel.DeleteCategory(cat.Id, combo.SelectedItem as string);
                SettingsCategoryList.ItemsSource = null;
                SettingsCategoryList.ItemsSource = _settingsViewModel.Categories;
            }
        }
        else
        {
            var dialog = new ContentDialog
            {
                Title = "Delete Category",
                Content = $"Delete \"{cat.Name}\"?",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.Content.XamlRoot,
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                _settingsViewModel.DeleteCategory(cat.Id, null);
                SettingsCategoryList.ItemsSource = null;
                SettingsCategoryList.ItemsSource = _settingsViewModel.Categories;
            }
        }
    }

    private void ThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SettingsTheme.SelectedItem is not RadioButton rb) return;
        var theme = rb.Tag?.ToString() ?? "Default";
        _settingsViewModel.SelectedTheme = theme;
        ApplyTheme(theme);
    }

    private void ApplyTheme(string theme)
    {
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = theme switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }
    }

    private async void DeactivateLicense_Click(object sender, RoutedEventArgs e)
    {
        var l = App.Localization;
        var dialog = new ContentDialog
        {
            Title = l.Get("settings.deactivate_title"),
            Content = l.Get("settings.deactivate_confirm"),
            PrimaryButtonText = l.Get("settings.license_deactivate"),
            CloseButtonText = l.Get("common.cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.Content.XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _settingsViewModel.Deactivate();
            DetermineStartupScreen();
        }
    }

    // ============================
    //  Language & Text Customization
    // ============================

    private void LanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SettingsLanguage.SelectedItem is not ComboBoxItem item) return;
        var lang = item.Tag?.ToString() ?? "en";
        if (lang != App.Localization.CurrentLanguage)
        {
            App.Localization.SwitchLanguage(lang);
            LoadTextCustomizationList();
        }
    }

    private void TextOverride_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb || tb.Tag is not string key) return;
        var currentVal = App.Localization.Get(key);
        if (tb.Text != currentVal)
        {
            App.Localization.SetOverride(key, tb.Text);
        }
    }

    private void ResetTextOverrides_Click(object sender, RoutedEventArgs e)
    {
        App.Localization.ResetOverrides();
        LoadTextCustomizationList();
    }

    private void LoadTextCustomizationList()
    {
        var strings = App.Localization.GetAllStrings();
        var items = strings.Select(kv => new TextEntry { Key = kv.Key, Value = kv.Value })
            .OrderBy(t => t.Key)
            .ToList();
        SettingsTextList.ItemsSource = items;
    }

    // ============================
    //  RefreshAllText (Localization)
    // ============================

    private void RefreshAllText()
    {
        var l = App.Localization;

        // Dashboard header
        DashboardTitle.Text = l.Get("dashboard.title");
        SubtitleText.Text = l.Get("dashboard.subtitle");
        NewRecordButtonText.Text = l.Get("record.new_button");

        // Stats cards
        StatTotalLabel.Text = l.Get("dashboard.stat_total");
        StatIncomeLabel.Text = l.Get("dashboard.stat_income");
        StatExpenseLabel.Text = l.Get("dashboard.stat_expense");
        StatBalanceLabel.Text = l.Get("dashboard.stat_balance");

        // Filter bar
        FilterTitle.PlaceholderText = l.Get("filter.search_title");
        FilterDescription.PlaceholderText = l.Get("filter.search_desc");
        FilterAmountMin.PlaceholderText = l.Get("filter.min_amount");
        FilterAmountMax.PlaceholderText = l.Get("filter.max_amount");
        FilterDateStart.PlaceholderText = l.Get("filter.from_date");
        FilterDateEnd.PlaceholderText = l.Get("filter.to_date");
        ClearFiltersText.Text = l.Get("filter.clear");

        // Table headers
        ColDate.Text = l.Get("table.date");
        ColType.Text = l.Get("balance.type_label");
        ColTitle.Text = l.Get("table.title");
        ColCategory.Text = l.Get("table.category");
        ColAmount.Text = l.Get("table.amount");

        // Empty state
        EmptyTitle.Text = l.Get("empty.title");
        EmptySubtitle.Text = l.Get("empty.subtitle");

        // Detail panel
        DetailAmountLabel.Text = l.Get("detail.amount_label");
        DetailDescLabel.Text = l.Get("detail.desc_label");
        DetailEditText.Text = l.Get("detail.edit");
        DetailDeleteText.Text = l.Get("detail.delete");

        // Form panel
        FormBalanceTypeLabel.Text = l.Get("balance.type_label");
        FormIncomeToggle.Content = l.Get("balance.income");
        FormExpenseToggle.Content = l.Get("balance.expense");
        FormImageDropText.Text = l.Get("form.image_drop");
        FormImageBrowseLink.Content = l.Get("form.image_browse");
        FormTitle.Header = l.Get("form.title_header");
        FormTitle.PlaceholderText = l.Get("form.title_placeholder");
        FormCategory.Header = l.Get("form.category_header");
        FormCategory.PlaceholderText = l.Get("form.category_placeholder");
        FormAmount.Header = l.Get("form.amount_header");
        FormDate.Header = l.Get("form.date_header");
        FormDate.PlaceholderText = l.Get("form.date_placeholder");
        FormDescription.Header = l.Get("form.desc_header");
        FormDescription.PlaceholderText = l.Get("form.desc_placeholder");
        FormSaveButton.Content = l.Get("form.save");
        FormCancelButton.Content = l.Get("form.cancel");

        // Activation panel
        ActivationTitle.Text = l.Get("activation.title");
        ActivationSubtitle.Text = l.Get("activation.subtitle");
        ActivationMacLabel.Text = l.Get("activation.mac_label");
        ActivationKeyInput.Header = l.Get("activation.key_header");
        ActivationKeyInput.PlaceholderText = l.Get("activation.key_placeholder");
        ActivateButton.Content = l.Get("activation.button");

        // Login panel
        LoginPassword.Header = l.Get("login.password_header");
        LoginPassword.PlaceholderText = l.Get("login.password_placeholder");
        LoginConfirmPassword.Header = l.Get("login.confirm_header");
        LoginConfirmPassword.PlaceholderText = l.Get("login.confirm_placeholder");

        // Settings panel
        SettingsTitleText.Text = l.Get("settings.title");
        SettingsSecurityLabel.Text = l.Get("settings.security");
        SettingsOldPassword.Header = l.Get("settings.old_password");
        SettingsOldPassword.PlaceholderText = l.Get("settings.old_password_placeholder");
        SettingsNewPassword.Header = l.Get("settings.new_password");
        SettingsNewPassword.PlaceholderText = l.Get("settings.new_password_placeholder");
        SettingsConfirmPassword.Header = l.Get("settings.confirm_password");
        SettingsConfirmPassword.PlaceholderText = l.Get("settings.confirm_password_placeholder");
        SettingsChangePasswordBtn.Content = l.Get("settings.change_password");
        SettingsCategoriesLabel.Text = l.Get("settings.categories");
        SettingsNewCategory.PlaceholderText = l.Get("settings.new_category_placeholder");
        SettingsAddCategoryBtn.Content = l.Get("settings.add_category");
        SettingsThemeLabel.Text = l.Get("settings.theme");
        ThemeDefault.Content = l.Get("settings.theme_default");
        ThemeLight.Content = l.Get("settings.theme_light");
        ThemeDark.Content = l.Get("settings.theme_dark");
        SettingsLanguageLabel.Text = l.Get("settings.language");
        SettingsTextCustomLabel.Text = l.Get("settings.text_customization");
        SettingsTextResetButton.Content = l.Get("settings.text_reset");
        SettingsLicenseLabel.Text = l.Get("settings.license");
        SettingsMacLabel.Text = l.Get("settings.license_mac");
        SettingsKeyLabel.Text = l.Get("settings.license_key");
        SettingsDeactivateBtn.Content = l.Get("settings.license_deactivate");
    }
}

public class TextEntry
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
