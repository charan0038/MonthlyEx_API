namespace MonthExpenses.Api.Models;

public class BaseBo
{
    public int CreatedBy { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public int? UpdatedBy { get; set; }
    public DateTime? UpdatedDate { get; set; }
    public int IsDeleted { get; set; } = 0;
}
