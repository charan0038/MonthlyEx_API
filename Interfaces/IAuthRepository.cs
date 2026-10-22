using MonthExpenses.Api.Models;

namespace MonthExpenses.Api.Interfaces;

public interface IAuthRepository
{
    Task<bool> IsMobileRegisteredAsync(string mobile);
    Task<bool> IsUsernameTakenAsync(string username, int? excludeUserId = null);
    Task<User> CreateUserAsync(User user);
    Task<User?> GetUserByIdentifierAsync(string identifier);
    Task<User?> GetUserByIdAsync(int id);
    Task<User> UpdateUserAsync(User user);

    Task<RefreshToken> SaveRefreshTokenAsync(RefreshToken token);
    Task<RefreshToken?> GetRefreshTokenAsync(string token);
    Task<bool> RevokeRefreshTokenAsync(string token);
    Task RevokeActiveUserRefreshTokensAsync(int userId);
}
