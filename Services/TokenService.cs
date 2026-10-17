using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using MonthExpenses.Api.Models;

namespace MonthExpenses.Api.Services;

public interface ITokenService
{
    string GenerateAccessToken(User user);
    RefreshToken GenerateRefreshToken(int userId);
    int AccessTokenExpirationSeconds { get; }
    int RefreshTokenExpirationMonths { get; }
}

public class TokenService : ITokenService
{
    private readonly IConfiguration _config;
    private readonly string _jwtKey;
    private readonly string _jwtIssuer;
    private readonly string _jwtAudience;
    private readonly int _accessTokenExpirationMinutes;
    private readonly int _refreshTokenExpirationMonths;

    public TokenService(IConfiguration config)
    {
        _config = config;
        _jwtKey = _config["Jwt:Key"] ?? "KharchaSecureJwtTokenKey2026_WithHighEntropyAtLeast256BitsLong!";
        _jwtIssuer = _config["Jwt:Issuer"] ?? "KharchaApi";
        _jwtAudience = _config["Jwt:Audience"] ?? "KharchaClient";

        if (!int.TryParse(_config["Jwt:AccessTokenExpirationMinutes"], out _accessTokenExpirationMinutes))
        {
            _accessTokenExpirationMinutes = 60;
        }

        if (!int.TryParse(_config["Jwt:RefreshTokenExpirationMonths"], out _refreshTokenExpirationMonths))
        {
            _refreshTokenExpirationMonths = 6;
        }
    }

    public int AccessTokenExpirationSeconds => _accessTokenExpirationMinutes * 60;
    public int RefreshTokenExpirationMonths => _refreshTokenExpirationMonths;

    public string GenerateAccessToken(User user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_jwtKey);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.MobilePhone, user.MobileNumber),
            new(ClaimTypes.Name, user.FullName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(_accessTokenExpirationMinutes),
            Issuer = _jwtIssuer,
            Audience = _jwtAudience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    public RefreshToken GenerateRefreshToken(int userId)
    {
        var randomBytes = RandomNumberGenerator.GetBytes(64);
        var tokenString = Convert.ToBase64String(randomBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        return new RefreshToken
        {
            Token = tokenString,
            UserId = userId,
            CreatedBy = userId,
            CreatedDate = DateTime.UtcNow,
            // 6 Months validity as requested by user
            ExpiresAt = DateTime.UtcNow.AddMonths(_refreshTokenExpirationMonths),
            IsDeleted = 0
        };
    }
}
