namespace LedgerDesk.Models;

public class RecordFilter
{
    public string? TitleQuery { get; set; }
    public string? CategoryFilter { get; set; }
    public int? PaymentTypeFilter { get; set; } // null = all, 0 = Income, 1 = Expense
    public string? DescriptionQuery { get; set; }
    public decimal? AmountMin { get; set; }
    public decimal? AmountMax { get; set; }
    public DateTime? DateStart { get; set; }
    public DateTime? DateEnd { get; set; }
    public string? SortBy { get; set; } // "date", "title", "amount", "category"
    public bool SortDescending { get; set; } = true;

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(TitleQuery) &&
        string.IsNullOrWhiteSpace(CategoryFilter) &&
        PaymentTypeFilter is null &&
        string.IsNullOrWhiteSpace(DescriptionQuery) &&
        AmountMin is null &&
        AmountMax is null &&
        DateStart is null &&
        DateEnd is null;
}
