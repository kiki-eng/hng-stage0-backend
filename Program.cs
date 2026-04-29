using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using HngStageZeroClean.Data;
using HngStageZeroClean.Middleware;
using HngStageZeroClean.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var jwtSecret = builder.Configuration["Jwt:Secret"] ?? "insighta-labs-super-secret-key-change-in-prod-2026";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "insighta-labs";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "insighta-labs";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ClockSkew = TimeSpan.FromSeconds(30)
    };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            if (context.Request.Cookies.TryGetValue("access_token", out var cookieToken))
            {
                context.Token = cookieToken;
            }
            return Task.CompletedTask;
        },
        OnChallenge = context =>
        {
            context.HandleResponse();
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            var body = JsonSerializer.Serialize(new { status = "error", message = "Authentication required" });
            return context.Response.WriteAsync(body);
        },
        OnForbidden = context =>
        {
            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";
            var body = JsonSerializer.Serialize(new { status = "error", message = "Forbidden" });
            return context.Response.WriteAsync(body);
        }
    };
});

builder.Services.AddAuthorization();

builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<GitHubService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        context.HttpContext.Response.ContentType = "application/json";
        var retryAfter = "60";
        context.HttpContext.Response.Headers["Retry-After"] = retryAfter;
        await context.HttpContext.Response.WriteAsync(
            JsonSerializer.Serialize(new { status = "error", message = "Rate limit exceeded. Try again later." }),
            cancellationToken);
    };

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var path = context.Request.Path.ToString();
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (path.StartsWith("/auth"))
        {
            return RateLimitPartition.GetFixedWindowLimiter(
                $"auth:{ip}",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                });
        }

        var userId = context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? ip;
        return RateLimitPartition.GetFixedWindowLimiter(
            $"api:{userId}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });
});

var connectionString = "Data Source=profiles.db";
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlite(connectionString);
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    try
    {
        await Seeder.SeedProfiles(db);
    }
    catch (Exception ex)
    {
        Console.WriteLine("SEED ERROR: " + ex.ToString());
    }
}

app.Use(async (context, next) =>
{
    context.Response.Headers["Access-Control-Allow-Origin"] = "*";
    context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, PATCH, DELETE, OPTIONS";
    context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization, X-API-Version";

    if (context.Request.Method == "OPTIONS")
    {
        context.Response.StatusCode = 204;
        return;
    }

    await next();
});

app.UseMiddleware<RequestLoggingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRateLimiter();

app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<ApiVersionMiddleware>();

app.MapControllers();

app.Run();
