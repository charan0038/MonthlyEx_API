using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MonthExpenses.Api.Data;
using MonthExpenses.Api.DTOs;
using MonthExpenses.Api.Models;

namespace MonthExpenses.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
[EnableRateLimiting("api-policy")]
public class CategoriesController : ControllerBase
{
    private readonly ExpenseDbContext _context;

    public CategoriesController(ExpenseDbContext context)
    {
        _context = context;
    }

    private int? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(claim, out var id) && id > 0) return id;
        return null;
    }

    /// <summary>
    /// Get all categories (defaults + user custom categories) along with their subcategories.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CategoryDto>>> GetCategories([FromQuery] string? type)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized(new { message = "User is not authenticated." });

        var query = _context.Categories
            .Where(c => c.IsDeleted == 0 && (c.IsDefault || c.UserId == userId.Value || (c.UserId == null && c.CreatedBy == 0)));

        if (!string.IsNullOrWhiteSpace(type) && !type.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var typeLower = type.ToLower();
            query = query.Where(c => c.Type.ToLower() == typeLower);
        }

        var list = await query
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
                    .Where(s => s.IsDeleted == 0 && (s.IsDefault || s.UserId == userId.Value || (s.UserId == null && s.CreatedBy == 0)))
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

        return Ok(list);
    }

    /// <summary>
    /// Create a new custom category dynamically.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CategoryDto>> CreateCategory([FromBody] CreateCategoryDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized(new { message = "User is not authenticated." });

        var cleanName = dto.Name.Trim();
        var cleanType = dto.Type.ToLower();

        // Check for duplicates within defaults and this user's custom categories
        var exists = await _context.Categories.AnyAsync(c =>
            c.IsDeleted == 0 &&
            c.Name.ToLower() == cleanName.ToLower() &&
            c.Type.ToLower() == cleanType &&
            (c.IsDefault || c.UserId == userId.Value || (c.UserId == null && c.CreatedBy == 0)));

        if (exists)
        {
            return BadRequest(new { message = $"Category '{cleanName}' already exists for {cleanType}." });
        }

        var category = new Category
        {
            Name = cleanName,
            Type = cleanType,
            IconName = string.IsNullOrWhiteSpace(dto.IconName) ? "Sparkles" : dto.IconName.Trim(),
            Color = string.IsNullOrWhiteSpace(dto.Color) ? "#6366f1" : dto.Color.Trim(),
            IsDefault = false,
            UserId = userId.Value,
            CreatedBy = userId.Value,
            CreatedDate = DateTime.UtcNow,
            IsDeleted = 0
        };

        _context.Categories.Add(category);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetCategories), new { id = category.Id }, new CategoryDto
        {
            Id = category.Id,
            Name = category.Name,
            Type = category.Type,
            IconName = category.IconName,
            Color = category.Color,
            IsDefault = category.IsDefault,
            UserId = category.UserId,
            CreatedBy = category.CreatedBy,
            Subcategories = new List<SubcategoryDto>()
        });
    }

    /// <summary>
    /// Delete a user-created custom category.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized(new { message = "User is not authenticated." });

        var category = await _context.Categories.FirstOrDefaultAsync(c =>
            c.Id == id && c.IsDeleted == 0 && !c.IsDefault && (c.UserId == userId.Value || c.CreatedBy == userId.Value));

        if (category == null)
        {
            return NotFound(new { message = "Category not found or cannot be deleted." });
        }

        category.IsDeleted = 1;
        category.UpdatedBy = userId.Value;
        category.UpdatedDate = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(new { message = $"Category '{category.Name}' deleted successfully." });
    }

    /// <summary>
    /// Add an optional custom subcategory to a category.
    /// </summary>
    [HttpPost("{categoryId}/subcategories")]
    public async Task<ActionResult<SubcategoryDto>> CreateSubcategory(int categoryId, [FromBody] CreateSubcategoryDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized(new { message = "User is not authenticated." });

        var category = await _context.Categories.FirstOrDefaultAsync(c =>
            c.Id == categoryId && c.IsDeleted == 0 && (c.IsDefault || c.UserId == userId.Value || (c.UserId == null && c.CreatedBy == 0)));

        if (category == null)
        {
            return NotFound(new { message = "Category not found." });
        }

        var cleanName = dto.Name.Trim();

        var exists = await _context.Subcategories.AnyAsync(s =>
            s.CategoryId == categoryId &&
            s.IsDeleted == 0 &&
            s.Name.ToLower() == cleanName.ToLower() &&
            (s.IsDefault || s.UserId == userId.Value || (s.UserId == null && s.CreatedBy == 0)));

        if (exists)
        {
            return BadRequest(new { message = $"Subcategory '{cleanName}' already exists under '{category.Name}'." });
        }

        var subcategory = new Subcategory
        {
            Name = cleanName,
            CategoryId = categoryId,
            IsDefault = false,
            UserId = userId.Value,
            CreatedBy = userId.Value,
            CreatedDate = DateTime.UtcNow,
            IsDeleted = 0
        };

        _context.Subcategories.Add(subcategory);
        await _context.SaveChangesAsync();

        return Ok(new SubcategoryDto
        {
            Id = subcategory.Id,
            Name = subcategory.Name,
            CategoryId = subcategory.CategoryId,
            IsDefault = subcategory.IsDefault,
            UserId = subcategory.UserId,
            CreatedBy = subcategory.CreatedBy
        });
    }

    /// <summary>
    /// Delete a user-created custom subcategory.
    /// </summary>
    [HttpDelete("subcategories/{id}")]
    public async Task<IActionResult> DeleteSubcategory(int id)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized(new { message = "User is not authenticated." });

        var subcategory = await _context.Subcategories.FirstOrDefaultAsync(s =>
            s.Id == id && s.IsDeleted == 0 && !s.IsDefault && (s.UserId == userId.Value || s.CreatedBy == userId.Value));

        if (subcategory == null)
        {
            return NotFound(new { message = "Subcategory not found or cannot be deleted." });
        }

        subcategory.IsDeleted = 1;
        subcategory.UpdatedBy = userId.Value;
        subcategory.UpdatedDate = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(new { message = $"Subcategory '{subcategory.Name}' deleted successfully." });
    }
}
