using MonthExpenses.Api.Models;

namespace MonthExpenses.Api.Interfaces;

public class CashCheckoutResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public Transaction? Transaction { get; set; }
}

public class RolloverBalanceResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string NextMonth { get; set; } = string.Empty;
    public Transaction? TransferOutTx { get; set; }
    public Transaction? OpeningIncomeTx { get; set; }
}

public interface IBudgetRepository
{
    Task<MonthlyBudget> GetBudgetAsync(string monthYear, int userId);
    Task<MonthlyBudget> SetBudgetAsync(string monthYear, decimal amount, int userId);
    Task<CashCheckoutResult> CashCheckoutAsync(string monthYear, decimal? amount, int userId);
    Task<RolloverBalanceResult> RolloverBalanceAsync(string monthYear, decimal? amount, int userId);
}
