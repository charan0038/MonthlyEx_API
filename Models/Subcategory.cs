using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace MonthExpenses.Api.Models;

public class Subcategory : BaseBo
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public int CategoryId { get; set; }

    [JsonIgnore]
    [ForeignKey(nameof(CategoryId))]
    public virtual Category? Category { get; set; }

    public bool IsDefault { get; set; } = false;

    public int? UserId { get; set; }
}
