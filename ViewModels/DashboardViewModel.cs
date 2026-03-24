using System.Collections.ObjectModel;
using LedgerDesk.Models;
using LedgerDesk.Services;

namespace LedgerDesk.ViewModels;

public class DashboardViewModel : BaseViewModel
{
    private readonly DatabaseService _db;
    public const int PageSize = 50;

    private ObservableCollection<Record> _records = [];
    private Record? _selectedRecord;
    private string _sidePanelMode = "None"; // None, Add, Edit
    private int _totalRecords;
    private string _totalIncome = "$0.00";
    private string _totalExpense = "$0.00";
    private string _balance = "$0.00";
    private string _statusText = "";
    private bool _hasRecords;

    // Pagination
    private int _currentPage;
    private int _totalPages;
    private int _totalFilteredCount;
    private RecordFilter? _currentFilter;

    public ObservableCollection<Record> Records
    {
        get => _records;
        set => SetProperty(ref _records, value);
    }

    public Record? SelectedRecord
    {
        get => _selectedRecord;
        set => SetProperty(ref _selectedRecord, value);
    }

    public string SidePanelMode
    {
        get => _sidePanelMode;
        set
        {
            if (SetProperty(ref _sidePanelMode, value))
                OnPropertyChanged(nameof(IsSidePanelOpen));
        }
    }

    public bool IsSidePanelOpen => SidePanelMode != "None";

    public int TotalRecords
    {
        get => _totalRecords;
        set => SetProperty(ref _totalRecords, value);
    }

    public string TotalIncome
    {
        get => _totalIncome;
        set => SetProperty(ref _totalIncome, value);
    }

    public string TotalExpense
    {
        get => _totalExpense;
        set => SetProperty(ref _totalExpense, value);
    }

    public string Balance
    {
        get => _balance;
        set => SetProperty(ref _balance, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public bool HasRecords
    {
        get => _hasRecords;
        set => SetProperty(ref _hasRecords, value);
    }

    public int CurrentPage
    {
        get => _currentPage;
        set => SetProperty(ref _currentPage, value);
    }

    public int TotalPages
    {
        get => _totalPages;
        set => SetProperty(ref _totalPages, value);
    }

    public bool CanGoBack => CurrentPage > 0;
    public bool CanGoForward => CurrentPage < TotalPages - 1;
    public bool ShowPagination => TotalPages > 1;

    // Commands
    public RelayCommand LoadRecordsCommand { get; }
    public RelayCommand NewRecordCommand { get; }
    public RelayCommand NextPageCommand { get; }
    public RelayCommand PrevPageCommand { get; }
    public RelayCommand FirstPageCommand { get; }
    public RelayCommand LastPageCommand { get; }

    // Categories for ComboBox binding
    private ObservableCollection<string> _categories = [];
    public ObservableCollection<string> Categories
    {
        get => _categories;
        set => SetProperty(ref _categories, value);
    }

    public DashboardViewModel(DatabaseService db)
    {
        _db = db;
        LoadRecordsCommand = new RelayCommand(LoadRecords);
        NewRecordCommand = new RelayCommand(() => SidePanelMode = "Add");
        NextPageCommand = new RelayCommand(() => GoToPage(CurrentPage + 1));
        PrevPageCommand = new RelayCommand(() => GoToPage(CurrentPage - 1));
        FirstPageCommand = new RelayCommand(() => GoToPage(0));
        LastPageCommand = new RelayCommand(() => GoToPage(TotalPages - 1));
    }

    public void LoadRecords()
    {
        _currentFilter = null;
        _currentPage = 0;
        LoadPage();
        UpdateStatsFromAll();
        LoadCategories();
    }

    public void LoadFiltered(RecordFilter filter)
    {
        _currentFilter = filter;
        _currentPage = 0;
        LoadPage();
    }

    private void GoToPage(int page)
    {
        if (page < 0 || page >= TotalPages) return;
        _currentPage = page;
        LoadPage();
    }

    private void LoadPage()
    {
        List<Record> records;

        if (_currentFilter is null || _currentFilter.IsEmpty)
        {
            _totalFilteredCount = _db.GetRecordCount();
            records = _db.GetRecordsPaged(_currentPage, PageSize);
        }
        else
        {
            _totalFilteredCount = _db.SearchRecordCount(_currentFilter);
            records = _db.SearchRecords(_currentFilter, _currentPage, PageSize);
        }

        TotalPages = Math.Max(1, (int)Math.Ceiling((double)_totalFilteredCount / PageSize));
        CurrentPage = _currentPage;

        Records.Clear();
        foreach (var r in records)
            Records.Add(r);

        HasRecords = Records.Count > 0;
        UpdatePaginationState();
    }

    private void UpdatePaginationState()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(ShowPagination));

        var start = _currentPage * PageSize + 1;
        var end = Math.Min(start + Records.Count - 1, _totalFilteredCount);
        StatusText = _totalFilteredCount == 0
            ? "No records"
            : $"Showing {start}-{end} of {_totalFilteredCount} records (Page {CurrentPage + 1}/{TotalPages})";
    }

    private void UpdateStatsFromAll()
    {
        // Stats always reflect ALL records, not just current page
        var allRecords = _db.GetAllRecords();
        TotalRecords = allRecords.Count;

        var income = allRecords.Where(r => r.Amount > 0).Sum(r => r.Amount);
        var expense = allRecords.Where(r => r.Amount < 0).Sum(r => Math.Abs(r.Amount));
        var balance = allRecords.Sum(r => r.Amount);

        TotalIncome = $"${income:N2}";
        TotalExpense = $"${expense:N2}";
        Balance = $"${balance:N2}";
    }

    private void LoadCategories()
    {
        var cats = _db.GetAllCategories();
        Categories.Clear();
        foreach (var c in cats)
            Categories.Add(c.Name);
    }
}
