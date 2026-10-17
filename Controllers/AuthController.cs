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
    [HttpPost("register")]
    [EnableRateLimiting("auth-policy")]
    public async Task<ActionResult<AuthResponseDto>> Register([FromBody] RegisterDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var cleanMobile = dto.MobileNumber.Trim();
        var existingUser = await _context.Users.AnyAsync(u => u.MobileNumber == cleanMobile && u.IsDeleted == 0);
        if (existingUser)
        {
            return BadRequest(new { message = "This mobile number is already registered. Please login instead." });
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
            return Unauthorized(new { message = "Refresh token expired. 6-month session ended. Please login again." });
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
}
