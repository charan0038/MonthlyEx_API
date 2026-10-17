using System.ComponentModel.DataAnnotations;

namespace MonthExpenses.Api.DTOs;

public class CreateTransactionDto
{
    [Required]
    public string Type { get; set; } = "expense"; // "expense" | "income"

    [Required]
    [Range(0.01, 100000000.0)]
    public decimal Amount { get; set; }

    public int? CategoryId { get; set; }

    [MaxLength(50)]
    public string? Category { get; set; }

    [Required]
    [MaxLength(10)]
    public string Date { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");

    [MaxLength(200)]
    public string? Note { get; set; }

    [MaxLength(30)]
    public string PaymentMethod { get; set; } = "UPI";
}

public class UpdateTransactionDto
{
    [Required]
    public string Type { get; set; } = "expense";

    [Required]
    [Range(0.01, 100000000.0)]
    public decimal Amount { get; set; }

    public int? CategoryId { get; set; }

    [MaxLength(50)]
    public string? Category { get; set; }

    [Required]
    [MaxLength(10)]
    public string Date { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");

    [MaxLength(200)]
    public string? Note { get; set; }

    [MaxLength(30)]
    public string PaymentMethod { get; set; } = "UPI";
}

public class SetBudgetDto
{
    [Required]
    [MaxLength(7)]
    public string MonthYear { get; set; } = string.Empty; // "YYYY-MM"

    [Required]
    [Range(0, 100000000.0)]
    public decimal Amount { get; set; }
}

public class CategoryBreakdownDto
{
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public double Percentage { get; set; }
}

public class DailySpendingDto
{
    public int Day { get; set; }
    public decimal Amount { get; set; }
}

public class MonthlySummaryDto
{
    public string MonthYear { get; set; } = string.Empty;
    public decimal TotalIncome { get; set; }
    public decimal TotalExpense { get; set; }
    public decimal NetBalance { get; set; }
    public decimal BudgetLimit { get; set; }
    public decimal BudgetLeft { get; set; }
    public double BudgetUsedPercentage { get; set; }
    public int TotalTransactions { get; set; }
    public string InsightMessage { get; set; } = string.Empty;
    public List<CategoryBreakdownDto> CategoryBreakdown { get; set; } = new();
    public List<DailySpendingDto> DailySpending { get; set; } = new();
}
