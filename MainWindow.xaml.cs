using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ProductListApp.Data;
using ProductListApp.Models;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace ProductListApp;

public sealed partial class MainWindow : Window
{
    private readonly DatabaseHelper _db = new();
    private readonly ObservableCollection<Product> _displayed = new();
    private List<Product> _allProducts = [];

    public MainWindow()
    {
        this.InitializeComponent();

        // --- Window setup ---
        SetWindowSize(1200, 780);
        SetupTitleBar();
        TrySetMicaBackdrop();

        ProductListView.ItemsSource = _displayed;
        LoadProducts();
    }

    // ============================
    //  Window / Title Bar / Mica
    // ============================

    private void SetWindowSize(int width, int height)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
    }

    private void SetupTitleBar()
    {
        // Extend content into the title bar area for a seamless look
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
    }

    private void TrySetMicaBackdrop()
    {
        if (MicaController.IsSupported())
        {
            this.SystemBackdrop = new MicaBackdrop
            {
                Kind = MicaKind.Base
            };
        }
        else if (DesktopAcrylicController.IsSupported())
        {
            this.SystemBackdrop = new DesktopAcrylicBackdrop();
        }
    }

    // ============================
    //  Data Loading
    // ============================

    private void LoadProducts()
    {
        _allProducts = _db.GetAllProducts();
        PopulateCategoryFilter();
        ApplyFilters();
        UpdateStats();
    }

    private void PopulateCategoryFilter()
    {
        var current = CategoryFilter.SelectedItem as ComboBoxItem;
        var currentText = current?.Content?.ToString();

        CategoryFilter.Items.Clear();
        CategoryFilter.Items.Add(new ComboBoxItem { Content = "All Categories" });

        var categories = _allProducts.Select(p => p.Category).Distinct().OrderBy(c => c);
        foreach (var cat in categories)
            CategoryFilter.Items.Add(new ComboBoxItem { Content = cat });

        // Restore selection
        if (currentText is not null)
        {
            foreach (ComboBoxItem item in CategoryFilter.Items)
            {
                if (item.Content?.ToString() == currentText)
                {
                    CategoryFilter.SelectedItem = item;
                    return;
                }
            }
        }
        CategoryFilter.SelectedIndex = 0;
    }

    private void ApplyFilters()
    {
        var query = SearchBox.Text?.Trim() ?? "";
        var catItem = CategoryFilter.SelectedItem as ComboBoxItem;
        var category = catItem?.Content?.ToString();
        var filterCategory = category is not null && category != "All Categories";

        var filtered = _allProducts.Where(p =>
        {
            if (filterCategory && !p.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrEmpty(query))
            {
                return p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                       p.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                       p.Description.Contains(query, StringComparison.OrdinalIgnoreCase);
            }
            return true;
        }).ToList();

        _displayed.Clear();
        foreach (var p in filtered) _displayed.Add(p);

        EmptyState.Visibility = _displayed.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = $"Showing {_displayed.Count} of {_allProducts.Count} products";
    }

    private void UpdateStats()
    {
        StatTotal.Text = _allProducts.Count.ToString();
        StatCategories.Text = _allProducts.Select(p => p.Category).Distinct().Count().ToString();

        var totalValue = _allProducts.Sum(p => p.Price * p.Stock);
        StatValue.Text = $"${totalValue:N0}";

        var lowStock = _allProducts.Count(p => p.Stock <= 20);
        StatLowStock.Text = lowStock.ToString();

        SubtitleText.Text = $"Manage your product inventory · {_allProducts.Count} items";
    }

    // ============================
    //  Event Handlers
    // ============================

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            ApplyFilters();
    }

    private void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_allProducts.Count > 0) ApplyFilters();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => LoadProducts();

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        DlgName.Text = "";
        DlgCategory.Text = "";
        DlgPrice.Value = 0;
        DlgStock.Value = 0;
        DlgDesc.Text = "";

        AddProductDialog.XamlRoot = this.Content.XamlRoot;
        await AddProductDialog.ShowAsync();
    }

    private void AddDialog_PrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(DlgName.Text) || string.IsNullOrWhiteSpace(DlgCategory.Text))
        {
            args.Cancel = true;
            return;
        }

        _db.AddProduct(new Product
        {
            Name        = DlgName.Text.Trim(),
            Category    = DlgCategory.Text.Trim(),
            Price       = (decimal)(double.IsNaN(DlgPrice.Value) ? 0 : DlgPrice.Value),
            Stock       = (int)(double.IsNaN(DlgStock.Value) ? 0 : DlgStock.Value),
            Description = DlgDesc.Text?.Trim() ?? "",
        });
        LoadProducts();
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (ProductListView.SelectedItem is not Product selected) return;

        var dialog = new ContentDialog
        {
            Title = "Delete Product",
            Content = $"Are you sure you want to delete \"{selected.Name}\"?\nThis action cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.Content.XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _db.DeleteProduct(selected.Id);
            LoadProducts();
        }
    }
}
