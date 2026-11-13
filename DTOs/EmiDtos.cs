using System.ComponentModel.DataAnnotations;

namespace MonthExpenses.Api.DTOs;

public class CreateEmiDto
{
    [Required(ErrorMessage = "EMI / Loan name is required.")]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Range(1.0, 100000000.0, ErrorMessage = "Total amount must be greater than zero.")]
    public decimal TotalAmount { get; set; }

    [Range(0.0, 100000000.0)]
    public decimal MonthlyAmount { get; set; } = 0m;

    /// <summary>
    /// Existing Category ID, or 0 if auto-creating a new category.
    /// </summary>
    public int CategoryId { get; set; }

    /// <summary>
    /// Optional: If CategoryId is 0 or user wants a new category, this category name will be created.
    /// </summary>
    [MaxLength(50)]
    public string? NewCategoryName { get; set; }

    [MaxLength(50)]
    public string? NewCategoryIcon { get; set; }

    [MaxLength(20)]
    public string? NewCategoryColor { get; set; }

    [MaxLength(10)]
    public string StartDate { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");

    [Range(1, 31)]
    public int DueDay { get; set; } = 5;

    [Range(1, 600)]
    public int TotalTenorMonths { get; set; } = 12;

    [MaxLength(250)]
    public string? Notes { get; set; }
}

public class UpdateEmiDto
{
    [Required(ErrorMessage = "EMI / Loan name is required.")]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Range(1.0, 100000000.0, ErrorMessage = "Total amount must be greater than zero.")]
    public decimal TotalAmount { get; set; }

    [Range(0.0, 100000000.0)]
    public decimal MonthlyAmount { get; set; } = 0m;

    public int CategoryId { get; set; }

    [MaxLength(10)]
    public string StartDate { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");

    [Range(1, 31)]
    public int DueDay { get; set; } = 5;

    [Range(1, 600)]
    public int TotalTenorMonths { get; set; } = 12;

    [MaxLength(250)]
    public string? Notes { get; set; }
}

public class EmiPaymentItemDto
{
    public string TransactionId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Date { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string PaymentMethod { get; set; } = "UPI";
}

public class EmiDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal MonthlyAmount { get; set; }
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string CategoryIcon { get; set; } = "CreditCard";
    public string CategoryColor { get; set; } = "#6366f1";
    public string StartDate { get; set; } = string.Empty;
    public int DueDay { get; set; }
    public int TotalTenorMonths { get; set; }
    public string? Notes { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal RemainingAmount { get; set; }
    public double ProgressPercentage { get; set; }
    public int PaymentsCount { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime CreatedDate { get; set; }
    public List<EmiPaymentItemDto> RecentPayments { get; set; } = new();
}

