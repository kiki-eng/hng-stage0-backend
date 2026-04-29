using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;

namespace HngStageZeroClean.Middleware;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RequireRoleAttribute : Attribute, IAuthorizationFilter
{
    private readonly string[] _roles;

    public RequireRoleAttribute(params string[] roles)
    {
        _roles = roles;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;

        if (!user.Identity?.IsAuthenticated ?? true)
        {
            context.Result = new JsonResult(new { status = "error", message = "Authentication required" })
            {
                StatusCode = 401
            };
            return;
        }

        var role = user.FindFirst(ClaimTypes.Role)?.Value;

        if (role == null || !_roles.Contains(role))
        {
            context.Result = new JsonResult(new { status = "error", message = "Insufficient permissions" })
            {
                StatusCode = 403
            };
        }
    }
}
