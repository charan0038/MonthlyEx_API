using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MonthExpenses.Api.Models;

public class Category : BaseBo
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string Type { get; set; } = "expense"; // "expense" | "income"

    [MaxLength(50)]
    public string IconName { get; set; } = "Sparkles";

    [MaxLength(20)]
    public string Color { get; set; } = "#6366f1";

    public bool IsDefault { get; set; } = false;
}
