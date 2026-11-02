using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MonthExpenses.Api.Data;
using MonthExpenses.Api.DTOs;
using MonthExpenses.Api.Interfaces;
using MonthExpenses.Api.Models;

namespace MonthExpenses.Api.Repositories;

public class TransactionRepository(ExpenseDbContext context) : ITransactionRepository
{
    private readonly ExpenseDbContext _context = context;

    public async Task<IEnumerable<Transaction>> GetTransactionsAsync(TransactionFilterParams filter)
    {
        var query = _context.Transactions
            .Include(t => t.Category)
            .Include(t => t.SubcategoryEntity)
            .Where(t => t.IsDeleted == 0 && (t.CreatedBy == filter.UserId || (filter.UserId == 1 && t.CreatedBy == 0)));

        if (!string.IsNullOrWhiteSpace(filter.MonthYear))
        {
            var cleanMonthYear = filter.MonthYear.Trim();
            query = query.Where(t => t.Date.StartsWith(cleanMonthYear));
        }

        if (!string.IsNullOrWhiteSpace(filter.StartDate))
        {
            var cleanStart = filter.StartDate.Trim();
            query = query.Where(t => string.Compare(t.Date, cleanStart) >= 0);
        }

        if (!string.IsNullOrWhiteSpace(filter.EndDate))
        {
            var cleanEnd = filter.EndDate.Trim();
            query = query.Where(t => string.Compare(t.Date, cleanEnd) <= 0);
        }

        if (!string.IsNullOrWhiteSpace(filter.Category) && !filter.Category.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var cleanCat = filter.Category.Trim();
            if (int.TryParse(cleanCat, out var catId))
            {
                query = query.Where(t => t.CategoryId == catId);
            }
            else
            {
                var lowerCat = cleanCat.ToLower();
                // Find all category IDs matching this name (handles dynamic custom categories + defaults)
                var matchingCatIds = await _context.Categories
                    .Where(c => c.Name.ToLower() == lowerCat && c.IsDeleted == 0)
                    .Select(c => c.Id)
                    .ToListAsync();

                query = query.Where(t => (t.Category != null && t.Category.Name.ToLower() == lowerCat) ||
                                         matchingCatIds.Contains(t.CategoryId));
            }
        }

        if (!string.IsNullOrWhiteSpace(filter.Subcategory) && !filter.Subcategory.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var cleanSub = filter.Subcategory.Trim();
            if (int.TryParse(cleanSub, out var subcatId))
            {
                query = query.Where(t => t.SubcategoryId == subcatId);
            }
            else
            {
                var lowerSub = cleanSub.ToLower();
                query = query.Where(t => (t.Subcategory != null && t.Subcategory.ToLower() == lowerSub) ||
                                         (t.SubcategoryEntity != null && t.SubcategoryEntity.Name.ToLower() == lowerSub));
            }
        }

        if (!string.IsNullOrWhiteSpace(filter.Type) && !filter.Type.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var lowerType = filter.Type.Trim().ToLower();
            query = query.Where(t => t.Type.ToLower() == lowerType);
        }

        if (!string.IsNullOrWhiteSpace(filter.Payment) && !filter.Payment.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var lowerPayment = filter.Payment.Trim().ToLower();
            query = query.Where(t => t.PaymentMethod.ToLower() == lowerPayment);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var lowerSearch = filter.Search.Trim().ToLower();
            query = query.Where(t => (t.Note != null && t.Note.ToLower().Contains(lowerSearch)) ||
                                     (t.Category != null && t.Category.Name.ToLower().Contains(lowerSearch)) ||
                                     (t.Subcategory != null && t.Subcategory.ToLower().Contains(lowerSearch)) ||
                                     (t.PaymentMethod != null && t.PaymentMethod.ToLower().Contains(lowerSearch)));
        }

        query = (filter.SortBy?.Trim().ToLower()) switch
        {
            "date-asc" => query.OrderBy(t => t.Date).ThenBy(t => t.CreatedDate),
            "amount-desc" => query.OrderByDescending(t => t.Amount),
            "amount-asc" => query.OrderBy(t => t.Amount),
            _ => query.OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedDate)
        };

        return await query.ToListAsync();
    }

    public async Task<Transaction?> GetTransactionByIdAsync(string id, int userId)
    {
        return await _context.Transactions
            .Include(t => t.Category)
            .Include(t => t.SubcategoryEntity)
            .FirstOrDefaultAsync(t => t.Id == id && t.IsDeleted == 0 && (t.CreatedBy == userId || (userId == 1 && t.CreatedBy == 0)));
    }

    public async Task<Category?> FindCategoryByNameAsync(string categoryName, int userId)
    {
        var clean = categoryName.Trim().ToLower();
        return await _context.Categories.FirstOrDefaultAsync(c =>
            c.Name.ToLower() == clean &&
            c.IsDeleted == 0 &&
            (c.IsDefault || c.UserId == userId || c.CreatedBy == userId || (userId == 1 && c.CreatedBy == 0)));
    }

    public async Task<Subcategory?> FindSubcategoryByIdAsync(int subcategoryId, int categoryId, int userId)
    {
        return await _context.Subcategories.FirstOrDefaultAsync(s =>
            s.Id == subcategoryId &&
            s.CategoryId == categoryId &&
            s.IsDeleted == 0 &&
            (s.IsDefault || s.UserId == userId || s.CreatedBy == userId || (userId == 1 && s.CreatedBy == 0)));
    }

    public async Task<Subcategory?> FindSubcategoryByNameAsync(int categoryId, string subcategoryName, int userId)
    {
        var cleanSub = subcategoryName.Trim().ToLower();
        return await _context.Subcategories.FirstOrDefaultAsync(s =>
            s.CategoryId == categoryId &&
            s.Name.ToLower() == cleanSub &&
            s.IsDeleted == 0 &&
            (s.IsDefault || s.UserId == userId || s.CreatedBy == userId || (userId == 1 && s.CreatedBy == 0)));
    }

    public async Task<Transaction> CreateTransactionAsync(Transaction transaction)
    {
        _context.Transactions.Add(transaction);
        await _context.SaveChangesAsync();

        await _context.Entry(transaction).Reference(t => t.Category).LoadAsync();
        if (transaction.SubcategoryId.HasValue)
        {
            await _context.Entry(transaction).Reference(t => t.SubcategoryEntity).LoadAsync();
        }

        if (transaction.Type == "income")
        {
            await SyncBudgetWithIncomeAsync(transaction.Date, transaction.CreatedBy);
        }

        return transaction;
    }

    public async Task<Transaction> UpdateTransactionAsync(Transaction transaction)
    {
        _context.Transactions.Update(transaction);
        await _context.SaveChangesAsync();

        await _context.Entry(transaction).Reference(t => t.Category).LoadAsync();
        if (transaction.SubcategoryId.HasValue)
        {
            await _context.Entry(transaction).Reference(t => t.SubcategoryEntity).LoadAsync();
        }

        await SyncBudgetWithIncomeAsync(transaction.Date, transaction.CreatedBy);

        return transaction;
    }

    public async Task<bool> DeleteTransactionAsync(string id, int userId)
    {
        var transaction = await _context.Transactions.FirstOrDefaultAsync(t =>
            t.Id == id && t.IsDeleted == 0 && (t.CreatedBy == userId || (userId == 1 && t.CreatedBy == 0)));

        if (transaction == null) return false;

        transaction.IsDeleted = 1;
        transaction.UpdatedBy = userId;
        transaction.UpdatedDate = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        if (transaction.Type == "income")
        {
            await SyncBudgetWithIncomeAsync(transaction.Date, userId);
        }

        return true;
    }

    private async Task SyncBudgetWithIncomeAsync(string date, int userId)
    {
        if (string.IsNullOrWhiteSpace(date) || date.Length < 7) return;
        var monthYear = date[..7];

        var totalMonthIncome = await _context.Transactions
            .Where(t => t.IsDeleted == 0 && t.Date.StartsWith(monthYear) && t.Type == "income" && (t.CreatedBy == userId || (userId == 1 && t.CreatedBy == 0)))
            .SumAsync(t => t.Amount);

        var budgetRecord = await _context.Budgets.FirstOrDefaultAsync(b =>
            b.IsDeleted == 0 && b.MonthYear == monthYear && (b.CreatedBy == userId || (userId == 1 && b.CreatedBy == 0)));

        if (budgetRecord == null && totalMonthIncome > 0)
        {
            _context.Budgets.Add(new MonthlyBudget
            {
                MonthYear = monthYear,
                Amount = totalMonthIncome,
                UserId = userId,
                CreatedBy = userId,
                CreatedDate = DateTime.UtcNow,
                IsDeleted = 0
            });
            await _context.SaveChangesAsync();
        }
        else if (budgetRecord != null && totalMonthIncome > budgetRecord.Amount)
        {
            budgetRecord.Amount = totalMonthIncome;
            budgetRecord.UpdatedBy = userId;
            budgetRecord.UpdatedDate = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<MonthlySummaryDto> GetMonthlySummaryAsync(string monthYear, int userId)
    {
        var currentTxs = await _context.Transactions
            .Include(t => t.Category)
            .Include(t => t.SubcategoryEntity)
            .Where(t => t.IsDeleted == 0 && t.Date.StartsWith(monthYear) && (t.CreatedBy == userId || (userId == 1 && t.CreatedBy == 0)))
            .ToListAsync();

        var budgetRecord = await _context.Budgets.FirstOrDefaultAsync(b =>
            b.IsDeleted == 0 && b.MonthYear == monthYear && (b.CreatedBy == userId || (userId == 1 && b.CreatedBy == 0)));
        var baseBudget = budgetRecord?.Amount ?? 0m;

        // If no budget has been set (budget is 0), clean up any previously auto-generated budget transactions
        if (baseBudget <= 0)
        {
            var ghostTxs = currentTxs.Where(t =>
                t.Type == "income" &&
                (t.Note == "Monthly Budget" || t.Note == "Monthly Budget Allocation" || t.Note == "Opening Budget"))
                .ToList();

            if (ghostTxs.Count > 0)
            {
                foreach (var gt in ghostTxs)
                {
                    gt.IsDeleted = 1;
                    gt.UpdatedBy = userId;
                    gt.UpdatedDate = DateTime.UtcNow;
                }
                await _context.SaveChangesAsync();
                currentTxs.RemoveAll(t => ghostTxs.Contains(t));
            }
        }

        var totalIncome = currentTxs.Where(t => t.Type == "income").Sum(t => t.Amount);
        var totalExpense = currentTxs.Where(t => t.Type == "expense").Sum(t => t.Amount);
        var netBalance = totalIncome - totalExpense;

        // When user adds income, it automatically adds into the monthly budget limit
        var budgetLimit = Math.Max(baseBudget, totalIncome);
        if (budgetRecord != null && totalIncome > budgetRecord.Amount)
        {
            budgetRecord.Amount = totalIncome;
            budgetRecord.UpdatedBy = userId;
            budgetRecord.UpdatedDate = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
        else if (budgetRecord == null && totalIncome > 0)
        {
            _context.Budgets.Add(new MonthlyBudget
            {
                MonthYear = monthYear,
                Amount = totalIncome,
                UserId = userId,
                CreatedBy = userId,
                CreatedDate = DateTime.UtcNow,
                IsDeleted = 0
            });
            await _context.SaveChangesAsync();
        }

        var budgetLeft = budgetLimit > 0 ? budgetLimit - totalExpense : 0m;
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
        int year = int.Parse(monthYear[..4]);
        int month = int.Parse(monthYear[5..7]);
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
        if (categoryGroups.Count > 0)
        {
            var topCategory = categoryGroups[0];
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
                insightBuilder.Append($"Top spending category this month is {topCategory.Category}. ");
            }
        }

        if (prevTotalExpense > 0)
        {
            var overallDiff = Math.Round(((totalExpense - prevTotalExpense) / prevTotalExpense) * 100);
            if (overallDiff > 0)
                insightBuilder.Append($"Overall expenses increased by {overallDiff}% compared to last month.");
            else if (overallDiff < 0)
                insightBuilder.Append($"Total spending decreased by {Math.Abs(overallDiff)}% compared to last month.");
        }
        else if (totalExpense > 0)
        {
            insightBuilder.Append("Track your expenses regularly to see month-over-month trend analysis.");
        }
        else
        {
            insightBuilder.Append("No expenses recorded yet for this month. Start by adding your first transaction!");
        }

        return new MonthlySummaryDto
        {
            MonthYear = monthYear,
            TotalIncome = totalIncome,
            TotalExpense = totalExpense,
            NetBalance = netBalance,
            BudgetLimit = budgetLimit,
            BudgetLeft = budgetLeft,
            BudgetUsedPercentage = Math.Round(budgetPercent, 1),
            TotalTransactions = currentTxs.Count,
            InsightMessage = insightBuilder.ToString().Trim(),
            CategoryBreakdown = categoryGroups,
            DailySpending = dailySpending
        };
    }

    public async Task<YearlySummaryDto> GetYearlySummaryAsync(int year, int userId)
    {
        var yearPrefix = $"{year}-";
        var yearTxs = await _context.Transactions
            .Include(t => t.Category)
            .Include(t => t.SubcategoryEntity)
            .Where(t => t.IsDeleted == 0 && t.Date.StartsWith(yearPrefix) && (t.CreatedBy == userId || (userId == 1 && t.CreatedBy == 0)))
            .OrderBy(t => t.Date)
            .ToListAsync();

        var yearBudgets = await _context.Budgets
            .Where(b => b.IsDeleted == 0 && b.MonthYear.StartsWith(yearPrefix) && (b.CreatedBy == userId || (userId == 1 && b.CreatedBy == 0)))
            .ToListAsync();

        var totalIncome = yearTxs.Where(t => t.Type == "income").Sum(t => t.Amount);
        var totalExpense = yearTxs.Where(t => t.Type == "expense").Sum(t => t.Amount);
        var netSavings = totalIncome - totalExpense;
        var savingsRate = totalIncome > 0 ? (double)(netSavings / totalIncome) * 100.0 : 0.0;

        var monthNames = new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
        var monthlyBreakdown = new List<MonthBreakdownItemDto>();
        for (int m = 1; m <= 12; m++)
        {
            var mKey = $"{year}-{m:D2}";
            var mTxs = yearTxs.Where(t => t.Date.StartsWith(mKey)).ToList();

            var mIncome = mTxs.Where(t => t.Type == "income").Sum(t => t.Amount);
            var mExpense = mTxs.Where(t => t.Type == "expense").Sum(t => t.Amount);
            var mBaseBudget = yearBudgets.FirstOrDefault(b => b.MonthYear == mKey)?.Amount ?? 0m;
            var mBudget = Math.Max(mBaseBudget, mIncome);
            var topCat = mTxs
                .Where(t => t.Type == "expense")
                .GroupBy(t => t.Category?.Name ?? "Other")
                .OrderByDescending(g => g.Sum(x => x.Amount))
                .Select(g => g.Key)
                .FirstOrDefault() ?? "None";

            monthlyBreakdown.Add(new MonthBreakdownItemDto
            {
                Month = m,
                MonthName = monthNames[m - 1],
                MonthKey = mKey,
                TotalIncome = mIncome,
                TotalExpense = mExpense,
                NetBalance = mIncome - mExpense,
                BudgetLimit = mBudget,
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
            insight.Append($"Outstanding financial health in {year}! You are saving {savingsRate:F1}% of your annual income.");
        }
        else if (savingsRate > 0)
        {
            insight.Append($"Positive savings in {year}. You retained {savingsRate:F1}% of your annual earnings.");
        }
        else if (totalIncome > 0 && netSavings < 0)
        {
            insight.Append($"Annual expenses exceeded income in {year} by ₹{Math.Abs(netSavings):N0}.");
        }
        else
        {
            insight.Append($"Annual expense overview for {year}.");
        }

        if (topExpenseCat != null)
        {
            insight.Append($" Your primary expense driver was {topExpenseCat.Category} (₹{topExpenseCat.Amount:N0}, {topExpenseCat.Percentage}%).");
        }

        return new YearlySummaryDto
        {
            Year = year,
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
    }

    public async Task<WeeklySummaryDto> GetWeeklySummaryAsync(DateTime targetDate, int userId)
    {
        // Determine Monday of current target week
        int diff = ((int)targetDate.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var weekStart = targetDate.AddDays(-diff);
        var weekEnd = weekStart.AddDays(6);

        var startDateStr = weekStart.ToString("yyyy-MM-dd");
        var endDateStr = weekEnd.ToString("yyyy-MM-dd");

        // ISO Week number
        var cal = CultureInfo.InvariantCulture.Calendar;
        int weekNumber = cal.GetWeekOfYear(weekStart, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        int year = weekStart.Year;

        // Current week transactions
        var weekTxs = await _context.Transactions
            .Include(t => t.Category)
            .Where(t => t.IsDeleted == 0 &&
                        string.Compare(t.Date, startDateStr) >= 0 &&
                        string.Compare(t.Date, endDateStr) <= 0 &&
                        (t.CreatedBy == userId || (userId == 1 && t.CreatedBy == 0)))
            .OrderBy(t => t.Date)
            .ThenBy(t => t.CreatedDate)
            .ToListAsync();

        // Previous week transactions for week-over-week comparison
        var prevWeekStart = weekStart.AddDays(-7);
        var prevWeekEnd = weekEnd.AddDays(-7);
        var prevStartDateStr = prevWeekStart.ToString("yyyy-MM-dd");
        var prevEndDateStr = prevWeekEnd.ToString("yyyy-MM-dd");

        var prevWeekTxs = await _context.Transactions
            .Where(t => t.IsDeleted == 0 &&
                        t.Type == "expense" &&
                        string.Compare(t.Date, prevStartDateStr) >= 0 &&
                        string.Compare(t.Date, prevEndDateStr) <= 0 &&
                        (t.CreatedBy == userId || (userId == 1 && t.CreatedBy == 0)))
            .ToListAsync();

        var prevTotalExpense = prevWeekTxs.Sum(t => t.Amount);

        // Filter out auto-budget transactions from income
        var validTxs = weekTxs.Where(t => !(t.Type == "income" &&
            (t.Note == "Monthly Budget" || t.Note == "Monthly Budget Allocation" || t.Note == "Opening Budget"))).ToList();

        var totalIncome = validTxs.Where(t => t.Type == "income").Sum(t => t.Amount);
        var totalExpense = validTxs.Where(t => t.Type == "expense").Sum(t => t.Amount);
        var netBalance = totalIncome - totalExpense;
        var dailyAverage = Math.Round(totalExpense / 7m, 2);

        // Daily breakdown (7 days: Mon-Sun)
        var dailyBreakdown = new List<DaySpendingSummaryDto>();
        for (int i = 0; i < 7; i++)
        {
            var dayDate = weekStart.AddDays(i);
            var dayDateStr = dayDate.ToString("yyyy-MM-dd");
            var dayTxs = validTxs.Where(t => t.Date == dayDateStr).ToList();

            var dayIncome = dayTxs.Where(t => t.Type == "income").Sum(t => t.Amount);
            var dayExpense = dayTxs.Where(t => t.Type == "expense").Sum(t => t.Amount);

            dailyBreakdown.Add(new DaySpendingSummaryDto
            {
                Date = dayDateStr,
                DayName = dayDate.ToString("dddd", CultureInfo.InvariantCulture),
                DayShort = dayDate.ToString("ddd", CultureInfo.InvariantCulture),
                TotalIncome = dayIncome,
                TotalExpense = dayExpense,
                NetBalance = dayIncome - dayExpense,
                TransactionCount = dayTxs.Count
            });
        }

        var highestDay = dailyBreakdown.OrderByDescending(d => d.TotalExpense).FirstOrDefault();

        // Category breakdown
        var expenseTxs = validTxs.Where(t => t.Type == "expense").ToList();
        var categoryGroups = expenseTxs
            .GroupBy(t => t.Category?.Name ?? "Other")
            .Select(g => new CategoryBreakdownDto
            {
                Category = g.Key,
                Amount = g.Sum(x => x.Amount),
                Percentage = totalExpense > 0 ? Math.Round((double)(g.Sum(x => x.Amount) / totalExpense) * 100.0, 1) : 0
            })
            .OrderByDescending(c => c.Amount)
            .ToList();

        var topCat = categoryGroups.FirstOrDefault();

        double? wowChange = null;
        if (prevTotalExpense > 0)
        {
            wowChange = Math.Round(((double)(totalExpense - prevTotalExpense) / (double)prevTotalExpense) * 100.0, 1);
        }

        var formattedRange = $"{weekStart:dd MMM} – {weekEnd:dd MMM yyyy}";

        var insight = new StringBuilder();
        if (totalExpense > 0)
        {
            if (topCat != null)
            {
                insight.Append($"{topCat.Category} was your primary expense driver this week (₹{topCat.Amount:N0}, {topCat.Percentage}%). ");
            }
            if (wowChange.HasValue)
            {
                if (wowChange.Value > 0)
                    insight.Append($"You spent {wowChange.Value:F1}% more compared to last week (₹{prevTotalExpense:N0}). ");
                else if (wowChange.Value < 0)
                    insight.Append($"Great discipline! You spent {Math.Abs(wowChange.Value):F1}% less than last week. ");
            }
            if (highestDay?.TotalExpense > 0)
            {
                insight.Append($"Peak spending day was {highestDay.DayName} (₹{highestDay.TotalExpense:N0}).");
            }
        }
        else
        {
            insight.Append("No expenses recorded yet for this week.");
        }

        return new WeeklySummaryDto
        {
            Year = year,
            WeekNumber = weekNumber,
            StartDate = startDateStr,
            EndDate = endDateStr,
            FormattedRange = formattedRange,
            TotalIncome = totalIncome,
            TotalExpense = totalExpense,
            NetBalance = netBalance,
            DailyAverage = dailyAverage,
            TransactionCount = validTxs.Count,
            HighestDayName = (highestDay?.TotalExpense > 0) ? highestDay.DayName : "None",
            HighestDayAmount = highestDay?.TotalExpense ?? 0m,
            TopCategory = topCat?.Category ?? "None",
            TopCategoryAmount = topCat?.Amount ?? 0m,
            InsightMessage = insight.ToString(),
            PrevWeekExpense = prevTotalExpense,
            WeekOverWeekChangePercent = wowChange,
            CategoryBreakdown = categoryGroups,
            DailyBreakdown = dailyBreakdown
        };
    }

    public async Task<byte[]> ExportCsvAsync(string monthYear, int userId)
    {
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

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public async Task SeedSampleDataAsync()
    {
        await _context.SeedSampleDataAsync();
    }

    public async Task<int> ClearMonthAsync(string monthYear, int userId)
    {
        var txs = await _context.Transactions
            .Where(t => t.IsDeleted == 0 && t.Date.StartsWith(monthYear) && (t.CreatedBy == userId || (userId == 1 && t.CreatedBy == 0)))
            .ToListAsync();

        foreach (var t in txs)
        {
            t.IsDeleted = 1;
            t.UpdatedBy = userId;
            t.UpdatedDate = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        return txs.Count;
    }
}
