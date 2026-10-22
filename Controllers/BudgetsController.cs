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
public class BudgetsController(IBudgetRepository budgetRepository) : ControllerBase
{
    private readonly IBudgetRepository _budgetRepository = budgetRepository;

    private int? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(claim, out var id) && id > 0) return id;
        return null;
    }

    [HttpGet("{monthYear}")]
    public async Task<ActionResult<MonthlyBudget>> GetBudget(string monthYear)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized(new { message = "User is not authenticated." });

        var budget = await _budgetRepository.GetBudgetAsync(monthYear, userId.Value);
        return Ok(budget);
    }

    [HttpPost]
    public async Task<ActionResult<MonthlyBudget>> SetBudget([FromBody] SetBudgetDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized(new { message = "User is not authenticated." });

        var budget = await _budgetRepository.SetBudgetAsync(dto.MonthYear, dto.Amount, userId.Value);
        return Ok(budget);
    }

    /// <summary>
    /// Cash checkout: Take remaining balance out as cash expense in current month.
    /// </summary>
    [HttpPost("cash-checkout")]
    public async Task<IActionResult> CashCheckout([FromBody] CashCheckoutDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized(new { message = "User is not authenticated." });

        var result = await _budgetRepository.CashCheckoutAsync(dto.MonthYear, dto.Amount, userId.Value);
        if (!result.Success)
        {
            return BadRequest(new { message = result.Message });
        }

        return Ok(new
        {
            message = result.Message,
            amount = result.Amount,
            transaction = result.Transaction
        });
    }

    /// <summary>
    /// Carry over remaining balance to the next month as opening balance income.
    /// </summary>
    [HttpPost("rollover-balance")]
    public async Task<IActionResult> RolloverBalance([FromBody] RolloverBalanceDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized(new { message = "User is not authenticated." });

        var result = await _budgetRepository.RolloverBalanceAsync(dto.MonthYear, dto.Amount, userId.Value);
        if (!result.Success)
        {
            return BadRequest(new { message = result.Message });
        }

        return Ok(new
        {
            message = result.Message,
            amount = result.Amount,
            nextMonth = result.NextMonth,
            transferOutTx = result.TransferOutTx,
            openingIncomeTx = result.OpeningIncomeTx
        });
    }
}
