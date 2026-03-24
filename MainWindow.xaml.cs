using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ProductListApp.Data;
using ProductListApp.Models;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace ProductListApp;

public sealed partial class MainWindow : Window
{
    private readonly DatabaseHelper _db = new();
    private readonly ObservableCollection<Product> _displayed = new();
    private List<Product> _allProducts = [];
    private byte[]? _pendingImageBytes;

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
        _pendingImageBytes = null;
        DlgImagePreview.Visibility = Visibility.Collapsed;
        DlgImagePlaceholder.Visibility = Visibility.Visible;

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
            ImageData   = _pendingImageBytes,
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

    // ============================
    //  Image Picker / Drag-Drop
    // ============================

    private static readonly string[] _imageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"];

    private async void BrowseImage_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        foreach (var ext in _imageExtensions)
            picker.FileTypeFilter.Add(ext);

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
            await SetPendingImage(file);
    }

    private void DlgImageDropZone_DragOver(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Add image";
    }

    private async void DlgImageDropZone_Drop(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        var items = await e.DataView.GetStorageItemsAsync();
        var file = items.OfType<StorageFile>()
            .FirstOrDefault(f => _imageExtensions.Contains(Path.GetExtension(f.Name).ToLowerInvariant()));

        if (file is not null)
            await SetPendingImage(file);
    }

    private const uint ImageSize = 512;
    private const double CanvasSize = 500;
    private const double HandleSize = 16;
    private const double MinCropSize = 40;

    // Crop state
    private BitmapDecoder? _cropDecoder;
    private IRandomAccessStream? _cropStream;
    private double _imgDisplayX, _imgDisplayY, _imgDisplayW, _imgDisplayH;
    private double _cropX, _cropY, _cropSide;
    private bool _isDraggingCrop;
    private Point _dragStart;
    private double _dragCropStartX, _dragCropStartY;
    private FrameworkElement? _activeHandle;

    private async Task SetPendingImage(StorageFile file)
    {
        // Keep stream alive for crop dialog
        _cropStream?.Dispose();
        _cropStream = await file.OpenReadAsync();
        _cropDecoder = await BitmapDecoder.CreateAsync(_cropStream);

        var w = _cropDecoder.PixelWidth;
        var h = _cropDecoder.PixelHeight;

        // Compute display size (fit within CanvasSize)
        double scale;
        if (w >= h)
        {
            scale = CanvasSize / w;
            _imgDisplayW = CanvasSize;
            _imgDisplayH = h * scale;
        }
        else
        {
            scale = CanvasSize / h;
            _imgDisplayH = CanvasSize;
            _imgDisplayW = w * scale;
        }
        _imgDisplayX = (CanvasSize - _imgDisplayW) / 2;
        _imgDisplayY = (CanvasSize - _imgDisplayH) / 2;

        // Position the image on canvas
        Canvas.SetLeft(CropImage, _imgDisplayX);
        Canvas.SetTop(CropImage, _imgDisplayY);
        CropImage.Width = _imgDisplayW;
        CropImage.Height = _imgDisplayH;

        // Load image for display
        var bmp = new BitmapImage();
        _cropStream.Seek(0);
        bmp.SetSource(_cropStream);
        CropImage.Source = bmp;

        // Default crop: largest centered square within the displayed image
        _cropSide = Math.Min(_imgDisplayW, _imgDisplayH);
        _cropX = _imgDisplayX + (_imgDisplayW - _cropSide) / 2;
        _cropY = _imgDisplayY + (_imgDisplayH - _cropSide) / 2;
        UpdateCropOverlay();

        // Must hide AddProductDialog before showing CropDialog (WinUI only allows one at a time)
        AddProductDialog.Hide();

        CropDialog.XamlRoot = this.Content.XamlRoot;
        var result = await CropDialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            await ApplyCrop();
        }

        // Reopen the Add Product dialog
        await AddProductDialog.ShowAsync();
    }

    private async Task ApplyCrop()
    {
        if (_cropDecoder is null) return;

        // Map display coords back to original image pixels
        double scaleX = _cropDecoder.PixelWidth / _imgDisplayW;
        double scaleY = _cropDecoder.PixelHeight / _imgDisplayH;

        uint origX = (uint)Math.Max(0, Math.Round((_cropX - _imgDisplayX) * scaleX));
        uint origY = (uint)Math.Max(0, Math.Round((_cropY - _imgDisplayY) * scaleY));
        uint origSide = (uint)Math.Round(_cropSide * Math.Min(scaleX, scaleY));

        // Clamp to image bounds
        uint imgW = _cropDecoder.PixelWidth;
        uint imgH = _cropDecoder.PixelHeight;
        if (origX + origSide > imgW) origSide = imgW - origX;
        if (origY + origSide > imgH) origSide = imgH - origY;
        origSide = Math.Max(1, origSide);

        // Crop using BitmapBounds — ScaledWidth/Height must match the full source
        var cropTransform = new BitmapTransform
        {
            ScaledWidth = imgW,
            ScaledHeight = imgH,
            InterpolationMode = BitmapInterpolationMode.Fant,
            Bounds = new BitmapBounds { X = origX, Y = origY, Width = origSide, Height = origSide },
        };

        _cropStream!.Seek(0);
        var decoder2 = await BitmapDecoder.CreateAsync(_cropStream);
        var croppedData = await decoder2.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            cropTransform, ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);

        // Encode cropped image, then decode+resize to exact 512x512
        using var tempStream = new InMemoryRandomAccessStream();
        var cropEncoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, tempStream);
        cropEncoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            origSide, origSide, 96, 96, croppedData.DetachPixelData());
        await cropEncoder.FlushAsync();

        tempStream.Seek(0);
        var resizeDecoder = await BitmapDecoder.CreateAsync(tempStream);
        var resizeTransform = new BitmapTransform
        {
            ScaledWidth = ImageSize,
            ScaledHeight = ImageSize,
            InterpolationMode = BitmapInterpolationMode.Fant,
        };
        var pixelData = await resizeDecoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            resizeTransform, ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);

        using var outStream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, outStream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            ImageSize, ImageSize, 96, 96, pixelData.DetachPixelData());
        await encoder.FlushAsync();

        outStream.Seek(0);
        var bytes = new byte[outStream.Size];
        using (var reader = new DataReader(outStream))
        {
            await reader.LoadAsync((uint)outStream.Size);
            reader.ReadBytes(bytes);
        }
        _pendingImageBytes = bytes;

        var bmpResult = new BitmapImage();
        using var memStream = new MemoryStream(bytes);
        bmpResult.SetSource(memStream.AsRandomAccessStream());
        DlgImagePreview.Source = bmpResult;
        DlgImagePreview.Visibility = Visibility.Visible;
        DlgImagePlaceholder.Visibility = Visibility.Collapsed;

        _cropStream?.Dispose();
        _cropStream = null;
        _cropDecoder = null;
    }

    private void UpdateCropOverlay()
    {
        // Crop rect
        Canvas.SetLeft(CropRect, _cropX);
        Canvas.SetTop(CropRect, _cropY);
        CropRect.Width = _cropSide;
        CropRect.Height = _cropSide;

        // Overlays: top, bottom, left, right around the crop area
        Canvas.SetLeft(OverlayTop, 0); Canvas.SetTop(OverlayTop, 0);
        OverlayTop.Width = CanvasSize; OverlayTop.Height = Math.Max(0, _cropY);

        Canvas.SetLeft(OverlayBottom, 0); Canvas.SetTop(OverlayBottom, _cropY + _cropSide);
        OverlayBottom.Width = CanvasSize; OverlayBottom.Height = Math.Max(0, CanvasSize - (_cropY + _cropSide));

        Canvas.SetLeft(OverlayLeft, 0); Canvas.SetTop(OverlayLeft, _cropY);
        OverlayLeft.Width = Math.Max(0, _cropX); OverlayLeft.Height = _cropSide;

        Canvas.SetLeft(OverlayRight, _cropX + _cropSide); Canvas.SetTop(OverlayRight, _cropY);
        OverlayRight.Width = Math.Max(0, CanvasSize - (_cropX + _cropSide)); OverlayRight.Height = _cropSide;

        // Corner handles
        double hs = HandleSize / 2;
        Canvas.SetLeft(HandleTL, _cropX - hs); Canvas.SetTop(HandleTL, _cropY - hs);
        Canvas.SetLeft(HandleTR, _cropX + _cropSide - hs); Canvas.SetTop(HandleTR, _cropY - hs);
        Canvas.SetLeft(HandleBL, _cropX - hs); Canvas.SetTop(HandleBL, _cropY + _cropSide - hs);
        Canvas.SetLeft(HandleBR, _cropX + _cropSide - hs); Canvas.SetTop(HandleBR, _cropY + _cropSide - hs);
    }

    // --- Drag crop box ---
    private void CropCanvas_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var pos = e.GetCurrentPoint(CropCanvas).Position;
        // Only start drag if inside the crop rect
        if (pos.X >= _cropX && pos.X <= _cropX + _cropSide &&
            pos.Y >= _cropY && pos.Y <= _cropY + _cropSide)
        {
            _isDraggingCrop = true;
            _dragStart = pos;
            _dragCropStartX = _cropX;
            _dragCropStartY = _cropY;
            CropCanvas.CapturePointer(e.Pointer);
            e.Handled = true;
        }
    }

    private void CropCanvas_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_isDraggingCrop) return;
        var pos = e.GetCurrentPoint(CropCanvas).Position;
        var dx = pos.X - _dragStart.X;
        var dy = pos.Y - _dragStart.Y;

        _cropX = Clamp(_dragCropStartX + dx, _imgDisplayX, _imgDisplayX + _imgDisplayW - _cropSide);
        _cropY = Clamp(_dragCropStartY + dy, _imgDisplayY, _imgDisplayY + _imgDisplayH - _cropSide);
        UpdateCropOverlay();
        e.Handled = true;
    }

    private void CropCanvas_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_isDraggingCrop)
        {
            _isDraggingCrop = false;
            CropCanvas.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }
    }

    // --- Resize handles ---
    private void Handle_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _activeHandle = sender as FrameworkElement;
        _dragStart = e.GetCurrentPoint(CropCanvas).Position;
        _dragCropStartX = _cropX;
        _dragCropStartY = _cropY;
        _activeHandle?.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void Handle_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_activeHandle is null) return;
        var pos = e.GetCurrentPoint(CropCanvas).Position;
        var dx = pos.X - _dragStart.X;
        var dy = pos.Y - _dragStart.Y;

        // Use the larger delta to maintain square
        double delta = Math.Abs(dx) > Math.Abs(dy) ? dx : dy;
        double oldSide = _cropSide;

        if (_activeHandle == HandleBR)
        {
            _cropSide = Clamp(oldSide + delta, MinCropSize,
                Math.Min(_imgDisplayX + _imgDisplayW - _cropX, _imgDisplayY + _imgDisplayH - _cropY));
        }
        else if (_activeHandle == HandleTL)
        {
            double newSide = Clamp(oldSide - delta, MinCropSize,
                Math.Min(_cropX - _imgDisplayX + oldSide, _cropY - _imgDisplayY + oldSide));
            double diff = newSide - oldSide;
            _cropX = _dragCropStartX - diff;
            _cropY = _dragCropStartY - diff;
            _cropSide = newSide;
        }
        else if (_activeHandle == HandleTR)
        {
            double newSide = Clamp(oldSide + dx, MinCropSize,
                Math.Min(_imgDisplayX + _imgDisplayW - _cropX, _cropY - _imgDisplayY + oldSide));
            double diff = newSide - oldSide;
            _cropY = _dragCropStartY - diff;
            _cropSide = newSide;
        }
        else if (_activeHandle == HandleBL)
        {
            double newSide = Clamp(oldSide - dx, MinCropSize,
                Math.Min(_cropX - _imgDisplayX + oldSide, _imgDisplayY + _imgDisplayH - _cropY));
            double diff = newSide - oldSide;
            _cropX = _dragCropStartX - diff;
            _cropSide = newSide;
        }

        _dragStart = pos;
        _dragCropStartX = _cropX;
        _dragCropStartY = _cropY;
        UpdateCropOverlay();
        e.Handled = true;
    }

    private void Handle_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_activeHandle is not null)
        {
            _activeHandle.ReleasePointerCapture(e.Pointer);
            _activeHandle = null;
            e.Handled = true;
        }
    }

    private static double Clamp(double value, double min, double max) =>
        Math.Max(min, Math.Min(max, value));
}
