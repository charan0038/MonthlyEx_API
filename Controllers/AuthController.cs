using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MonthExpenses.Api.Data;
using MonthExpenses.Api.DTOs;
using MonthExpenses.Api.Models;
using MonthExpenses.Api.Services;

namespace MonthExpenses.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ExpenseDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(ExpenseDbContext context, ITokenService tokenService, ILogger<AuthController> logger)
    {
        _context = context;
        _tokenService = tokenService;
        _logger = logger;
    }

    /// <summary>
    /// Register a new account with Mobile Number and Password.
    /// Rate limited by "auth-policy" (5 attempts per minute per IP) to prevent spam.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("register")]
    [EnableRateLimiting("auth-policy")]
    public async Task<ActionResult<AuthResponseDto>> Register([FromBody] RegisterDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var cleanMobile = dto.MobileNumber.Trim();
        var cleanName = dto.FullName.Trim();

        // 1. Prevent duplicate mobile numbers
        var existingMobile = await _context.Users.AnyAsync(u => u.MobileNumber == cleanMobile && u.IsDeleted == 0);
        if (existingMobile)
        {
            return BadRequest(new { message = "This mobile number is already registered. Please sign in instead." });
        }

        // 2. Prevent duplicate usernames/names (case-insensitive)
        var existingName = await _context.Users.AnyAsync(u => u.FullName.ToLower() == cleanName.ToLower() && u.IsDeleted == 0);
        if (existingName)
        {
            return BadRequest(new { message = "This username is already taken. Please choose a different name." });
        }

        var user = new User
        {
            FullName = dto.FullName.Trim(),
            MobileNumber = cleanMobile,
            PasswordHash = PasswordHelper.HashPassword(dto.Password),
            CreatedDate = DateTime.UtcNow,
            LastLoginAt = DateTime.UtcNow,
            IsDeleted = 0
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        user.CreatedBy = user.Id;
        await _context.SaveChangesAsync();

        // Generate Access Token & 6-Month Refresh Token
        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken(user.Id);

        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync();

        _logger.LogInformation("New user registered with mobile: {Mobile}", cleanMobile);

        return Ok(new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            ExpiresIn = _tokenService.AccessTokenExpirationSeconds,
            RefreshTokenExpiresAt = refreshToken.ExpiresAt,
            User = new UserDto
            {
                Id = user.Id,
                FullName = user.FullName,
                MobileNumber = user.MobileNumber,
                CreatedDate = user.CreatedDate,
                LastLoginAt = user.LastLoginAt
            }
        });
    }

    /// <summary>
    /// Login with Mobile Number and Password.
    /// Rate limited by "auth-policy" (5 attempts per minute per IP) to prevent brute-force attacks.
    /// Returns JWT Access Token + 6-month Refresh Token.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("login")]
    [EnableRateLimiting("auth-policy")]
    public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var identifier = dto.MobileNumber.Trim();
        var user = await _context.Users.FirstOrDefaultAsync(u =>
            (u.MobileNumber == identifier || u.FullName.ToLower() == identifier.ToLower()) && u.IsDeleted == 0);

        if (user == null || !PasswordHelper.VerifyPassword(dto.Password, user.PasswordHash))
        {
            return Unauthorized(new { message = "Invalid mobile number/username or password." });
        }

        user.LastLoginAt = DateTime.UtcNow;
        user.UpdatedDate = DateTime.UtcNow;
        user.UpdatedBy = user.Id;

        // Generate Access Token & 6-Month Refresh Token
        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken(user.Id);

        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync();

        _logger.LogInformation("User logged in successfully: {Identifier}", identifier);

        return Ok(new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            ExpiresIn = _tokenService.AccessTokenExpirationSeconds,
            RefreshTokenExpiresAt = refreshToken.ExpiresAt,
            User = new UserDto
            {
                Id = user.Id,
                FullName = user.FullName,
                MobileNumber = user.MobileNumber,
                CreatedDate = user.CreatedDate,
                LastLoginAt = user.LastLoginAt
            }
        });
    }

    /// <summary>
    /// Refresh an expired JWT access token using the 6-month refresh token.
    /// Rotates the refresh token for maximum security.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponseDto>> Refresh([FromBody] RefreshTokenDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.RefreshToken))
            return BadRequest(new { message = "Refresh token is required." });

        var storedToken = await _context.RefreshTokens
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Token == dto.RefreshToken && r.IsDeleted == 0);

        if (storedToken == null)
        {
            return Unauthorized(new { message = "Invalid refresh token." });
        }

        if (storedToken.IsRevoked)
        {
            return Unauthorized(new { message = "Refresh token has been revoked. Please login again." });
        }

        if (storedToken.IsExpired)
        {
            return Unauthorized(new { message = "Session expired. Please login again." });
        }

        var user = storedToken.User;
        if (user == null || user.IsDeleted != 0)
        {
            return Unauthorized(new { message = "Associated user not found." });
        }

        // Rotate Refresh Token
        storedToken.RevokedAt = DateTime.UtcNow;
        storedToken.UpdatedDate = DateTime.UtcNow;
        storedToken.UpdatedBy = user.Id;

        var newRefreshToken = _tokenService.GenerateRefreshToken(user.Id);
        storedToken.ReplacedByToken = newRefreshToken.Token;

        _context.RefreshTokens.Add(newRefreshToken);
        await _context.SaveChangesAsync();

        var newAccessToken = _tokenService.GenerateAccessToken(user);

        return Ok(new AuthResponseDto
        {
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken.Token,
            ExpiresIn = _tokenService.AccessTokenExpirationSeconds,
            RefreshTokenExpiresAt = newRefreshToken.ExpiresAt,
            User = new UserDto
            {
                Id = user.Id,
                FullName = user.FullName,
                MobileNumber = user.MobileNumber,
                CreatedDate = user.CreatedDate,
                LastLoginAt = user.LastLoginAt
            }
        });
    }

    /// <summary>
    /// Revoke the refresh token on logout.
    /// </summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.RefreshToken))
        {
            var storedToken = await _context.RefreshTokens.FirstOrDefaultAsync(r => r.Token == dto.RefreshToken);
            if (storedToken != null && !storedToken.IsRevoked)
            {
                storedToken.RevokedAt = DateTime.UtcNow;
                storedToken.UpdatedDate = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }

        return Ok(new { message = "Logged out successfully." });
    }

    /// <summary>
    /// Get current logged-in user profile from JWT claims.
    /// </summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> GetCurrentUser()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId && u.IsDeleted == 0);
        if (user == null)
            return NotFound(new { message = "User not found." });

        return Ok(new UserDto
        {
            Id = user.Id,
            FullName = user.FullName,
            MobileNumber = user.MobileNumber,
            CreatedDate = user.CreatedDate,
            LastLoginAt = user.LastLoginAt
        });
    }

    /// <summary>
    /// Change Password for authenticated user.
    /// Requires current password verification.
    /// </summary>
    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId && u.IsDeleted == 0);
        if (user == null)
            return NotFound(new { message = "User not found." });

        if (!PasswordHelper.VerifyPassword(dto.CurrentPassword, user.PasswordHash))
        {
            return BadRequest(new { message = "Current password is incorrect." });
        }

        if (dto.NewPassword == dto.CurrentPassword)
        {
            return BadRequest(new { message = "New password must be different from current password." });
        }

        user.PasswordHash = PasswordHelper.HashPassword(dto.NewPassword);
        user.UpdatedDate = DateTime.UtcNow;
        user.UpdatedBy = user.Id;

        // Revoke active refresh tokens
        var activeTokens = await _context.RefreshTokens
            .Where(r => r.UserId == user.Id && r.RevokedAt == null && r.ExpiresAt > DateTime.UtcNow)
            .ToListAsync();
        foreach (var t in activeTokens)
        {
            t.RevokedAt = DateTime.UtcNow;
            t.UpdatedDate = DateTime.UtcNow;
            t.UpdatedBy = user.Id;
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Password changed successfully for user ID: {UserId}", user.Id);

        return Ok(new { message = "Password changed successfully!" });
    }

    /// <summary>
    /// Forgot Password / Reset Password using registered mobile number or username.
    /// Rate limited by auth-policy (5 attempts per minute per IP) to prevent spam.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth-policy")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var identifier = dto.MobileNumber.Trim();
        var user = await _context.Users.FirstOrDefaultAsync(u =>
            (u.MobileNumber == identifier || u.FullName.ToLower() == identifier.ToLower()) && u.IsDeleted == 0);

        if (user == null)
        {
            return NotFound(new { message = "No account found registered with this mobile number or username." });
        }

        user.PasswordHash = PasswordHelper.HashPassword(dto.NewPassword);
        user.UpdatedDate = DateTime.UtcNow;
        user.UpdatedBy = user.Id;

        // Revoke active refresh tokens
        var activeTokens = await _context.RefreshTokens
            .Where(r => r.UserId == user.Id && r.RevokedAt == null && r.ExpiresAt > DateTime.UtcNow)
            .ToListAsync();
        foreach (var t in activeTokens)
        {
            t.RevokedAt = DateTime.UtcNow;
            t.UpdatedDate = DateTime.UtcNow;
            t.UpdatedBy = user.Id;
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Password reset successfully for user: {Identifier}", identifier);

        return Ok(new { message = "Password reset successfully! You can now sign in with your new password." });
    }

    /// <summary>
    /// Update user profile (Full Name / Username and Mobile Number).
    /// Requires Bearer JWT Token.
    /// </summary>
    [Authorize]
    [HttpPut("profile")]
    public async Task<ActionResult<UserDto>> UpdateProfile([FromBody] UpdateProfileDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId && u.IsDeleted == 0);
        if (user == null)
            return NotFound(new { message = "User not found." });

        var cleanName = dto.FullName.Trim();
        var cleanMobile = dto.MobileNumber.Trim();

        // Check if username/name taken by another user
        var nameTaken = await _context.Users.AnyAsync(u => u.Id != userId && u.FullName.ToLower() == cleanName.ToLower() && u.IsDeleted == 0);
        if (nameTaken)
        {
            return BadRequest(new { message = "This username is already taken. Please choose a different name." });
        }

        // Check if mobile number taken by another user
        var mobileTaken = await _context.Users.AnyAsync(u => u.Id != userId && u.MobileNumber == cleanMobile && u.IsDeleted == 0);
        if (mobileTaken)
        {
            return BadRequest(new { message = "This mobile number is already registered to another account." });
        }

        user.FullName = cleanName;
        user.MobileNumber = cleanMobile;
        user.UpdatedDate = DateTime.UtcNow;
        user.UpdatedBy = user.Id;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Profile updated successfully for user ID: {UserId}", user.Id);

        return Ok(new UserDto
        {
            Id = user.Id,
            FullName = user.FullName,
            MobileNumber = user.MobileNumber,
            CreatedDate = user.CreatedDate,
            LastLoginAt = user.LastLoginAt
        });
    }
}
