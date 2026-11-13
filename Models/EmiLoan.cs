using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace MonthExpenses.Api.Models;

public class EmiLoan : BaseBo
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Range(1.0, 100000000.0, ErrorMessage = "Total amount must be greater than zero.")]
    public decimal TotalAmount { get; set; }

    [Range(0.0, 100000000.0)]
    public decimal MonthlyAmount { get; set; } = 0m;

    [Required]
    public int CategoryId { get; set; }

    [ForeignKey(nameof(CategoryId))]
    public virtual Category? Category { get; set; }

    [Required]
    [MaxLength(10)]
    public string StartDate { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");

    [Range(1, 31)]
    public int DueDay { get; set; } = 5;

    [Range(1, 600)]
    public int TotalTenorMonths { get; set; } = 12;

    [MaxLength(250)]
    public string? Notes { get; set; }

    public int UserId
    {
        get => CreatedBy;
        set => CreatedBy = value;
    }
}

