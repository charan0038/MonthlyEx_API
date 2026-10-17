using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MonthExpenses.Api.Data;
using MonthExpenses.Api.DTOs;
using MonthExpenses.Api.Models;

namespace MonthExpenses.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("api-policy")]
public class CategoriesController : ControllerBase
{
    private readonly ExpenseDbContext _context;

    public CategoriesController(ExpenseDbContext context)
    {
        _context = context;
    }

    private int GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(claim, out var id)) return id;
        return 1; // Default demo user id
    }

    /// <summary>
    /// Get all categories (defaults + user custom categories).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CategoryDto>>> GetCategories([FromQuery] string? type)
    {
        var userId = GetCurrentUserId();

        var query = _context.Categories
            .Where(c => c.IsDeleted == 0 && (c.IsDefault || c.CreatedBy == userId || (userId == 1 && c.CreatedBy == 0)));

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
                CreatedBy = c.CreatedBy
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
        var cleanName = dto.Name.Trim();
        var cleanType = dto.Type.ToLower();

        // Check for duplicates
        var exists = await _context.Categories.AnyAsync(c =>
            c.IsDeleted == 0 &&
            c.Name.ToLower() == cleanName.ToLower() &&
            c.Type.ToLower() == cleanType &&
            (c.IsDefault || c.CreatedBy == userId));

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
            CreatedBy = userId,
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
            CreatedBy = category.CreatedBy
        });
    }

    /// <summary>
    /// Delete a user-created custom category.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        var userId = GetCurrentUserId();
        var category = await _context.Categories.FirstOrDefaultAsync(c =>
            c.Id == id && c.IsDeleted == 0 && c.CreatedBy == userId);

        if (category == null)
        {
            return NotFound(new { message = "Category not found or cannot be deleted." });
        }

        if (category.IsDefault)
        {
            return BadRequest(new { message = "Default system categories cannot be deleted." });
        }

        category.IsDeleted = 1;
        category.UpdatedBy = userId;
        category.UpdatedDate = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(new { message = $"Category '{category.Name}' deleted successfully." });
    }
}
