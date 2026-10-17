using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MonthExpenses.Api.Models;

public class Transaction : BaseBo
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [Required]
    public string Type { get; set; } = "expense"; // "expense" | "income"

    [Required]
    [Range(0.01, 100000000.0, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }

    [Required]
    public int CategoryId { get; set; }

    [ForeignKey(nameof(CategoryId))]
    public virtual Category? Category { get; set; }

    [NotMapped]
    public string CategoryName => Category?.Name ?? string.Empty;

    [Required]
    [MaxLength(10)]
    public string Date { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd"); // YYYY-MM-DD

    [MaxLength(200)]
    public string? Note { get; set; }

    [MaxLength(30)]
    public string PaymentMethod { get; set; } = "UPI"; // "UPI" | "Cash" | "Card"

    public int UserId
    {
        get => CreatedBy;
        set => CreatedBy = value;
    }

    public DateTime CreatedAt
    {
        get => CreatedDate;
        set => CreatedDate = value;
    }
}
