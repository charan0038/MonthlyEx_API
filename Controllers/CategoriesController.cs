using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MonthExpenses.Api.DTOs;
using MonthExpenses.Api.Interfaces;
using MonthExpenses.Api.Models;

namespace MonthExpenses.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
[EnableRateLimiting("api-policy")]
public class CategoriesController(ICategoryRepository categoryRepository) : ControllerBase
{
    private readonly ICategoryRepository _categoryRepository = categoryRepository;

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

        var list = await _categoryRepository.GetCategoriesAsync(type, userId.Value);
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
        var exists = await _categoryRepository.CategoryExistsAsync(cleanName, cleanType, userId.Value);
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

        var created = await _categoryRepository.CreateCategoryAsync(category);

        return CreatedAtAction(nameof(GetCategories), new { id = created.Id }, new CategoryDto
        {
            Id = created.Id,
            Name = created.Name,
            Type = created.Type,
            IconName = created.IconName,
            Color = created.Color,
            IsDefault = created.IsDefault,
            UserId = created.UserId,
            CreatedBy = created.CreatedBy,
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

        var success = await _categoryRepository.DeleteCategoryAsync(id, userId.Value);
        if (!success)
        {
            return NotFound(new { message = "Category not found or cannot be deleted." });
        }

        return Ok(new { message = "Category deleted successfully." });
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

        var category = await _categoryRepository.GetCategoryByIdAsync(categoryId, userId.Value);
        if (category == null)
        {
            return NotFound(new { message = "Category not found." });
        }

        var cleanName = dto.Name.Trim();
        var exists = await _categoryRepository.SubcategoryExistsAsync(categoryId, cleanName, userId.Value);
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

        var created = await _categoryRepository.CreateSubcategoryAsync(subcategory);

        return Ok(new SubcategoryDto
        {
            Id = created.Id,
            Name = created.Name,
            CategoryId = created.CategoryId,
            IsDefault = created.IsDefault,
            UserId = created.UserId,
            CreatedBy = created.CreatedBy
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

        var success = await _categoryRepository.DeleteSubcategoryAsync(id, userId.Value);
        if (!success)
        {
            return NotFound(new { message = "Subcategory not found or cannot be deleted." });
        }

        return Ok(new { message = "Subcategory deleted successfully." });
    }
}
