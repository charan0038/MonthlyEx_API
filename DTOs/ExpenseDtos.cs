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

    public int? SubcategoryId { get; set; }

    [MaxLength(50)]
    public string? Subcategory { get; set; }

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

    public int? SubcategoryId { get; set; }

    [MaxLength(50)]
    public string? Subcategory { get; set; }

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

    public bool AddToIncome { get; set; } = true;
}

public class CashCheckoutDto
{
    [Required]
    [MaxLength(7)]
    public string MonthYear { get; set; } = string.Empty;

    public decimal? Amount { get; set; }
}

public class RolloverBalanceDto
{
    [Required]
    [MaxLength(7)]
    public string MonthYear { get; set; } = string.Empty;

    public decimal? Amount { get; set; }
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

public class MonthBreakdownItemDto
{
    public int Month { get; set; }
    public string MonthName { get; set; } = string.Empty;
    public string MonthKey { get; set; } = string.Empty; // "YYYY-MM"
    public decimal TotalIncome { get; set; }
    public decimal TotalExpense { get; set; }
    public decimal NetBalance { get; set; }
    public decimal BudgetLimit { get; set; }
    public int TransactionCount { get; set; }
    public string TopCategory { get; set; } = string.Empty;
}

public class SubcategoryBreakdownDto
{
    public string Subcategory { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public double Percentage { get; set; }
}

public class YearlySummaryDto
{
    public int Year { get; set; }
    public decimal TotalIncome { get; set; }
    public decimal TotalExpense { get; set; }
    public decimal NetSavings { get; set; }
    public double SavingsRate { get; set; }
    public decimal AverageMonthlyExpense { get; set; }
    public decimal AverageMonthlyIncome { get; set; }
    public int TotalTransactions { get; set; }
    public string HighestExpenseMonth { get; set; } = string.Empty;
    public decimal HighestExpenseAmount { get; set; }
    public string LowestExpenseMonth { get; set; } = string.Empty;
    public decimal LowestExpenseAmount { get; set; }
    public string TopExpenseCategory { get; set; } = string.Empty;
    public decimal TopExpenseCategoryAmount { get; set; }
    public string InsightMessage { get; set; } = string.Empty;
    public List<MonthBreakdownItemDto> MonthlyBreakdown { get; set; } = new();
    public List<CategoryBreakdownDto> CategoryBreakdown { get; set; } = new();
    public List<SubcategoryBreakdownDto> SubcategoryBreakdown { get; set; } = new();
}

public class DaySpendingSummaryDto
{
    public string Date { get; set; } = string.Empty; // "YYYY-MM-DD"
    public string DayName { get; set; } = string.Empty; // "Monday", "Tuesday", etc.
    public string DayShort { get; set; } = string.Empty; // "Mon", "Tue", etc.
    public decimal TotalIncome { get; set; }
    public decimal TotalExpense { get; set; }
    public decimal NetBalance { get; set; }
    public int TransactionCount { get; set; }
}

public class WeeklySummaryDto
{
    public int Year { get; set; }
    public int WeekNumber { get; set; }
    public string StartDate { get; set; } = string.Empty; // "YYYY-MM-DD"
    public string EndDate { get; set; } = string.Empty; // "YYYY-MM-DD"
    public string FormattedRange { get; set; } = string.Empty; // "19 Oct - 25 Oct 2026"
    public decimal TotalIncome { get; set; }
    public decimal TotalExpense { get; set; }
    public decimal NetBalance { get; set; }
    public decimal DailyAverage { get; set; }
    public int TransactionCount { get; set; }
    public string HighestDayName { get; set; } = string.Empty;
    public decimal HighestDayAmount { get; set; }
    public string TopCategory { get; set; } = string.Empty;
    public decimal TopCategoryAmount { get; set; }
    public string InsightMessage { get; set; } = string.Empty;
    public decimal? PrevWeekExpense { get; set; }
    public double? WeekOverWeekChangePercent { get; set; }
    public List<CategoryBreakdownDto> CategoryBreakdown { get; set; } = new();
    public List<DaySpendingSummaryDto> DailyBreakdown { get; set; } = new();
}

