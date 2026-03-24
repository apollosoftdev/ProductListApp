using System.Collections.ObjectModel;
using LedgerDesk.Models;
using LedgerDesk.Services;

namespace LedgerDesk.ViewModels;

public class RecordFormViewModel : BaseViewModel
{
    private readonly DatabaseService _db;

    private string _title = string.Empty;
    private string _category = string.Empty;
    private string _description = string.Empty;
    private double _amount;
    private int _balanceType; // 0 = Income, 1 = Expense
    private DateTimeOffset _date = DateTimeOffset.Now;
    private bool _isEditMode;
    private int _editRecordId;
    private string _headerText = "New Record";

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Category
    {
        get => _category;
        set => SetProperty(ref _category, value);
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public double Amount
    {
        get => _amount;
        set => SetProperty(ref _amount, value);
    }

    public int PaymentType
    {
        get => _balanceType;
        set => SetProperty(ref _balanceType, value);
    }

    public DateTimeOffset Date
    {
        get => _date;
        set => SetProperty(ref _date, value);
    }

    public bool IsEditMode
    {
        get => _isEditMode;
        set
        {
            if (SetProperty(ref _isEditMode, value))
                HeaderText = value ? "Edit Record" : "New Record";
        }
    }

    public string HeaderText
    {
        get => _headerText;
        set => SetProperty(ref _headerText, value);
    }

    public ObservableCollection<PendingImage> PendingImages { get; } = [];

    public RecordFormViewModel(DatabaseService db)
    {
        _db = db;
    }

    public void ResetForAdd()
    {
        IsEditMode = false;
        _editRecordId = 0;
        Title = string.Empty;
        Category = string.Empty;
        Description = string.Empty;
        Amount = 0;
        PaymentType = 0; // default Income
        Date = DateTimeOffset.Now;
        PendingImages.Clear();
    }

    public void LoadForEdit(Record record)
    {
        IsEditMode = true;
        _editRecordId = record.Id;
        Title = record.Title;
        Category = record.Category;
        Description = record.Description;
        Amount = (double)Math.Abs(record.Amount); // always show positive in form
        PaymentType = record.PaymentType;
        Date = new DateTimeOffset(record.Date);

        PendingImages.Clear();
        var images = _db.GetImagesForRecord(record.Id);
        foreach (var img in images)
        {
            PendingImages.Add(new PendingImage
            {
                ImageData = img.ImageData,
                ExistingImageId = img.Id,
            });
        }
    }

    public void AddImage(byte[] imageData)
    {
        PendingImages.Add(new PendingImage { ImageData = imageData });
    }

    public void RemoveImage(PendingImage image)
    {
        PendingImages.Remove(image);
    }

    public bool Validate()
    {
        return !string.IsNullOrWhiteSpace(Title) && !string.IsNullOrWhiteSpace(Category);
    }

    public int Save()
    {
        var amount = Math.Abs((decimal)(double.IsNaN(Amount) ? 0 : Amount));
        var category = Category.Trim();

        // Apply sign based on balance type: Expense = negative
        if (PaymentType == 1 && amount > 0)
            amount = -amount;

        var record = new Record
        {
            Title = Title.Trim(),
            Category = category,
            Description = Description?.Trim() ?? string.Empty,
            Amount = amount,
            PaymentType = PaymentType,
            Date = Date.DateTime,
        };

        int recordId;

        if (IsEditMode)
        {
            record.Id = _editRecordId;
            _db.UpdateRecord(record);
            recordId = _editRecordId;

            // Replace all images: delete existing, re-add
            _db.DeleteImagesForRecord(recordId);
        }
        else
        {
            recordId = _db.AddRecord(record);
        }

        // Save images
        for (int i = 0; i < PendingImages.Count; i++)
        {
            _db.AddImage(recordId, PendingImages[i].ImageData, i);
        }

        return recordId;
    }
}

public class PendingImage : BaseViewModel
{
    private byte[] _imageData = [];
    private int _existingImageId;

    public byte[] ImageData
    {
        get => _imageData;
        set => SetProperty(ref _imageData, value);
    }

    public int ExistingImageId
    {
        get => _existingImageId;
        set => SetProperty(ref _existingImageId, value);
    }

    public Microsoft.UI.Xaml.Media.Imaging.BitmapImage? ImageSource
    {
        get
        {
            if (_imageData is not { Length: > 0 }) return null;
            var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
            using var stream = new MemoryStream(_imageData);
            bmp.SetSource(stream.AsRandomAccessStream());
            return bmp;
        }
    }
}
