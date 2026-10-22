using MonthExpenses.Api.DTOs;
using MonthExpenses.Api.Models;

namespace MonthExpenses.Api.Interfaces;

public class TransactionFilterParams
{
    public string? MonthYear { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public string? Category { get; set; }
    public string? Subcategory { get; set; }
    public string? Type { get; set; }
    public string? Payment { get; set; }
    public string? Search { get; set; }
    public string? SortBy { get; set; } = "date-desc";
    public int UserId { get; set; }
}

public interface ITransactionRepository
{
    Task<IEnumerable<Transaction>> GetTransactionsAsync(TransactionFilterParams filter);
    Task<Transaction?> GetTransactionByIdAsync(string id, int userId);
    Task<Category?> FindCategoryByNameAsync(string categoryName, int userId);
    Task<Subcategory?> FindSubcategoryByIdAsync(int subcategoryId, int categoryId, int userId);
    Task<Subcategory?> FindSubcategoryByNameAsync(int categoryId, string subcategoryName, int userId);
    Task<Transaction> CreateTransactionAsync(Transaction transaction);
    Task<Transaction> UpdateTransactionAsync(Transaction transaction);
    Task<bool> DeleteTransactionAsync(string id, int userId);

    Task<MonthlySummaryDto> GetMonthlySummaryAsync(string monthYear, int userId);
    Task<YearlySummaryDto> GetYearlySummaryAsync(int year, int userId);
    Task<WeeklySummaryDto> GetWeeklySummaryAsync(DateTime targetDate, int userId);

    Task<byte[]> ExportCsvAsync(string monthYear, int userId);
    Task SeedSampleDataAsync();
    Task<int> ClearMonthAsync(string monthYear, int userId);
}
