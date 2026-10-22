using System.Globalization;
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
public class TransactionsController(ITransactionRepository transactionRepository) : ControllerBase
{
    private readonly ITransactionRepository _transactionRepository = transactionRepository;

    private int GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(claim, out var id)) return id;
        return 1; // Default demo user id
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Transaction>>> GetTransactions(
        [FromQuery] string? monthYear,
        [FromQuery] string? startDate,
        [FromQuery] string? endDate,
        [FromQuery] string? category,
        [FromQuery] string? subcategory,
        [FromQuery] string? type,
        [FromQuery] string? payment,
        [FromQuery] string? search,
        [FromQuery] string? sortBy = "date-desc")
    {
        var userId = GetCurrentUserId();
        var filter = new TransactionFilterParams
        {
            MonthYear = monthYear,
            StartDate = startDate,
            EndDate = endDate,
            Category = category,
            Subcategory = subcategory,
            Type = type,
            Payment = payment,
            Search = search,
            SortBy = sortBy,
            UserId = userId
        };

        var results = await _transactionRepository.GetTransactionsAsync(filter);
        return Ok(results);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Transaction>> GetTransaction(string id)
    {
        var userId = GetCurrentUserId();
        var transaction = await _transactionRepository.GetTransactionByIdAsync(id, userId);

        if (transaction == null)
        {
            return NotFound(new { message = $"Transaction with ID '{id}' not found." });
        }

        return Ok(transaction);
    }

    [HttpPost]
    public async Task<ActionResult<Transaction>> CreateTransaction([FromBody] CreateTransactionDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var userId = GetCurrentUserId();

        int categoryId = dto.CategoryId ?? 0;
        if (categoryId <= 0 && !string.IsNullOrWhiteSpace(dto.Category))
        {
            var cat = await _transactionRepository.FindCategoryByNameAsync(dto.Category, userId);
            if (cat != null)
            {
                categoryId = cat.Id;
            }
        }

        if (categoryId <= 0)
        {
            return BadRequest(new { message = "Valid CategoryId is required." });
        }

        int? subcategoryId = dto.SubcategoryId > 0 ? dto.SubcategoryId : null;
        string? subcategoryName = dto.Subcategory?.Trim();

        // If subcategory was provided, validate or resolve
        if (subcategoryId.HasValue)
        {
            var subcat = await _transactionRepository.FindSubcategoryByIdAsync(subcategoryId.Value, categoryId, userId);
            if (subcat != null && string.IsNullOrWhiteSpace(subcategoryName))
            {
                subcategoryName = subcat.Name;
            }
        }
        else if (!string.IsNullOrWhiteSpace(subcategoryName))
        {
            var subcat = await _transactionRepository.FindSubcategoryByNameAsync(categoryId, subcategoryName, userId);
            if (subcat != null)
            {
                subcategoryId = subcat.Id;
                subcategoryName = subcat.Name;
            }
        }

        var transaction = new Transaction
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = dto.Type.ToLower(),
            Amount = dto.Amount,
            CategoryId = categoryId,
            SubcategoryId = subcategoryId,
            Subcategory = subcategoryName,
            Date = dto.Date,
            Note = dto.Note,
            PaymentMethod = dto.PaymentMethod,
            CreatedBy = userId,
            CreatedDate = DateTime.UtcNow,
            IsDeleted = 0
        };

        var created = await _transactionRepository.CreateTransactionAsync(transaction);
        return CreatedAtAction(nameof(GetTransaction), new { id = created.Id }, created);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateTransaction(string id, [FromBody] UpdateTransactionDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var userId = GetCurrentUserId();
        var transaction = await _transactionRepository.GetTransactionByIdAsync(id, userId);

        if (transaction == null) return NotFound(new { message = $"Transaction with ID '{id}' not found." });

        int categoryId = dto.CategoryId ?? 0;
        if (categoryId <= 0 && !string.IsNullOrWhiteSpace(dto.Category))
        {
            var cat = await _transactionRepository.FindCategoryByNameAsync(dto.Category, userId);
            if (cat != null)
            {
                categoryId = cat.Id;
            }
        }

        int? subcategoryId = dto.SubcategoryId > 0 ? dto.SubcategoryId : null;
        string? subcategoryName = dto.Subcategory?.Trim();

        if (subcategoryId.HasValue)
        {
            var targetCatId = categoryId > 0 ? categoryId : transaction.CategoryId;
            var subcat = await _transactionRepository.FindSubcategoryByIdAsync(subcategoryId.Value, targetCatId, userId);
            if (subcat != null && string.IsNullOrWhiteSpace(subcategoryName))
            {
                subcategoryName = subcat.Name;
            }
        }
        else if (!string.IsNullOrWhiteSpace(subcategoryName))
        {
            var targetCatId = categoryId > 0 ? categoryId : transaction.CategoryId;
            var subcat = await _transactionRepository.FindSubcategoryByNameAsync(targetCatId, subcategoryName, userId);
            if (subcat != null)
            {
                subcategoryId = subcat.Id;
                subcategoryName = subcat.Name;
            }
        }

        transaction.Type = dto.Type.ToLower();
        transaction.Amount = dto.Amount;
        if (categoryId > 0)
        {
            transaction.CategoryId = categoryId;
        }
        transaction.SubcategoryId = subcategoryId;
        transaction.Subcategory = subcategoryName;
        transaction.Date = dto.Date;
        transaction.Note = dto.Note;
        transaction.PaymentMethod = dto.PaymentMethod;
        transaction.UpdatedBy = userId;
        transaction.UpdatedDate = DateTime.UtcNow;

        var updated = await _transactionRepository.UpdateTransactionAsync(transaction);
        return Ok(updated);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTransaction(string id)
    {
        var userId = GetCurrentUserId();
        var success = await _transactionRepository.DeleteTransactionAsync(id, userId);

        if (!success)
        {
            return NotFound(new { message = $"Transaction with ID '{id}' not found." });
        }

        return Ok(new { message = $"Transaction '{id}' deleted successfully." });
    }

    [HttpGet("summary")]
    public async Task<ActionResult<MonthlySummaryDto>> GetMonthlySummary([FromQuery] string monthYear)
    {
        if (string.IsNullOrWhiteSpace(monthYear))
        {
            monthYear = DateTime.UtcNow.ToString("yyyy-MM");
        }

        var userId = GetCurrentUserId();
        var summary = await _transactionRepository.GetMonthlySummaryAsync(monthYear, userId);
        return Ok(summary);
    }

    [HttpGet("yearly-summary")]
    public async Task<ActionResult<YearlySummaryDto>> GetYearlySummary([FromQuery] int? year)
    {
        int targetYear = year ?? DateTime.UtcNow.Year;
        var userId = GetCurrentUserId();

        var result = await _transactionRepository.GetYearlySummaryAsync(targetYear, userId);
        return Ok(result);
    }

    [HttpGet("weekly-summary")]
    public async Task<ActionResult<WeeklySummaryDto>> GetWeeklySummary([FromQuery] string? date)
    {
        DateTime targetDate;
        if (!string.IsNullOrWhiteSpace(date) && DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            targetDate = parsed.Date;
        }
        else
        {
            targetDate = DateTime.UtcNow.Date;
        }

        var userId = GetCurrentUserId();
        var result = await _transactionRepository.GetWeeklySummaryAsync(targetDate, userId);
        return Ok(result);
    }

    [HttpGet("export")]
    public async Task<IActionResult> ExportCsv([FromQuery] string monthYear)
    {
        if (string.IsNullOrWhiteSpace(monthYear))
        {
            monthYear = DateTime.UtcNow.ToString("yyyy-MM");
        }

        var userId = GetCurrentUserId();
        var bytes = await _transactionRepository.ExportCsvAsync(monthYear, userId);
        return File(bytes, "text/csv", $"Kharcha_{monthYear}.csv");
    }

    [HttpPost("seed")]
    public async Task<IActionResult> SeedDemoData()
    {
        await _transactionRepository.SeedSampleDataAsync();
        return Ok(new { message = "Sample demo transactions and budgets loaded successfully." });
    }

    [HttpDelete("clear")]
    public async Task<IActionResult> ClearMonth([FromQuery] string monthYear)
    {
        if (string.IsNullOrWhiteSpace(monthYear))
        {
            return BadRequest(new { message = "monthYear parameter is required (e.g. '2026-10')." });
        }

        var userId = GetCurrentUserId();
        var count = await _transactionRepository.ClearMonthAsync(monthYear, userId);

        return Ok(new { message = $"Cleared {count} transactions for month {monthYear}." });
    }
}
