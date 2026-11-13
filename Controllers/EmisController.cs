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
public class EmisController(IEmiRepository emiRepository, ICategoryRepository categoryRepository) : ControllerBase
{
    private readonly IEmiRepository _emiRepository = emiRepository;
    private readonly ICategoryRepository _categoryRepository = categoryRepository;

    private int? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(claim, out var id) && id > 0) return id;
        return null;
    }

    /// <summary>
    /// Get all EMIs/Loans with automatic deduction from expenses.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<EmiDto>>> GetEmis()
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized(new { message = "User is not authenticated." });

        var list = await _emiRepository.GetEmisAsync(userId.Value);
        return Ok(list);
    }

    /// <summary>
    /// Get details and full payment history of a specific EMI.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<EmiDto>> GetEmi(int id)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized(new { message = "User is not authenticated." });

        var emi = await _emiRepository.GetEmiByIdAsync(id, userId.Value);
        if (emi == null) return NotFound(new { message = "EMI record not found." });

        return Ok(emi);
    }

    /// <summary>
    /// Create a new EMI / Loan record. If CategoryId is not set, a new category will be auto-created.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<EmiDto>> CreateEmi([FromBody] CreateEmiDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized(new { message = "User is not authenticated." });

        int targetCategoryId = dto.CategoryId;

        // Auto-create category if user provided a new category name or if CategoryId is <= 0
        if (targetCategoryId <= 0 || !string.IsNullOrWhiteSpace(dto.NewCategoryName))
        {
            var catName = string.IsNullOrWhiteSpace(dto.NewCategoryName)
                ? $"EMI - {dto.Name.Trim()}"
                : dto.NewCategoryName.Trim();

            // Check if category already exists for this user
            var categories = await _categoryRepository.GetCategoriesAsync("expense", userId.Value);
            var existing = categories.FirstOrDefault(c => c.Name.Equals(catName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                targetCategoryId = existing.Id;
            }
            else
            {
                var newCat = new Category
                {
                    Name = catName,
                    Type = "expense",
                    IconName = string.IsNullOrWhiteSpace(dto.NewCategoryIcon) ? "CreditCard" : dto.NewCategoryIcon.Trim(),
                    Color = string.IsNullOrWhiteSpace(dto.NewCategoryColor) ? "#6366f1" : dto.NewCategoryColor.Trim(),
                    IsDefault = false,
                    UserId = userId.Value,
                    CreatedBy = userId.Value,
                    CreatedDate = DateTime.UtcNow,
                    IsDeleted = 0
                };
                var createdCat = await _categoryRepository.CreateCategoryAsync(newCat);
                targetCategoryId = createdCat.Id;
            }
        }

        var emi = new EmiLoan
        {
            Name = dto.Name.Trim(),
            TotalAmount = dto.TotalAmount,
            MonthlyAmount = dto.MonthlyAmount,
            CategoryId = targetCategoryId,
            StartDate = string.IsNullOrWhiteSpace(dto.StartDate) ? DateTime.UtcNow.ToString("yyyy-MM-dd") : dto.StartDate.Trim(),
            DueDay = dto.DueDay > 0 ? dto.DueDay : 5,
            TotalTenorMonths = dto.TotalTenorMonths > 0 ? dto.TotalTenorMonths : 12,
            Notes = dto.Notes?.Trim(),
            UserId = userId.Value,
            CreatedBy = userId.Value,
            CreatedDate = DateTime.UtcNow,
            IsDeleted = 0
        };

        var created = await _emiRepository.CreateEmiAsync(emi);
        var detailed = await _emiRepository.GetEmiByIdAsync(created.Id, userId.Value);

        return CreatedAtAction(nameof(GetEmi), new { id = created.Id }, detailed);
    }

    /// <summary>
    /// Update an existing EMI record.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<EmiDto>> UpdateEmi(int id, [FromBody] UpdateEmiDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized(new { message = "User is not authenticated." });

        var updated = await _emiRepository.UpdateEmiAsync(id, dto, userId.Value);
        if (updated == null)
        {
            return NotFound(new { message = "EMI not found or cannot be edited." });
        }

        var detailed = await _emiRepository.GetEmiByIdAsync(id, userId.Value);
        return Ok(detailed);
    }

    /// <summary>
    /// Soft delete an EMI record.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteEmi(int id)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized(new { message = "User is not authenticated." });

        var success = await _emiRepository.DeleteEmiAsync(id, userId.Value);
        if (!success)
        {
            return NotFound(new { message = "EMI not found or cannot be deleted." });
        }

        return Ok(new { message = "EMI record deleted successfully." });
    }

    /// <summary>
    /// Get full list of all expense payments deducted for this EMI.
    /// </summary>
    [HttpGet("{id}/history")]
    public async Task<ActionResult<IEnumerable<EmiPaymentItemDto>>> GetHistory(int id)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized(new { message = "User is not authenticated." });

        var emi = await _emiRepository.GetEmiByIdAsync(id, userId.Value);
        if (emi == null) return NotFound(new { message = "EMI record not found." });

        var history = await _emiRepository.GetEmiPaymentHistoryAsync(emi.CategoryId, userId.Value);
        return Ok(history);
    }
}

