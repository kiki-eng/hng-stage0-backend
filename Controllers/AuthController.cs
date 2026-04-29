using System.Security.Claims;
using System.Text.Json.Serialization;
using HngStageZeroClean.Data;
using HngStageZeroClean.Helpers;
using HngStageZeroClean.Models;
using HngStageZeroClean.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HngStageZeroClean.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly GitHubService _github;
    private readonly TokenService _tokens;
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public AuthController(GitHubService github, TokenService tokens, AppDbContext db, IConfiguration config)
    {
        _github = github;
        _tokens = tokens;
        _db = db;
        _config = config;
    }

    [HttpGet("github")]
    public IActionResult RedirectToGitHub(
        [FromQuery] string? redirect_uri,
        [FromQuery] string? state,
        [FromQuery] string? code_challenge,
        [FromQuery] string? source)
    {
        var callbackBase = _config["App:BackendUrl"] ?? $"{Request.Scheme}://{Request.Host}";
        var callbackUri = $"{callbackBase}/auth/github/callback";

        var oauthState = state ?? Guid.NewGuid().ToString("N");

        if (!string.IsNullOrEmpty(redirect_uri))
            oauthState += $"|{redirect_uri}";
        if (!string.IsNullOrEmpty(source))
            oauthState += $"|source={source}";

        var url = _github.GetAuthorizationUrl(oauthState, callbackUri, code_challenge);
        return Redirect(url);
    }

    [HttpGet("github/callback")]
    public async Task<IActionResult> GitHubCallback([FromQuery] string? code, [FromQuery] string? state)
    {
        if (string.IsNullOrEmpty(code))
            return BadRequest(new { status = "error", message = "Missing authorization code" });

        if (string.IsNullOrEmpty(state))
            return BadRequest(new { status = "error", message = "Missing state parameter" });

        var stateParts = state.Split('|');
        var redirectUri = stateParts.Length > 1 ? stateParts[1] : null;
        var isWebSource = state.Contains("source=web");
        var isCliSource = state.Contains("source=cli");

        if (isCliSource && !string.IsNullOrEmpty(redirectUri))
        {
            var sep = redirectUri.Contains('?') ? '&' : '?';
            return Redirect($"{redirectUri}{sep}code={code}&state={stateParts[0]}");
        }

        var callbackBase = _config["App:BackendUrl"] ?? $"{Request.Scheme}://{Request.Host}";
        var callbackUri = $"{callbackBase}/auth/github/callback";

        var ghToken = await _github.ExchangeCodeForToken(code, callbackUri);
        if (ghToken == null)
            return StatusCode(502, new { status = "error", message = "Failed to exchange code with GitHub" });

        var ghUser = await _github.GetUserInfo(ghToken);
        if (ghUser == null)
            return StatusCode(502, new { status = "error", message = "Failed to get user info from GitHub" });

        var email = ghUser.Email ?? await _github.GetUserEmail(ghToken);

        var user = await FindOrCreateUser(ghUser, email);
        if (user == null)
            return StatusCode(403, new { status = "error", message = "Account is deactivated" });

        var accessToken = _tokens.GenerateAccessToken(user);
        var refreshToken = await _tokens.GenerateRefreshToken(user);

        if (isWebSource && !string.IsNullOrEmpty(redirectUri))
        {
            var sep = redirectUri.Contains('?') ? '&' : '?';
            return Redirect($"{redirectUri}{sep}access_token={accessToken}&refresh_token={refreshToken.Token}&username={user.Username}");
        }

        if (!string.IsNullOrEmpty(redirectUri) && !redirectUri.StartsWith("source="))
        {
            var separator = redirectUri.Contains('?') ? '&' : '?';
            return Redirect($"{redirectUri}{separator}access_token={accessToken}&refresh_token={refreshToken.Token}&username={user.Username}");
        }

        return Ok(new
        {
            status = "success",
            access_token = accessToken,
            refresh_token = refreshToken.Token,
            user = new
            {
                id = user.Id,
                username = user.Username,
                email = user.Email,
                avatar_url = user.AvatarUrl,
                role = user.Role
            }
        });
    }

    [HttpPost("token/exchange")]
    public async Task<IActionResult> ExchangeToken([FromBody] TokenExchangeRequest request)
    {
        if (string.IsNullOrEmpty(request.Code))
            return BadRequest(new { status = "error", message = "Missing authorization code" });

        var callbackBase = _config["App:BackendUrl"] ?? $"{Request.Scheme}://{Request.Host}";
        var callbackUri = $"{callbackBase}/auth/github/callback";

        var ghToken = await _github.ExchangeCodeForToken(request.Code, request.RedirectUri ?? callbackUri, request.CodeVerifier);
        if (ghToken == null)
            return StatusCode(502, new { status = "error", message = "Failed to exchange code with GitHub" });

        var ghUser = await _github.GetUserInfo(ghToken);
        if (ghUser == null)
            return StatusCode(502, new { status = "error", message = "Failed to get user info from GitHub" });

        var email = ghUser.Email ?? await _github.GetUserEmail(ghToken);

        var user = await FindOrCreateUser(ghUser, email);
        if (user == null)
            return StatusCode(403, new { status = "error", message = "Account is deactivated" });

        var accessToken = _tokens.GenerateAccessToken(user);
        var refreshToken = await _tokens.GenerateRefreshToken(user);

        return Ok(new
        {
            status = "success",
            access_token = accessToken,
            refresh_token = refreshToken.Token,
            user = new
            {
                id = user.Id,
                username = user.Username,
                email = user.Email,
                avatar_url = user.AvatarUrl,
                role = user.Role
            }
        });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest? request = null)
    {
        var tokenValue = request?.RefreshToken;

        if (string.IsNullOrEmpty(tokenValue))
        {
            tokenValue = Request.Cookies["refresh_token"];
        }

        if (string.IsNullOrEmpty(tokenValue))
            return BadRequest(new { status = "error", message = "Missing refresh token" });

        var result = await _tokens.RefreshTokens(tokenValue);
        if (result == null)
            return Unauthorized(new { status = "error", message = "Invalid or expired refresh token" });

        var (accessToken, refreshToken) = result.Value;

        if (Request.Cookies.ContainsKey("access_token"))
        {
            SetAuthCookies(accessToken, refreshToken.Token);
        }

        return Ok(new
        {
            status = "success",
            access_token = accessToken,
            refresh_token = refreshToken.Token
        });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest? request = null)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (userId == null)
            return Unauthorized(new { status = "error", message = "Authentication required" });

        if (!string.IsNullOrEmpty(request?.RefreshToken))
        {
            await _tokens.RevokeToken(request.RefreshToken);
        }

        await _tokens.RevokeAllUserTokens(userId);

        Response.Cookies.Delete("access_token");
        Response.Cookies.Delete("refresh_token");
        Response.Cookies.Delete("csrf_token");

        return Ok(new { status = "success", message = "Logged out successfully" });
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetCurrentUser()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId == null)
            return Unauthorized(new { status = "error", message = "Authentication required" });

        var user = await _db.Users.FindAsync(userId);
        if (user == null)
            return NotFound(new { status = "error", message = "User not found" });

        if (!user.IsActive)
            return StatusCode(403, new { status = "error", message = "Account is deactivated" });

        return Ok(new
        {
            status = "success",
            data = new
            {
                id = user.Id,
                username = user.Username,
                email = user.Email,
                avatar_url = user.AvatarUrl,
                role = user.Role,
                is_active = user.IsActive,
                last_login_at = user.LastLoginAt?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                created_at = user.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
            }
        });
    }

    private async Task<User?> FindOrCreateUser(GitHubUser ghUser, string? email)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.GitHubId == ghUser.Id.ToString());

        if (user == null)
        {
            var anyUsersExist = await _db.Users.AnyAsync();

            user = new User
            {
                Id = UuidV7Generator.Create().ToString(),
                GitHubId = ghUser.Id.ToString(),
                Username = ghUser.Login,
                Email = email,
                AvatarUrl = ghUser.AvatarUrl,
                Role = anyUsersExist ? "analyst" : "admin",
                IsActive = true,
                LastLoginAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            _db.Users.Add(user);
        }
        else
        {
            user.Username = ghUser.Login;
            user.Email = email ?? user.Email;
            user.AvatarUrl = ghUser.AvatarUrl ?? user.AvatarUrl;
            user.LastLoginAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();

        if (!user.IsActive)
            return null;

        return user;
    }

    private void SetAuthCookies(string accessToken, string refreshToken)
    {
        Response.Cookies.Append("access_token", accessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = TimeSpan.FromMinutes(3)
        });

        Response.Cookies.Append("refresh_token", refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = TimeSpan.FromMinutes(5)
        });

        var csrfToken = Guid.NewGuid().ToString("N");
        Response.Cookies.Append("csrf_token", csrfToken, new CookieOptions
        {
            HttpOnly = false,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = TimeSpan.FromMinutes(5)
        });
    }
}

public class TokenExchangeRequest
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("code_verifier")]
    public string? CodeVerifier { get; set; }

    [JsonPropertyName("redirect_uri")]
    public string? RedirectUri { get; set; }
}

public class RefreshRequest
{
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }
}

public class LogoutRequest
{
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }
}
