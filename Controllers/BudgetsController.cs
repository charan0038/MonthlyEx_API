using System.Globalization;
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
public class BudgetsController : ControllerBase
{
    private readonly ExpenseDbContext _context;

    public BudgetsController(ExpenseDbContext context)
    {
        _context = context;
    }

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

        var budget = await _context.Budgets.FirstOrDefaultAsync(b =>
            b.IsDeleted == 0 &&
            b.MonthYear == monthYear &&
            b.CreatedBy == userId.Value);

        if (budget == null)
        {
            // Default 10,000 (10k) for this user if not explicitly saved yet
            return Ok(new MonthlyBudget
            {
                MonthYear = monthYear,
                Amount = 10000m,
                CreatedBy = userId.Value,
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
        if (userId == null) return Unauthorized(new { message = "User is not authenticated." });

        var budget = await _context.Budgets.FirstOrDefaultAsync(b =>
            b.IsDeleted == 0 &&
            b.MonthYear == dto.MonthYear &&
            b.CreatedBy == userId.Value);

        if (budget == null)
        {
            budget = new MonthlyBudget
            {
                MonthYear = dto.MonthYear,
                Amount = dto.Amount,
                UserId = userId.Value,
                CreatedBy = userId.Value,
                CreatedDate = DateTime.UtcNow,
                IsDeleted = 0
            };
            _context.Budgets.Add(budget);
        }
        else
        {
            budget.Amount = dto.Amount;
            budget.UserId = userId.Value;
            budget.UpdatedBy = userId.Value;
            budget.UpdatedDate = DateTime.UtcNow;
        }

        // Setting a budget always allocates it to Total Income & Net Balance for that month
        if (dto.Amount > 0)
        {
            var budgetIncomeTx = await _context.Transactions.FirstOrDefaultAsync(t =>
                t.IsDeleted == 0 &&
                t.CreatedBy == userId.Value &&
                t.Type == "income" &&
                t.Date.StartsWith(dto.MonthYear) &&
                (t.Note == "Monthly Budget" || t.Note == "Monthly Budget Allocation" || t.Note == "Opening Budget"));

            if (budgetIncomeTx != null)
            {
                budgetIncomeTx.Amount = dto.Amount;
                budgetIncomeTx.UpdatedBy = userId.Value;
                budgetIncomeTx.UpdatedDate = DateTime.UtcNow;
            }
            else
            {
                var incomeCat = await _context.Categories.FirstOrDefaultAsync(c =>
                    c.IsDeleted == 0 &&
                    c.Type == "income" &&
                    (c.IsDefault || c.UserId == userId.Value || c.CreatedBy == userId.Value)) ??
                    await _context.Categories.FirstOrDefaultAsync(c => c.Type == "income");

                int catId = incomeCat?.Id ?? 9;

                var newIncomeTx = new Transaction
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Type = "income",
                    Amount = dto.Amount,
                    CategoryId = catId,
                    Date = $"{dto.MonthYear}-01",
                    Note = "Monthly Budget",
                    PaymentMethod = "UPI",
                    CreatedBy = userId.Value,
                    CreatedDate = DateTime.UtcNow,
                    IsDeleted = 0
                };
                _context.Transactions.Add(newIncomeTx);
            }
        }

        await _context.SaveChangesAsync();
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

        var monthTxs = await _context.Transactions
            .Where(t => t.IsDeleted == 0 && t.Date.StartsWith(dto.MonthYear) && (t.CreatedBy == userId.Value || (userId == 1 && t.CreatedBy == 0)))
            .ToListAsync();

        var totalIncome = monthTxs.Where(t => t.Type == "income").Sum(t => t.Amount);
        var totalExpense = monthTxs.Where(t => t.Type == "expense").Sum(t => t.Amount);
        var netBalance = totalIncome - totalExpense;

        decimal amountToCheckout = dto.Amount ?? netBalance;
        if (amountToCheckout <= 0)
        {
            return BadRequest(new { message = "No remaining balance available for cash checkout." });
        }

        var expenseCat = await _context.Categories.FirstOrDefaultAsync(c =>
            c.IsDeleted == 0 &&
            c.Type == "expense" &&
            c.Name.ToLower() == "other" &&
            (c.IsDefault || c.CreatedBy == userId.Value)) ??
            await _context.Categories.FirstOrDefaultAsync(c => c.Type == "expense");

        int catId = expenseCat?.Id ?? 1;

        var tx = new Transaction
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = "expense",
            Amount = amountToCheckout,
            CategoryId = catId,
            Date = DateTime.UtcNow.ToString("yyyy-MM-dd").StartsWith(dto.MonthYear)
                ? DateTime.UtcNow.ToString("yyyy-MM-dd")
                : $"{dto.MonthYear}-28",
            Note = "Cash Checkout",
            PaymentMethod = "Cash",
            CreatedBy = userId.Value,
            CreatedDate = DateTime.UtcNow,
            IsDeleted = 0
        };

        _context.Transactions.Add(tx);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = $"Successfully checked out {amountToCheckout:C} as cash.",
            amount = amountToCheckout,
            transaction = tx
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

        if (!DateTime.TryParseExact(dto.MonthYear + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var currentMonthDate))
        {
            return BadRequest(new { message = "Invalid month format. Expected YYYY-MM." });
        }

        var nextMonthDate = currentMonthDate.AddMonths(1);
        var nextMonthKey = nextMonthDate.ToString("yyyy-MM");

        var monthTxs = await _context.Transactions
            .Where(t => t.IsDeleted == 0 && t.Date.StartsWith(dto.MonthYear) && (t.CreatedBy == userId.Value || (userId == 1 && t.CreatedBy == 0)))
            .ToListAsync();

        var totalIncome = monthTxs.Where(t => t.Type == "income").Sum(t => t.Amount);
        var totalExpense = monthTxs.Where(t => t.Type == "expense").Sum(t => t.Amount);
        var netBalance = totalIncome - totalExpense;

        decimal amountToRollover = dto.Amount ?? netBalance;
        if (amountToRollover <= 0)
        {
            return BadRequest(new { message = "No remaining balance available to carry forward." });
        }

        // 1. Current month: Transfer out expense
        var otherCat = await _context.Categories.FirstOrDefaultAsync(c =>
            c.IsDeleted == 0 &&
            c.Type == "expense" &&
            c.Name.ToLower() == "other" &&
            (c.IsDefault || c.CreatedBy == userId.Value)) ??
            await _context.Categories.FirstOrDefaultAsync(c => c.Type == "expense");

        int daysInMonth = DateTime.DaysInMonth(currentMonthDate.Year, currentMonthDate.Month);
        var transferOutTx = new Transaction
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = "expense",
            Amount = amountToRollover,
            CategoryId = otherCat?.Id ?? 1,
            Date = $"{dto.MonthYear}-{daysInMonth:D2}",
            Note = $"Transferred to {nextMonthKey} Opening Balance",
            PaymentMethod = "Cash",
            CreatedBy = userId.Value,
            CreatedDate = DateTime.UtcNow,
            IsDeleted = 0
        };
        _context.Transactions.Add(transferOutTx);

        // 2. Next month: Opening Balance Income
        var incomeCat = await _context.Categories.FirstOrDefaultAsync(c =>
            c.IsDeleted == 0 &&
            c.Type == "income" &&
            (c.IsDefault || c.CreatedBy == userId.Value)) ??
            await _context.Categories.FirstOrDefaultAsync(c => c.Type == "income");

        var openingIncomeTx = new Transaction
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = "income",
            Amount = amountToRollover,
            CategoryId = incomeCat?.Id ?? 1,
            Date = $"{nextMonthKey}-01",
            Note = $"Opening Balance from {dto.MonthYear}",
            PaymentMethod = "Cash",
            CreatedBy = userId.Value,
            CreatedDate = DateTime.UtcNow,
            IsDeleted = 0
        };
        _context.Transactions.Add(openingIncomeTx);

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = $"Successfully transferred ₹{amountToRollover:N0} to {nextMonthKey} as opening balance income.",
            amount = amountToRollover,
            nextMonth = nextMonthKey,
            transferOutTx,
            openingIncomeTx
        });
    }
}
