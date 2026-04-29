using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HngStageZeroClean.Data;
using HngStageZeroClean.Helpers;
using HngStageZeroClean.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace HngStageZeroClean.Services;

public class TokenService
{
    private readonly IConfiguration _config;
    private readonly AppDbContext _db;

    public TokenService(IConfiguration config, AppDbContext db)
    {
        _config = config;
        _db = db;
    }

    public string GenerateAccessToken(User user)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config["Jwt:Secret"] ?? throw new InvalidOperationException("JWT secret not configured")));

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("github_id", user.GitHubId)
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"] ?? "insighta-labs",
            audience: _config["Jwt:Audience"] ?? "insighta-labs",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(3),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task<RefreshToken> GenerateRefreshToken(User user)
    {
        var token = new RefreshToken
        {
            Id = UuidV7Generator.Create().ToString(),
            Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            IsRevoked = false,
            CreatedAt = DateTime.UtcNow
        };

        _db.RefreshTokens.Add(token);
        await _db.SaveChangesAsync();
        return token;
    }

    public async Task<(string accessToken, RefreshToken refreshToken)?> RefreshTokens(string refreshTokenValue)
    {
        var token = await _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == refreshTokenValue && !t.IsRevoked);

        if (token == null || token.ExpiresAt <= DateTime.UtcNow)
            return null;

        if (!token.User.IsActive)
            return null;

        token.IsRevoked = true;

        var newAccessToken = GenerateAccessToken(token.User);
        var newRefreshToken = await GenerateRefreshToken(token.User);

        token.User.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return (newAccessToken, newRefreshToken);
    }

    public async Task RevokeAllUserTokens(string userId)
    {
        var tokens = await _db.RefreshTokens
            .Where(t => t.UserId == userId && !t.IsRevoked)
            .ToListAsync();

        foreach (var t in tokens)
            t.IsRevoked = true;

        await _db.SaveChangesAsync();
    }

    public async Task RevokeToken(string refreshTokenValue)
    {
        var token = await _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.Token == refreshTokenValue && !t.IsRevoked);

        if (token != null)
        {
            token.IsRevoked = true;
            await _db.SaveChangesAsync();
        }
    }
}
