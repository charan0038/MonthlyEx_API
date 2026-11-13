using Microsoft.EntityFrameworkCore;
using MonthExpenses.Api.Data;
using MonthExpenses.Api.DTOs;
using MonthExpenses.Api.Interfaces;
using MonthExpenses.Api.Models;

namespace MonthExpenses.Api.Repositories;

public class EmiRepository(ExpenseDbContext context) : IEmiRepository
{
    private readonly ExpenseDbContext _context = context;

    public async Task<IEnumerable<EmiDto>> GetEmisAsync(int userId)
    {
        var emis = await _context.Emis
            .Include(e => e.Category)
            .Where(e => e.IsDeleted == 0 && (e.UserId == userId || (userId == 1 && e.CreatedBy == 0)))
            .OrderBy(e => e.DueDay)
            .ToListAsync();

        if (emis.Count == 0) return Enumerable.Empty<EmiDto>();

        var catIds = emis.Select(e => e.CategoryId).Distinct().ToList();

        // Get all expense transactions belonging to these categories
        var allTxs = await _context.Transactions
            .Where(t => t.IsDeleted == 0 &&
                        (t.CreatedBy == userId || (userId == 1 && t.CreatedBy == 0)) &&
                        t.Type.ToLower() == "expense" &&
                        catIds.Contains(t.CategoryId))
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync();

        var result = new List<EmiDto>();

        foreach (var emi in emis)
        {
            var txsForEmi = allTxs.Where(t => t.CategoryId == emi.CategoryId).ToList();
            var totalPaid = txsForEmi.Sum(t => t.Amount);
            var remaining = Math.Max(0m, emi.TotalAmount - totalPaid);
            var progress = emi.TotalAmount > 0m
                ? Math.Min(100.0, Math.Round((double)(totalPaid / emi.TotalAmount) * 100, 1))
                : 0.0;

            var recent = txsForEmi.Take(5).Select(t => new EmiPaymentItemDto
            {
                TransactionId = t.Id,
                Amount = t.Amount,
                Date = t.Date,
                Note = t.Note,
                PaymentMethod = t.PaymentMethod
            }).ToList();

            result.Add(new EmiDto
            {
                Id = emi.Id,
                Name = emi.Name,
                TotalAmount = emi.TotalAmount,
                MonthlyAmount = emi.MonthlyAmount,
                CategoryId = emi.CategoryId,
                CategoryName = emi.Category?.Name ?? "EMI",
                CategoryIcon = emi.Category?.IconName ?? "CreditCard",
                CategoryColor = emi.Category?.Color ?? "#6366f1",
                StartDate = emi.StartDate,
                DueDay = emi.DueDay,
                TotalTenorMonths = emi.TotalTenorMonths,
                Notes = emi.Notes,
                TotalPaid = totalPaid,
                RemainingAmount = remaining,
                ProgressPercentage = progress,
                PaymentsCount = txsForEmi.Count,
                IsCompleted = remaining <= 0m,
                CreatedDate = emi.CreatedDate,
                RecentPayments = recent
            });
        }

        // Return active first, then completed
        return result.OrderBy(e => e.IsCompleted).ThenBy(e => e.DueDay);
    }

    public async Task<EmiDto?> GetEmiByIdAsync(int id, int userId)
    {
        var emi = await _context.Emis
            .Include(e => e.Category)
            .FirstOrDefaultAsync(e => e.Id == id && e.IsDeleted == 0 && (e.UserId == userId || (userId == 1 && e.CreatedBy == 0)));

        if (emi == null) return null;

        var txs = await _context.Transactions
            .Where(t => t.IsDeleted == 0 &&
                        (t.CreatedBy == userId || (userId == 1 && t.CreatedBy == 0)) &&
                        t.Type.ToLower() == "expense" &&
                        t.CategoryId == emi.CategoryId)
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync();

        var totalPaid = txs.Sum(t => t.Amount);
        var remaining = Math.Max(0m, emi.TotalAmount - totalPaid);
        var progress = emi.TotalAmount > 0m
            ? Math.Min(100.0, Math.Round((double)(totalPaid / emi.TotalAmount) * 100, 1))
            : 0.0;

        return new EmiDto
        {
            Id = emi.Id,
            Name = emi.Name,
            TotalAmount = emi.TotalAmount,
            MonthlyAmount = emi.MonthlyAmount,
            CategoryId = emi.CategoryId,
            CategoryName = emi.Category?.Name ?? "EMI",
            CategoryIcon = emi.Category?.IconName ?? "CreditCard",
            CategoryColor = emi.Category?.Color ?? "#6366f1",
            StartDate = emi.StartDate,
            DueDay = emi.DueDay,
            TotalTenorMonths = emi.TotalTenorMonths,
            Notes = emi.Notes,
            TotalPaid = totalPaid,
            RemainingAmount = remaining,
            ProgressPercentage = progress,
            PaymentsCount = txs.Count,
            IsCompleted = remaining <= 0m,
            CreatedDate = emi.CreatedDate,
            RecentPayments = txs.Select(t => new EmiPaymentItemDto
            {
                TransactionId = t.Id,
                Amount = t.Amount,
                Date = t.Date,
                Note = t.Note,
                PaymentMethod = t.PaymentMethod
            }).ToList()
        };
    }

    public async Task<EmiLoan> CreateEmiAsync(EmiLoan emi)
    {
        _context.Emis.Add(emi);
        await _context.SaveChangesAsync();
        return emi;
    }

    public async Task<EmiLoan?> UpdateEmiAsync(int id, UpdateEmiDto dto, int userId)
    {
        var emi = await _context.Emis
            .FirstOrDefaultAsync(e => e.Id == id && e.IsDeleted == 0 && (e.UserId == userId || (userId == 1 && e.CreatedBy == 0)));

        if (emi == null) return null;

        emi.Name = dto.Name.Trim();
        emi.TotalAmount = dto.TotalAmount;
        emi.MonthlyAmount = dto.MonthlyAmount;
        emi.CategoryId = dto.CategoryId;
        emi.StartDate = string.IsNullOrWhiteSpace(dto.StartDate) ? emi.StartDate : dto.StartDate.Trim();
        emi.DueDay = dto.DueDay > 0 ? dto.DueDay : emi.DueDay;
        emi.TotalTenorMonths = dto.TotalTenorMonths > 0 ? dto.TotalTenorMonths : emi.TotalTenorMonths;
        emi.Notes = dto.Notes?.Trim();
        emi.UpdatedBy = userId;
        emi.UpdatedDate = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return emi;
    }

    public async Task<bool> DeleteEmiAsync(int id, int userId)
    {
        var emi = await _context.Emis
            .FirstOrDefaultAsync(e => e.Id == id && e.IsDeleted == 0 && (e.UserId == userId || (userId == 1 && e.CreatedBy == 0)));

        if (emi == null) return false;

        emi.IsDeleted = 1;
        emi.UpdatedBy = userId;
        emi.UpdatedDate = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<List<EmiPaymentItemDto>> GetEmiPaymentHistoryAsync(int categoryId, int userId)
    {
        return await _context.Transactions
            .Where(t => t.IsDeleted == 0 &&
                        (t.CreatedBy == userId || (userId == 1 && t.CreatedBy == 0)) &&
                        t.Type.ToLower() == "expense" &&
                        t.CategoryId == categoryId)
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.CreatedAt)
            .Select(t => new EmiPaymentItemDto
            {
                TransactionId = t.Id,
                Amount = t.Amount,
                Date = t.Date,
                Note = t.Note,
                PaymentMethod = t.PaymentMethod
            })
            .ToListAsync();
    }
}
