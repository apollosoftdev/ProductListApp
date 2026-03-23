using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ProductListApp.Models;

public class Product : INotifyPropertyChanged
{
    private int _id;
    private string _name = string.Empty;
    private string _category = string.Empty;
    private decimal _price;
    private int _stock;
    private string _description = string.Empty;
    private byte[]? _imageData;

    public int Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(); }
    }

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public string Category
    {
        get => _category;
        set { _category = value; OnPropertyChanged(); }
    }

    public decimal Price
    {
        get => _price;
        set { _price = value; OnPropertyChanged(); OnPropertyChanged(nameof(PriceDisplay)); }
    }

    public int Stock
    {
        get => _stock;
        set { _stock = value; OnPropertyChanged(); OnPropertyChanged(nameof(StockStatus)); }
    }

    public string Description
    {
        get => _description;
        set { _description = value; OnPropertyChanged(); }
    }

    public byte[]? ImageData
    {
        get => _imageData;
        set { _imageData = value; OnPropertyChanged(); OnPropertyChanged(nameof(ImageSource)); OnPropertyChanged(nameof(HasImage)); }
    }

    public bool HasImage => _imageData is { Length: > 0 };

    public BitmapImage? ImageSource
    {
        get
        {
            if (_imageData is not { Length: > 0 }) return null;
            var bmp = new BitmapImage();
            using var stream = new System.IO.MemoryStream(_imageData);
            bmp.SetSource(stream.AsRandomAccessStream());
            return bmp;
        }
    }

    // Computed display properties
    public string PriceDisplay => $"${Price:N2}";
    public string StockStatus => Stock switch
    {
        <= 0 => "Out of Stock",
        <= 20 => "Low Stock",
        _ => "In Stock"
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
