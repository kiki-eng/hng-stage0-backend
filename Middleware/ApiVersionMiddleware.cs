using System.Text.Json;

namespace HngStageZeroClean.Middleware;

public class ApiVersionMiddleware
{
    private readonly RequestDelegate _next;

    public ApiVersionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api/profiles"))
        {
            if (!context.Request.Headers.TryGetValue("X-API-Version", out var version) ||
                string.IsNullOrWhiteSpace(version))
            {
                context.Response.StatusCode = 400;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    status = "error",
                    message = "API version header required"
                }));
                return;
            }
        }

        await _next(context);
    }
}
