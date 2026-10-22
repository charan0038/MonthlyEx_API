using Microsoft.EntityFrameworkCore;
using MonthExpenses.Api.Data;
using MonthExpenses.Api.Interfaces;
using MonthExpenses.Api.Models;

namespace MonthExpenses.Api.Repositories;

public class AuthRepository(ExpenseDbContext context) : IAuthRepository
{
    private readonly ExpenseDbContext _context = context;

    public async Task<bool> IsMobileRegisteredAsync(string mobile)
    {
        var cleanMobile = mobile.Trim();
        return await _context.Users.AnyAsync(u => u.MobileNumber == cleanMobile && u.IsDeleted == 0);
    }

    public async Task<bool> IsUsernameTakenAsync(string username, int? excludeUserId = null)
    {
        var cleanName = username.Trim().ToLower();
        var query = _context.Users.Where(u => u.FullName.ToLower() == cleanName && u.IsDeleted == 0);
        if (excludeUserId.HasValue)
        {
            query = query.Where(u => u.Id != excludeUserId.Value);
        }
        return await query.AnyAsync();
    }

    public async Task<User> CreateUserAsync(User user)
    {
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        if (user.CreatedBy == 0)
        {
            user.CreatedBy = user.Id;
            await _context.SaveChangesAsync();
        }

        return user;
    }

    public async Task<User?> GetUserByIdentifierAsync(string identifier)
    {
        var clean = identifier.Trim();
        return await _context.Users.FirstOrDefaultAsync(u =>
            (u.MobileNumber == clean || u.FullName.ToLower() == clean.ToLower()) && u.IsDeleted == 0);
    }

    public async Task<User?> GetUserByIdAsync(int id)
    {
        return await _context.Users.FirstOrDefaultAsync(u => u.Id == id && u.IsDeleted == 0);
    }

    public async Task<User> UpdateUserAsync(User user)
    {
        _context.Users.Update(user);
        await _context.SaveChangesAsync();
        return user;
    }

    public async Task<RefreshToken> SaveRefreshTokenAsync(RefreshToken token)
    {
        _context.RefreshTokens.Add(token);
        await _context.SaveChangesAsync();
        return token;
    }

    public async Task<RefreshToken?> GetRefreshTokenAsync(string token)
    {
        return await _context.RefreshTokens
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Token == token && r.IsDeleted == 0);
    }

    public async Task<bool> RevokeRefreshTokenAsync(string token)
    {
        var storedToken = await _context.RefreshTokens.FirstOrDefaultAsync(r => r.Token == token);
        if (storedToken != null && !storedToken.IsRevoked)
        {
            storedToken.RevokedAt = DateTime.UtcNow;
            storedToken.UpdatedDate = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }
        return false;
    }

    public async Task RevokeActiveUserRefreshTokensAsync(int userId)
    {
        var activeTokens = await _context.RefreshTokens
            .Where(r => r.UserId == userId && r.RevokedAt == null && r.ExpiresAt > DateTime.UtcNow)
            .ToListAsync();

        foreach (var t in activeTokens)
        {
            t.RevokedAt = DateTime.UtcNow;
            t.UpdatedDate = DateTime.UtcNow;
            t.UpdatedBy = userId;
        }

        await _context.SaveChangesAsync();
    }
}
