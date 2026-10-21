using System.ComponentModel.DataAnnotations;

namespace MonthExpenses.Api.Models;

public class MonthlyBudget : BaseBo
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(7)]
    public string MonthYear { get; set; } = string.Empty; // Format: "YYYY-MM" (e.g. "2026-10")

    [Required]
    [Range(0, 100000000.0)]
    public decimal Amount { get; set; } = 0m;

    public int UserId
    {
        get => CreatedBy;
        set => CreatedBy = value;
    }

    public DateTime UpdatedAt
    {
        get => UpdatedDate ?? CreatedDate;
        set => UpdatedDate = value;
    }
}
