using LedgerDesk.Models;
using Microsoft.UI.Xaml;

namespace LedgerDesk.ViewModels;

public class FilterViewModel : BaseViewModel
{
    private string _titleQuery = string.Empty;
    private string? _selectedCategory;
    private int? _selectedPaymentType; // null = all, 0 = Income, 1 = Expense
    private string _descriptionQuery = string.Empty;
    private double _amountMin = double.NaN;
    private double _amountMax = double.NaN;
    private DateTimeOffset? _dateStart;
    private DateTimeOffset? _dateEnd;
    private bool _hasActiveFilters;
    private string _sortBy = "date";
    private bool _sortDescending = true;

    private readonly DispatcherTimer _debounceTimer;

    public event Action? FilterChanged;

    public string TitleQuery
    {
        get => _titleQuery;
        set { if (SetProperty(ref _titleQuery, value)) ScheduleFilter(); }
    }

    public string? SelectedCategory
    {
        get => _selectedCategory;
        set { if (SetProperty(ref _selectedCategory, value)) ApplyFilter(); }
    }

    public int? SelectedPaymentType
    {
        get => _selectedPaymentType;
        set { if (SetProperty(ref _selectedPaymentType, value)) ApplyFilter(); }
    }

    public string DescriptionQuery
    {
        get => _descriptionQuery;
        set { if (SetProperty(ref _descriptionQuery, value)) ScheduleFilter(); }
    }

    public double AmountMin
    {
        get => _amountMin;
        set { if (SetProperty(ref _amountMin, value)) ScheduleFilter(); }
    }

    public double AmountMax
    {
        get => _amountMax;
        set { if (SetProperty(ref _amountMax, value)) ScheduleFilter(); }
    }

    public DateTimeOffset? DateStart
    {
        get => _dateStart;
        set { if (SetProperty(ref _dateStart, value)) ApplyFilter(); }
    }

    public DateTimeOffset? DateEnd
    {
        get => _dateEnd;
        set { if (SetProperty(ref _dateEnd, value)) ApplyFilter(); }
    }

    public string SortBy
    {
        get => _sortBy;
        set { if (SetProperty(ref _sortBy, value)) ApplyFilter(); }
    }

    public bool SortDescending
    {
        get => _sortDescending;
        set { if (SetProperty(ref _sortDescending, value)) ApplyFilter(); }
    }

    public bool HasActiveFilters
    {
        get => _hasActiveFilters;
        private set => SetProperty(ref _hasActiveFilters, value);
    }

    public RelayCommand ClearFiltersCommand { get; }

    public FilterViewModel()
    {
        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            ApplyFilter();
        };

        ClearFiltersCommand = new RelayCommand(ClearFilters);
    }

    private void ScheduleFilter()
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void ApplyFilter()
    {
        HasActiveFilters = !BuildFilter().IsEmpty;
        FilterChanged?.Invoke();
    }

    public RecordFilter BuildFilter()
    {
        return new RecordFilter
        {
            TitleQuery = string.IsNullOrWhiteSpace(TitleQuery) ? null : TitleQuery.Trim(),
            CategoryFilter = string.IsNullOrWhiteSpace(SelectedCategory) ? null : SelectedCategory,
            PaymentTypeFilter = SelectedPaymentType,
            DescriptionQuery = string.IsNullOrWhiteSpace(DescriptionQuery) ? null : DescriptionQuery.Trim(),
            AmountMin = double.IsNaN(AmountMin) ? null : (decimal)AmountMin,
            AmountMax = double.IsNaN(AmountMax) ? null : (decimal)AmountMax,
            DateStart = DateStart?.DateTime,
            DateEnd = DateEnd?.DateTime,
            SortBy = SortBy,
            SortDescending = SortDescending,
        };
    }

    public void ClearFilters()
    {
        _debounceTimer.Stop();
        _titleQuery = string.Empty;
        _selectedCategory = null;
        _selectedPaymentType = null;
        _descriptionQuery = string.Empty;
        _amountMin = double.NaN;
        _amountMax = double.NaN;
        _dateStart = null;
        _dateEnd = null;

        OnPropertyChanged(nameof(TitleQuery));
        OnPropertyChanged(nameof(SelectedCategory));
        OnPropertyChanged(nameof(SelectedPaymentType));
        OnPropertyChanged(nameof(DescriptionQuery));
        OnPropertyChanged(nameof(AmountMin));
        OnPropertyChanged(nameof(AmountMax));
        OnPropertyChanged(nameof(DateStart));
        OnPropertyChanged(nameof(DateEnd));

        HasActiveFilters = false;
        FilterChanged?.Invoke();
    }
}
