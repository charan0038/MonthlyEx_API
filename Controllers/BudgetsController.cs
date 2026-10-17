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
public class BudgetsController : ControllerBase
{
    private readonly ExpenseDbContext _context;

    public BudgetsController(ExpenseDbContext context)
    {
        _context = context;
    }

    private int GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(claim, out var id)) return id;
        return 1; // Default demo user id
    }

    [HttpGet("{monthYear}")]
    public async Task<ActionResult<MonthlyBudget>> GetBudget(string monthYear)
    {
        var userId = GetCurrentUserId();
        var budget = await _context.Budgets.FirstOrDefaultAsync(b =>
            b.IsDeleted == 0 &&
            b.MonthYear == monthYear && (b.CreatedBy == userId || (userId == 1 && b.CreatedBy == 0)));

        if (budget == null)
        {
            // Default 50,000 if not explicitly saved yet
            return Ok(new MonthlyBudget
            {
                MonthYear = monthYear,
                Amount = 50000m,
                CreatedBy = userId,
                CreatedDate = DateTime.UtcNow,
                IsDeleted = 0
            });
        }
        return Ok(budget);
    }

    [HttpPost]
    public async Task<ActionResult<MonthlyBudget>> SetBudget([FromBody] SetBudgetDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var userId = GetCurrentUserId();
        var budget = await _context.Budgets.FirstOrDefaultAsync(b =>
            b.IsDeleted == 0 &&
            b.MonthYear == dto.MonthYear && (b.CreatedBy == userId || (userId == 1 && b.CreatedBy == 0)));

        if (budget == null)
        {
            budget = new MonthlyBudget
            {
                MonthYear = dto.MonthYear,
                Amount = dto.Amount,
                CreatedBy = userId,
                CreatedDate = DateTime.UtcNow,
                IsDeleted = 0
            };
            _context.Budgets.Add(budget);
        }
        else
        {
            budget.Amount = dto.Amount;
            budget.UpdatedBy = userId;
            budget.UpdatedDate = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        return Ok(budget);
    }
}
