using MonthExpenses.Api.DTOs;
using MonthExpenses.Api.Models;

namespace MonthExpenses.Api.Interfaces;

public interface ICategoryRepository
{
    Task<IEnumerable<CategoryDto>> GetCategoriesAsync(string? type, int userId);
    Task<Category?> GetCategoryByIdAsync(int id, int userId);
    Task<bool> CategoryExistsAsync(string name, string type, int userId);
    Task<Category> CreateCategoryAsync(Category category);
    Task<bool> DeleteCategoryAsync(int id, int userId);

    Task<Subcategory?> GetSubcategoryByIdAsync(int id, int userId);
    Task<bool> SubcategoryExistsAsync(int categoryId, string name, int userId);
    Task<Subcategory> CreateSubcategoryAsync(Subcategory subcategory);
    Task<bool> DeleteSubcategoryAsync(int id, int userId);
}
