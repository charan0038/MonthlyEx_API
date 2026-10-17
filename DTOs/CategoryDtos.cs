using System.ComponentModel.DataAnnotations;

namespace MonthExpenses.Api.DTOs;

public class CreateCategoryDto
{
    [Required(ErrorMessage = "Category name is required.")]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(expense|income)$", ErrorMessage = "Type must be 'expense' or 'income'.")]
    public string Type { get; set; } = "expense";

    [MaxLength(50)]
    public string IconName { get; set; } = "Sparkles";

    [MaxLength(20)]
    public string Color { get; set; } = "#6366f1";
}

public class CategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "expense";
    public string IconName { get; set; } = "Sparkles";
    public string Color { get; set; } = "#6366f1";
    public bool IsDefault { get; set; }
    public int CreatedBy { get; set; }
}
