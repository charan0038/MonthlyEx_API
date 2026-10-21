using System.Globalization;
using System.Security.Claims;
using System.Text;
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
public class TransactionsController : ControllerBase
{
    private readonly ExpenseDbContext _context;

    public TransactionsController(ExpenseDbContext context)
    {
        _context = context;
    }

    private int GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(claim, out var id)) return id;
        return 1; // Default demo user id
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Transaction>>> GetTransactions(
        [FromQuery] string? monthYear,
        [FromQuery] string? category,
        [FromQuery] string? type,
        [FromQuery] string? payment,
        [FromQuery] string? search,
        [FromQuery] string? sortBy = "date-desc")
    {
        var userId = GetCurrentUserId();
        var query = _context.Transactions
            .Include(t => t.Category)
            .Include(t => t.SubcategoryEntity)
            .Where(t => t.IsDeleted == 0 && (t.CreatedBy == userId || (userId == 1 && t.CreatedBy == 0)));

        if (!string.IsNullOrWhiteSpace(monthYear))
        {
            query = query.Where(t => t.Date.StartsWith(monthYear));
        }

        if (!string.IsNullOrWhiteSpace(category) && !category.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(category, out var catId))
            {
                query = query.Where(t => t.CategoryId == catId);
            }
            else
            {
                query = query.Where(t => t.Category != null && t.Category.Name.ToLower() == category.ToLower());
            }
        }

        if (!string.IsNullOrWhiteSpace(type) && !type.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(t => t.Type.ToLower() == type.ToLower());
        }

        if (!string.IsNullOrWhiteSpace(payment) && !payment.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(t => t.PaymentMethod.ToLower() == payment.ToLower());
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLower();
            query = query.Where(t => (t.Note != null && t.Note.ToLower().Contains(searchLower)) ||
                                     (t.Category != null && t.Category.Name.ToLower().Contains(searchLower)) ||
                                     (t.Subcategory != null && t.Subcategory.ToLower().Contains(searchLower)));
        }

        query = sortBy switch
        {
            "date-asc" => query.OrderBy(t => t.Date).ThenBy(t => t.CreatedDate),
            "amount-desc" => query.OrderByDescending(t => t.Amount),
            "amount-asc" => query.OrderBy(t => t.Amount),
            _ => query.OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedDate)
        };

        var results = await query.ToListAsync();
        return Ok(results);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Transaction>> GetTransaction(string id)
    {
        var userId = GetCurrentUserId();
        var transaction = await _context.Transactions
            .Include(t => t.Category)
            .Include(t => t.SubcategoryEntity)
            .FirstOrDefaultAsync(t =>
                t.Id == id && t.IsDeleted == 0 && (t.CreatedBy == userId || (userId == 1 && t.CreatedBy == 0)));

        if (transaction == null) return NotFound(new { message = $"Transaction with ID '{id}' not found." });
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
            var cat = await _context.Categories.FirstOrDefaultAsync(c =>
                c.Name.ToLower() == dto.Category.Trim().ToLower() &&
                c.IsDeleted == 0 &&
                (c.IsDefault || c.UserId == userId || c.CreatedBy == userId || (userId == 1 && c.CreatedBy == 0)));
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
            var subcat = await _context.Subcategories.FirstOrDefaultAsync(s =>
                s.Id == subcategoryId.Value &&
                s.CategoryId == categoryId &&
                s.IsDeleted == 0 &&
                (s.IsDefault || s.UserId == userId || s.CreatedBy == userId || (userId == 1 && s.CreatedBy == 0)));
            if (subcat != null && string.IsNullOrWhiteSpace(subcategoryName))
            {
                subcategoryName = subcat.Name;
            }
        }
        else if (!string.IsNullOrWhiteSpace(subcategoryName))
        {
            var subcat = await _context.Subcategories.FirstOrDefaultAsync(s =>
                s.CategoryId == categoryId &&
                s.Name.ToLower() == subcategoryName.ToLower() &&
                s.IsDeleted == 0 &&
                (s.IsDefault || s.UserId == userId || s.CreatedBy == userId || (userId == 1 && s.CreatedBy == 0)));
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
            Note = dto.Note?.Trim(),
            PaymentMethod = dto.PaymentMethod,
            CreatedBy = userId,
            CreatedDate = DateTime.UtcNow,
            IsDeleted = 0
        };

        _context.Transactions.Add(transaction);
        await _context.SaveChangesAsync();

        await _context.Entry(transaction).Reference(t => t.Category).LoadAsync();
        if (transaction.SubcategoryId.HasValue)
        {
            await _context.Entry(transaction).Reference(t => t.SubcategoryEntity).LoadAsync();
        }

        return CreatedAtAction(nameof(GetTransaction), new { id = transaction.Id }, transaction);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<Transaction>> UpdateTransaction(string id, [FromBody] UpdateTransactionDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var userId = GetCurrentUserId();
        var transaction = await _context.Transactions
            .Include(t => t.Category)
            .Include(t => t.SubcategoryEntity)
            .FirstOrDefaultAsync(t =>
                t.Id == id && t.IsDeleted == 0 && (t.CreatedBy == userId || (userId == 1 && t.CreatedBy == 0)));

        if (transaction == null) return NotFound(new { message = $"Transaction with ID '{id}' not found." });

        int categoryId = dto.CategoryId ?? 0;
        if (categoryId <= 0 && !string.IsNullOrWhiteSpace(dto.Category))
        {
            var cat = await _context.Categories.FirstOrDefaultAsync(c =>
                c.Name.ToLower() == dto.Category.Trim().ToLower() &&
                c.IsDeleted == 0 &&
                (c.IsDefault || c.UserId == userId || c.CreatedBy == userId || (userId == 1 && c.CreatedBy == 0)));
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
            var subcat = await _context.Subcategories.FirstOrDefaultAsync(s =>
                s.Id == subcategoryId.Value &&
                s.CategoryId == targetCatId &&
                s.IsDeleted == 0 &&
                (s.IsDefault || s.UserId == userId || s.CreatedBy == userId || (userId == 1 && s.CreatedBy == 0)));
            if (subcat != null && string.IsNullOrWhiteSpace(subcategoryName))
            {
                subcategoryName = subcat.Name;
            }
        }
        else if (!string.IsNullOrWhiteSpace(subcategoryName))
        {
            var targetCatId = categoryId > 0 ? categoryId : transaction.CategoryId;
            var subcat = await _context.Subcategories.FirstOrDefaultAsync(s =>
                s.CategoryId == targetCatId &&
                s.Name.ToLower() == subcategoryName.ToLower() &&
                s.IsDeleted == 0 &&
                (s.IsDefault || s.UserId == userId || s.CreatedBy == userId || (userId == 1 && s.CreatedBy == 0)));
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
        transaction.Note = dto.Note?.Trim();
        transaction.PaymentMethod = dto.PaymentMethod;
        transaction.UpdatedBy = userId;
        transaction.UpdatedDate = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        await _context.Entry(transaction).Reference(t => t.Category).LoadAsync();
        if (transaction.SubcategoryId.HasValue)
        {
            await _context.Entry(transaction).Reference(t => t.SubcategoryEntity).LoadAsync();
        }
        return Ok(transaction);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTransaction(string id)
    {
        var userId = GetCurrentUserId();
        var transaction = await _context.Transactions.FirstOrDefaultAsync(t =>
            t.Id == id && t.IsDeleted == 0 && (t.CreatedBy == userId || (userId == 1 && t.CreatedBy == 0)));

        if (transaction == null) return NotFound(new { message = $"Transaction with ID '{id}' not found." });

        // Soft delete using BaseBo.IsDeleted
        transaction.IsDeleted = 1;
        transaction.UpdatedBy = userId;
        transaction.UpdatedDate = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(new { message = "Transaction deleted successfully." });
    }

    [HttpGet("summary")]
    public async Task<ActionResult<MonthlySummaryDto>> GetMonthlySummary([FromQuery] string monthYear)
    {
        if (string.IsNullOrWhiteSpace(monthYear))
        {
            monthYear = DateTime.UtcNow.ToString("yyyy-MM");
        }

        var userId = GetCurrentUserId();
        var currentTxs = await _context.Transactions
            .Include(t => t.Category)
            .Where(t => t.IsDeleted == 0 && t.Date.StartsWith(monthYear) && (t.CreatedBy == userId || (userId == 1 && t.CreatedBy == 0)))
            .ToListAsync();

        var budgetRecord = await _context.Budgets.FirstOrDefaultAsync(b =>
            b.IsDeleted == 0 && b.MonthYear == monthYear && (b.CreatedBy == userId || (userId == 1 && b.CreatedBy == 0)));
        var budgetLimit = budgetRecord?.Amount ?? 10000m;

        // Ensure monthly budget allocation is always synchronized into Total Income and Net Balance
        if (userId > 0 && budgetLimit > 0)
        {
            var budgetIncomeTx = currentTxs.FirstOrDefault(t =>
                t.Type == "income" &&
                (t.Note == "Monthly Budget" || t.Note == "Monthly Budget Allocation" || t.Note == "Opening Budget"));

            if (budgetIncomeTx == null)
            {
                var incomeCat = await _context.Categories.FirstOrDefaultAsync(c =>
                    c.IsDeleted == 0 &&
                    c.Type == "income" &&
                    (c.IsDefault || c.UserId == userId || c.CreatedBy == userId)) ??
                    await _context.Categories.FirstOrDefaultAsync(c => c.Type == "income");

                int catId = incomeCat?.Id ?? 9;

                var newIncomeTx = new Transaction
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Type = "income",
                    Amount = budgetLimit,
                    CategoryId = catId,
                    Date = $"{monthYear}-01",
                    Note = "Monthly Budget",
                    PaymentMethod = "UPI",
                    CreatedBy = userId,
                    CreatedDate = DateTime.UtcNow,
                    IsDeleted = 0,
                    Category = incomeCat
                };

                _context.Transactions.Add(newIncomeTx);
                await _context.SaveChangesAsync();
                currentTxs.Add(newIncomeTx);
            }
            else if (budgetRecord != null && budgetIncomeTx.Amount != budgetRecord.Amount)
            {
                budgetIncomeTx.Amount = budgetRecord.Amount;
                budgetIncomeTx.UpdatedBy = userId;
                budgetIncomeTx.UpdatedDate = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }

        var totalIncome = currentTxs.Where(t => t.Type == "income").Sum(t => t.Amount);
        var totalExpense = currentTxs.Where(t => t.Type == "expense").Sum(t => t.Amount);
        var netBalance = totalIncome - totalExpense;
        var budgetLeft = budgetLimit - totalExpense;
        var budgetPercent = budgetLimit > 0 ? (double)(totalExpense / budgetLimit) * 100.0 : 0.0;

        // Category breakdown for expenses
        var expenseTxs = currentTxs.Where(t => t.Type == "expense").ToList();
        var categoryGroups = expenseTxs
            .GroupBy(t => t.Category != null ? t.Category.Name : "Other")
            .Select(g => new CategoryBreakdownDto
            {
                Category = g.Key,
                Amount = g.Sum(x => x.Amount),
                Percentage = totalExpense > 0 ? (double)(g.Sum(x => x.Amount) / totalExpense) * 100.0 : 0.0
            })
            .OrderByDescending(c => c.Amount)
            .ToList();

        // Daily spending trend
        int year = int.Parse(monthYear.Substring(0, 4));
        int month = int.Parse(monthYear.Substring(5, 2));
        int daysInMonth = DateTime.DaysInMonth(year, month);

        var dailyDict = Enumerable.Range(1, daysInMonth).ToDictionary(d => d, _ => 0m);
        foreach (var t in expenseTxs)
        {
            if (DateTime.TryParse(t.Date, out var parsedDate))
            {
                if (dailyDict.ContainsKey(parsedDate.Day))
                {
                    dailyDict[parsedDate.Day] += t.Amount;
                }
            }
        }

        var dailySpending = dailyDict.Select(kv => new DailySpendingDto { Day = kv.Key, Amount = kv.Value }).ToList();

        // Previous month comparison for insight message
        var prevDate = new DateTime(year, month, 1).AddMonths(-1);
        var prevMonthKey = prevDate.ToString("yyyy-MM");
        var prevExpenses = await _context.Transactions
            .Include(t => t.Category)
            .Where(t => t.IsDeleted == 0 && t.Date.StartsWith(prevMonthKey) && t.Type == "expense" && (t.CreatedBy == userId || (userId == 1 && t.CreatedBy == 0)))
            .ToListAsync();

        var prevTotalExpense = prevExpenses.Sum(t => t.Amount);

        var insightBuilder = new StringBuilder();
        if (categoryGroups.Any())
        {
            var topCategory = categoryGroups.First();
            var prevTopCatSpend = prevExpenses.Where(t => (t.Category != null ? t.Category.Name : string.Empty) == topCategory.Category).Sum(t => t.Amount);

            if (prevTopCatSpend > 0)
            {
                var diff = Math.Round(((topCategory.Amount - prevTopCatSpend) / prevTopCatSpend) * 100);
                if (diff > 0)
                    insightBuilder.Append($"You spent {diff}% more on {topCategory.Category} than last month. ");
                else if (diff < 0)
                    insightBuilder.Append($"Great job! You spent {Math.Abs(diff)}% less on {topCategory.Category} than last month. ");
            }
            else
            {
                insightBuilder.Append($"{topCategory.Category} is your largest expense ({Math.Round(topCategory.Percentage)}% of total). ");
            }

            if (prevTotalExpense > 0)
            {
                var overallDiff = Math.Round(((totalExpense - prevTotalExpense) / prevTotalExpense) * 100);
                if (overallDiff > 10)
                    insightBuilder.Append($"Overall outflow is running {overallDiff}% higher than last month. ");
                else if (overallDiff < -10)
                    insightBuilder.Append($"Overall expenses are {Math.Abs(overallDiff)}% lower than last month. ");
            }

            var today = DateTime.UtcNow;
            int activeDays = (today.Year == year && today.Month == month) ? Math.Max(today.Day, 1) : daysInMonth;
            var dailyAverage = Math.Round(totalExpense / activeDays);
            insightBuilder.Append($"Averaging ₹{dailyAverage:N0}/day across {activeDays} days.");
        }
        else
        {
            insightBuilder.Append("No expenses recorded yet for this month. Start logging transactions to unlock insights!");
        }

        var summary = new MonthlySummaryDto
        {
            MonthYear = monthYear,
            TotalIncome = totalIncome,
            TotalExpense = totalExpense,
            NetBalance = netBalance,
            BudgetLimit = budgetLimit,
            BudgetLeft = budgetLeft,
            BudgetUsedPercentage = budgetPercent,
            TotalTransactions = currentTxs.Count,
            InsightMessage = insightBuilder.ToString(),
            CategoryBreakdown = categoryGroups,
            DailySpending = dailySpending
        };

        return Ok(summary);
    }

    [HttpGet("yearly-summary")]
    public async Task<ActionResult<YearlySummaryDto>> GetYearlySummary([FromQuery] int? year)
    {
        var targetYear = year ?? DateTime.UtcNow.Year;
        var yearPrefix = targetYear.ToString();

        var userId = GetCurrentUserId();
        var yearTxs = await _context.Transactions
            .Include(t => t.Category)
            .Where(t => t.IsDeleted == 0 && t.Date.StartsWith(yearPrefix) && (t.CreatedBy == userId || (userId == 1 && t.CreatedBy == 0)))
            .ToListAsync();

        var totalIncome = yearTxs.Where(t => t.Type == "income").Sum(t => t.Amount);
        var totalExpense = yearTxs.Where(t => t.Type == "expense").Sum(t => t.Amount);
        var netSavings = totalIncome - totalExpense;
        var savingsRate = totalIncome > 0 ? (double)(netSavings / totalIncome) * 100.0 : 0.0;

        var monthNames = new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

        var monthlyBreakdown = new List<MonthBreakdownItemDto>();
        for (int m = 1; m <= 12; m++)
        {
            var mPrefix = $"{targetYear}-{m:D2}";
            var mTxs = yearTxs.Where(t => t.Date.StartsWith(mPrefix)).ToList();
            var mIncome = mTxs.Where(t => t.Type == "income").Sum(t => t.Amount);
            var mExpense = mTxs.Where(t => t.Type == "expense").Sum(t => t.Amount);

            var topCat = mTxs.Where(t => t.Type == "expense")
                .GroupBy(t => t.Category?.Name ?? "Other")
                .OrderByDescending(g => g.Sum(x => x.Amount))
                .Select(g => g.Key)
                .FirstOrDefault() ?? "None";

            monthlyBreakdown.Add(new MonthBreakdownItemDto
            {
                Month = m,
                MonthName = monthNames[m - 1],
                MonthKey = mPrefix,
                TotalIncome = mIncome,
                TotalExpense = mExpense,
                NetBalance = mIncome - mExpense,
                TransactionCount = mTxs.Count,
                TopCategory = topCat
            });
        }

        var monthsWithExpenses = monthlyBreakdown.Where(mb => mb.TotalExpense > 0).ToList();
        var highestExpenseItem = monthsWithExpenses.OrderByDescending(mb => mb.TotalExpense).FirstOrDefault();
        var lowestExpenseItem = monthsWithExpenses.OrderBy(mb => mb.TotalExpense).FirstOrDefault();

        var categoryGroups = yearTxs
            .Where(t => t.Type == "expense")
            .GroupBy(t => t.Category?.Name ?? "Other")
            .Select(g => new CategoryBreakdownDto
            {
                Category = g.Key,
                Amount = g.Sum(t => t.Amount),
                Percentage = totalExpense > 0 ? Math.Round((double)(g.Sum(t => t.Amount) / totalExpense) * 100.0, 1) : 0
            })
            .OrderByDescending(c => c.Amount)
            .ToList();

        var subcategoryGroups = yearTxs
            .Where(t => t.Type == "expense" && !string.IsNullOrWhiteSpace(t.Subcategory))
            .GroupBy(t => new { Subcat = t.Subcategory!, Cat = t.Category?.Name ?? "Other" })
            .Select(g => new SubcategoryBreakdownDto
            {
                Subcategory = g.Key.Subcat,
                Category = g.Key.Cat,
                Amount = g.Sum(t => t.Amount),
                Percentage = totalExpense > 0 ? Math.Round((double)(g.Sum(t => t.Amount) / totalExpense) * 100.0, 1) : 0
            })
            .OrderByDescending(s => s.Amount)
            .Take(10)
            .ToList();

        var topExpenseCat = categoryGroups.FirstOrDefault();

        var insight = new StringBuilder();
        if (savingsRate >= 30)
        {
            insight.Append($"Outstanding financial health in {targetYear}! You are saving {savingsRate:F1}% of your annual income.");
        }
        else if (savingsRate > 0)
        {
            insight.Append($"Positive savings in {targetYear}. You retained {savingsRate:F1}% of your annual earnings.");
        }
        else if (totalIncome > 0 && netSavings < 0)
        {
            insight.Append($"Annual expenses exceeded income in {targetYear} by ₹{Math.Abs(netSavings):N0}.");
        }
        else
        {
            insight.Append($"Annual expense overview for {targetYear}.");
        }

        if (topExpenseCat != null)
        {
            insight.Append($" Your primary expense driver was {topExpenseCat.Category} (₹{topExpenseCat.Amount:N0}, {topExpenseCat.Percentage}%).");
        }

        var result = new YearlySummaryDto
        {
            Year = targetYear,
            TotalIncome = totalIncome,
            TotalExpense = totalExpense,
            NetSavings = netSavings,
            SavingsRate = Math.Round(savingsRate, 1),
            AverageMonthlyExpense = Math.Round(totalExpense / 12m, 2),
            AverageMonthlyIncome = Math.Round(totalIncome / 12m, 2),
            TotalTransactions = yearTxs.Count,
            HighestExpenseMonth = highestExpenseItem?.MonthName ?? "None",
            HighestExpenseAmount = highestExpenseItem?.TotalExpense ?? 0m,
            LowestExpenseMonth = lowestExpenseItem?.MonthName ?? "None",
            LowestExpenseAmount = lowestExpenseItem?.TotalExpense ?? 0m,
            TopExpenseCategory = topExpenseCat?.Category ?? "None",
            TopExpenseCategoryAmount = topExpenseCat?.Amount ?? 0m,
            InsightMessage = insight.ToString(),
            MonthlyBreakdown = monthlyBreakdown,
            CategoryBreakdown = categoryGroups,
            SubcategoryBreakdown = subcategoryGroups
        };

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
        var txs = await _context.Transactions
            .Include(t => t.Category)
            .Where(t => t.IsDeleted == 0 && t.Date.StartsWith(monthYear) && (t.CreatedBy == userId || (userId == 1 && t.CreatedBy == 0)))
            .OrderByDescending(t => t.Date)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("ID,Type,Date,Category,CategoryId,Amount,PaymentMethod,Note");

        foreach (var t in txs)
        {
            var catName = t.Category?.Name ?? "Other";
            var note = (t.Note ?? string.Empty).Replace("\"", "\"\"");
            sb.AppendLine($"{t.Id},{t.Type},{t.Date},{catName},{t.CategoryId},{t.Amount},{t.PaymentMethod},\"{note}\"");
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", $"Kharcha_{monthYear}.csv");
    }

    [HttpPost("seed")]
    public async Task<IActionResult> SeedDemoData()
    {
        await _context.SeedSampleDataAsync();
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
        var txs = await _context.Transactions
            .Where(t => t.IsDeleted == 0 && t.Date.StartsWith(monthYear) && (t.CreatedBy == userId || (userId == 1 && t.CreatedBy == 0)))
            .ToListAsync();

        // Soft delete all
        foreach (var t in txs)
        {
            t.IsDeleted = 1;
            t.UpdatedBy = userId;
            t.UpdatedDate = DateTime.UtcNow;
        }
        await _context.SaveChangesAsync();

        return Ok(new { message = $"Cleared {txs.Count} transactions for month {monthYear}." });
    }
}
