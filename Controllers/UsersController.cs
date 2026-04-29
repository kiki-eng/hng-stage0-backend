using System.Security.Claims;
using HngStageZeroClean.Data;
using HngStageZeroClean.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HngStageZeroClean.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;

    public UsersController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId == null)
            return Unauthorized(new { status = "error", message = "Authentication required" });

        var user = await _db.Users.FindAsync(userId);
        if (user == null)
            return NotFound(new { status = "error", message = "User not found" });

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

    [HttpGet]
    [RequireRole("admin")]
    public async Task<IActionResult> ListUsers()
    {
        var users = await _db.Users.OrderByDescending(u => u.CreatedAt).ToListAsync();

        return Ok(new
        {
            status = "success",
            data = users.Select(u => new
            {
                id = u.Id,
                username = u.Username,
                email = u.Email,
                avatar_url = u.AvatarUrl,
                role = u.Role,
                is_active = u.IsActive,
                last_login_at = u.LastLoginAt?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                created_at = u.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
            })
        });
    }

    [HttpPatch("{id}/role")]
    [RequireRole("admin")]
    public async Task<IActionResult> UpdateRole(string id, [FromBody] UpdateRoleRequest request)
    {
        if (string.IsNullOrEmpty(request.Role) || (request.Role != "admin" && request.Role != "analyst"))
            return BadRequest(new { status = "error", message = "Role must be 'admin' or 'analyst'" });

        var user = await _db.Users.FindAsync(id);
        if (user == null)
            return NotFound(new { status = "error", message = "User not found" });

        user.Role = request.Role;
        await _db.SaveChangesAsync();

        return Ok(new
        {
            status = "success",
            data = new
            {
                id = user.Id,
                username = user.Username,
                role = user.Role
            }
        });
    }
}

public class UpdateRoleRequest
{
    public string Role { get; set; } = "";
}
