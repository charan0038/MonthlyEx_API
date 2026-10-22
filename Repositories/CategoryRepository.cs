using Microsoft.EntityFrameworkCore;
using MonthExpenses.Api.Data;
using MonthExpenses.Api.DTOs;
using MonthExpenses.Api.Interfaces;
using MonthExpenses.Api.Models;

namespace MonthExpenses.Api.Repositories;

public class CategoryRepository(ExpenseDbContext context) : ICategoryRepository
{
    private readonly ExpenseDbContext _context = context;

    public async Task<IEnumerable<CategoryDto>> GetCategoriesAsync(string? type, int userId)
    {
        var query = _context.Categories
            .Where(c => c.IsDeleted == 0 && (c.IsDefault || c.UserId == userId || (c.UserId == null && c.CreatedBy == 0)));

        if (!string.IsNullOrWhiteSpace(type) && !type.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var typeLower = type.ToLower();
            query = query.Where(c => c.Type.ToLower() == typeLower);
        }

        return await query
            .OrderByDescending(c => c.IsDefault)
            .ThenBy(c => c.Name)
            .Select(c => new CategoryDto
            {
                Id = c.Id,
                Name = c.Name,
                Type = c.Type.ToLower(),
                IconName = c.IconName,
                Color = c.Color,
                IsDefault = c.IsDefault,
                UserId = c.UserId,
                CreatedBy = c.CreatedBy,
                Subcategories = c.Subcategories
                    .Where(s => s.IsDeleted == 0 && (s.IsDefault || s.UserId == userId || (s.UserId == null && s.CreatedBy == 0)))
                    .OrderByDescending(s => s.IsDefault)
                    .ThenBy(s => s.Name)
                    .Select(s => new SubcategoryDto
                    {
                        Id = s.Id,
                        Name = s.Name,
                        CategoryId = s.CategoryId,
                        IsDefault = s.IsDefault,
                        UserId = s.UserId,
                        CreatedBy = s.CreatedBy
                    })
                    .ToList()
            })
            .ToListAsync();
    }

    public async Task<Category?> GetCategoryByIdAsync(int id, int userId)
    {
        return await _context.Categories.FirstOrDefaultAsync(c =>
            c.Id == id && c.IsDeleted == 0 && (c.IsDefault || c.UserId == userId || (c.UserId == null && c.CreatedBy == 0)));
    }

    public async Task<bool> CategoryExistsAsync(string name, string type, int userId)
    {
        var cleanName = name.Trim();
        var cleanType = type.ToLower();

        return await _context.Categories.AnyAsync(c =>
            c.IsDeleted == 0 &&
            c.Name.ToLower() == cleanName.ToLower() &&
            c.Type.ToLower() == cleanType &&
            (c.IsDefault || c.UserId == userId || (c.UserId == null && c.CreatedBy == 0)));
    }

    public async Task<Category> CreateCategoryAsync(Category category)
    {
        _context.Categories.Add(category);
        await _context.SaveChangesAsync();
        return category;
    }

    public async Task<bool> DeleteCategoryAsync(int id, int userId)
    {
        var category = await _context.Categories.FirstOrDefaultAsync(c =>
            c.Id == id && c.IsDeleted == 0 && !c.IsDefault && (c.UserId == userId || c.CreatedBy == userId));

        if (category == null) return false;

        category.IsDeleted = 1;
        category.UpdatedBy = userId;
        category.UpdatedDate = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<Subcategory?> GetSubcategoryByIdAsync(int id, int userId)
    {
        return await _context.Subcategories.FirstOrDefaultAsync(s =>
            s.Id == id && s.IsDeleted == 0 && (s.IsDefault || s.UserId == userId || (s.UserId == null && s.CreatedBy == 0)));
    }

    public async Task<bool> SubcategoryExistsAsync(int categoryId, string name, int userId)
    {
        var cleanName = name.Trim();
        return await _context.Subcategories.AnyAsync(s =>
            s.CategoryId == categoryId &&
            s.IsDeleted == 0 &&
            s.Name.ToLower() == cleanName.ToLower() &&
            (s.IsDefault || s.UserId == userId || (s.UserId == null && s.CreatedBy == 0)));
    }

    public async Task<Subcategory> CreateSubcategoryAsync(Subcategory subcategory)
    {
        _context.Subcategories.Add(subcategory);
        await _context.SaveChangesAsync();
        return subcategory;
    }

    public async Task<bool> DeleteSubcategoryAsync(int id, int userId)
    {
        var subcategory = await _context.Subcategories.FirstOrDefaultAsync(s =>
            s.Id == id && s.IsDeleted == 0 && !s.IsDefault && (s.UserId == userId || s.CreatedBy == userId));

        if (subcategory == null) return false;

        subcategory.IsDeleted = 1;
        subcategory.UpdatedBy = userId;
        subcategory.UpdatedDate = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }
}
