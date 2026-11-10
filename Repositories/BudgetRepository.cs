using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MonthExpenses.Api.Data;
using MonthExpenses.Api.Interfaces;
using MonthExpenses.Api.Models;

namespace MonthExpenses.Api.Repositories;

public class BudgetRepository(ExpenseDbContext context) : IBudgetRepository
{
    private readonly ExpenseDbContext _context = context;

    public async Task<MonthlyBudget> GetBudgetAsync(string monthYear, int userId)
    {
        var budget = await _context.Budgets.FirstOrDefaultAsync(b =>
            b.IsDeleted == 0 &&
            b.MonthYear == monthYear &&
            (b.CreatedBy == userId || (userId == 1 && b.CreatedBy == 0)));

        var totalMonthIncome = await _context.Transactions
            .Where(t => t.IsDeleted == 0 && t.Date.StartsWith(monthYear) && t.Type == "income" && (t.CreatedBy == userId || (userId == 1 && t.CreatedBy == 0)))
            .SumAsync(t => t.Amount);

        if (budget == null)
        {
            return new MonthlyBudget
            {
                MonthYear = monthYear,
                Amount = totalMonthIncome,
                CreatedBy = userId,
                CreatedDate = DateTime.UtcNow,
                IsDeleted = 0
            };
        }

        if (totalMonthIncome > budget.Amount)
        {
            budget.Amount = totalMonthIncome;
            budget.UpdatedBy = userId;
            budget.UpdatedDate = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return budget;
    }

    public async Task<MonthlyBudget> SetBudgetAsync(string monthYear, decimal amount, int userId)
    {
        var budget = await _context.Budgets.FirstOrDefaultAsync(b =>
            b.IsDeleted == 0 &&
            b.MonthYear == monthYear &&
            b.CreatedBy == userId);

        if (budget == null)
        {
            budget = new MonthlyBudget
            {
                MonthYear = monthYear,
                Amount = amount,
                UserId = userId,
                CreatedBy = userId,
                CreatedDate = DateTime.UtcNow,
                IsDeleted = 0
            };
            _context.Budgets.Add(budget);
        }
        else
        {
            budget.Amount = amount;
            budget.UserId = userId;
            budget.UpdatedBy = userId;
            budget.UpdatedDate = DateTime.UtcNow;
        }

        // Setting a budget always allocates it to Total Income & Net Balance for that month
        if (amount > 0)
        {
            var budgetIncomeTx = await _context.Transactions.FirstOrDefaultAsync(t =>
                t.IsDeleted == 0 &&
                t.CreatedBy == userId &&
                t.Type == "income" &&
                t.Date.StartsWith(monthYear) &&
                (t.Note == "Monthly Budget" || t.Note == "Monthly Budget Allocation" || t.Note == "Opening Budget"));

            if (budgetIncomeTx != null)
            {
                budgetIncomeTx.Amount = amount;
                budgetIncomeTx.UpdatedBy = userId;
                budgetIncomeTx.UpdatedDate = DateTime.UtcNow;
            }
            else
            {
                var incomeCat = await _context.Categories.FirstOrDefaultAsync(c =>
                    c.IsDeleted == 0 &&
                    c.Type == "income" &&
                    (c.IsDefault || c.UserId == userId || c.CreatedBy == userId)) ??
                    await _context.Categories.FirstOrDefaultAsync(c => c.IsDeleted == 0 && c.Type == "income") ??
                    await _context.Categories.FirstOrDefaultAsync(c => c.IsDeleted == 0);

                if (incomeCat != null)
                {
                    var newIncomeTx = new Transaction
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Type = "income",
                        Amount = amount,
                        CategoryId = incomeCat.Id,
                        Date = $"{monthYear}-01",
                        Note = "Monthly Budget",
                        PaymentMethod = "UPI",
                        CreatedBy = userId,
                        CreatedDate = DateTime.UtcNow,
                        IsDeleted = 0
                    };
                    _context.Transactions.Add(newIncomeTx);
                }
            }
        }

        await _context.SaveChangesAsync();
        return budget;
    }

    public async Task<CashCheckoutResult> CashCheckoutAsync(string monthYear, decimal? amount, int userId)
    {
        var monthTxs = await _context.Transactions
            .Where(t => t.IsDeleted == 0 && t.Date.StartsWith(monthYear) && (t.CreatedBy == userId || (userId == 1 && t.CreatedBy == 0)))
            .ToListAsync();

        var totalIncome = monthTxs.Where(t => t.Type == "income").Sum(t => t.Amount);
        var totalExpense = monthTxs.Where(t => t.Type == "expense").Sum(t => t.Amount);
        var netBalance = totalIncome - totalExpense;

        decimal amountToCheckout = amount ?? netBalance;
        if (amountToCheckout <= 0)
        {
            return new CashCheckoutResult
            {
                Success = false,
                Message = "No remaining balance available for cash checkout."
            };
        }

        var expenseCat = await _context.Categories.FirstOrDefaultAsync(c =>
            c.IsDeleted == 0 &&
            c.Type == "expense" &&
            c.Name.ToLower() == "other" &&
            (c.IsDefault || c.CreatedBy == userId || c.UserId == userId)) ??
            await _context.Categories.FirstOrDefaultAsync(c => c.IsDeleted == 0 && c.Type == "expense") ??
            await _context.Categories.FirstOrDefaultAsync(c => c.IsDeleted == 0);

        if (expenseCat == null)
        {
            return new CashCheckoutResult
            {
                Success = false,
                Message = "No category found for cash checkout."
            };
        }

        var tx = new Transaction
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = "expense",
            Amount = amountToCheckout,
            CategoryId = expenseCat.Id,
            Date = DateTime.UtcNow.ToString("yyyy-MM-dd").StartsWith(monthYear)
                ? DateTime.UtcNow.ToString("yyyy-MM-dd")
                : $"{monthYear}-28",
            Note = "Cash Checkout",
            PaymentMethod = "Cash",
            CreatedBy = userId,
            CreatedDate = DateTime.UtcNow,
            IsDeleted = 0
        };

        _context.Transactions.Add(tx);
        await _context.SaveChangesAsync();

        return new CashCheckoutResult
        {
            Success = true,
            Message = $"Successfully checked out {amountToCheckout:C} as cash.",
            Amount = amountToCheckout,
            Transaction = tx
        };
    }

    public async Task<RolloverBalanceResult> RolloverBalanceAsync(string monthYear, decimal? amount, int userId)
    {
        if (!DateTime.TryParseExact(monthYear + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var currentMonthDate))
        {
            return new RolloverBalanceResult
            {
                Success = false,
                Message = "Invalid month format. Expected YYYY-MM."
            };
        }

        var nextMonthDate = currentMonthDate.AddMonths(1);
        var nextMonthKey = nextMonthDate.ToString("yyyy-MM");

        var monthTxs = await _context.Transactions
            .Where(t => t.IsDeleted == 0 && t.Date.StartsWith(monthYear) && (t.CreatedBy == userId || (userId == 1 && t.CreatedBy == 0)))
            .ToListAsync();

        // Calculate real income and real expenses (do NOT treat balance transfers to next month as expenses!)
        var totalIncome = monthTxs.Where(t => t.Type == "income").Sum(t => t.Amount);
        var totalExpense = monthTxs.Where(t => t.Type == "expense" && !(t.Note != null && t.Note.StartsWith("Transferred to ") && t.Note.EndsWith(" Opening Balance"))).Sum(t => t.Amount);
        var netBalance = totalIncome - totalExpense;

        decimal amountToRollover = amount ?? netBalance;
        if (amountToRollover <= 0)
        {
            return new RolloverBalanceResult
            {
                Success = false,
                Message = "No remaining balance available to carry forward."
            };
        }

        // Soft-delete any legacy rollover transfer-out expense transactions in the current month so expenses stay pure
        var legacyTransferOuts = monthTxs.Where(t => t.Type == "expense" && t.Note != null && t.Note.StartsWith("Transferred to ") && t.Note.EndsWith(" Opening Balance")).ToList();
        if (legacyTransferOuts.Count > 0)
        {
            foreach (var ltx in legacyTransferOuts)
            {
                ltx.IsDeleted = 1;
                ltx.UpdatedDate = DateTime.UtcNow;
            }
        }

        // 2. Next month: Opening Balance Income
        var incomeCat = await _context.Categories.FirstOrDefaultAsync(c =>
            c.IsDeleted == 0 &&
            c.Type == "income" &&
            (c.IsDefault || c.CreatedBy == userId || c.UserId == userId)) ??
            await _context.Categories.FirstOrDefaultAsync(c => c.IsDeleted == 0 && c.Type == "income") ??
            await _context.Categories.FirstOrDefaultAsync(c => c.IsDeleted == 0);

        if (incomeCat == null)
        {
            return new RolloverBalanceResult
            {
                Success = false,
                Message = "Income category could not be found."
            };
        }

        // Check if an opening balance income transaction already exists in next month from this month
        var existingOpeningTx = await _context.Transactions.FirstOrDefaultAsync(t =>
            t.IsDeleted == 0 &&
            t.Date.StartsWith(nextMonthKey) &&
            t.Type == "income" &&
            t.Note == $"Opening Balance from {monthYear}" &&
            (t.CreatedBy == userId || (userId == 1 && t.CreatedBy == 0)));

        Transaction openingIncomeTx;
        if (existingOpeningTx != null)
        {
            existingOpeningTx.Amount = amountToRollover;
            existingOpeningTx.UpdatedBy = userId;
            existingOpeningTx.UpdatedDate = DateTime.UtcNow;
            openingIncomeTx = existingOpeningTx;
        }
        else
        {
            openingIncomeTx = new Transaction
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = "income",
                Amount = amountToRollover,
                CategoryId = incomeCat.Id,
                Date = $"{nextMonthKey}-01",
                Note = $"Opening Balance from {monthYear}",
                PaymentMethod = "Cash",
                CreatedBy = userId,
                CreatedDate = DateTime.UtcNow,
                IsDeleted = 0
            };
            _context.Transactions.Add(openingIncomeTx);
        }

        await _context.SaveChangesAsync();

        return new RolloverBalanceResult
        {
            Success = true,
            Message = $"Successfully transferred ₹{amountToRollover:N0} to {nextMonthKey} as opening balance income.",
            Amount = amountToRollover,
            NextMonth = nextMonthKey,
            TransferOutTx = null,
            OpeningIncomeTx = openingIncomeTx
        };
    }
}
