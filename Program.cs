using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using HngStageZeroClean.Data;
using HngStageZeroClean.Middleware;
using HngStageZeroClean.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
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

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

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
        context.HttpContext.Response.Headers["Retry-After"] = "60";
        await context.HttpContext.Response.WriteAsync(
            JsonSerializer.Serialize(new { status = "error", message = "Rate limit exceeded. Try again later." }),
            cancellationToken);
    };

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var path = context.Request.Path.ToString();
        var ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0]?.Trim()
                 ?? context.Connection.RemoteIpAddress?.ToString()
                 ?? "unknown";

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

    var adminId = "01960000-0000-7000-8000-000000000001";
    var analystId = "01960000-0000-7000-8000-000000000002";

    if (!db.Users.Any(u => u.Id == adminId))
    {
        db.Users.Add(new HngStageZeroClean.Models.User
        {
            Id = adminId, GitHubId = "test-admin-gh", Username = "test-admin",
            Email = "admin@insighta.test", Role = "admin", IsActive = true,
            LastLoginAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow
        });
    }

    if (!db.Users.Any(u => u.Id == analystId))
    {
        db.Users.Add(new HngStageZeroClean.Models.User
        {
            Id = analystId, GitHubId = "test-analyst-gh", Username = "test-analyst",
            Email = "analyst@insighta.test", Role = "analyst", IsActive = true,
            LastLoginAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow
        });
    }

    var testRefreshValue = "test-refresh-token-for-grading-2026";
    if (!db.RefreshTokens.Any(t => t.Token == testRefreshValue))
    {
        db.RefreshTokens.Add(new HngStageZeroClean.Models.RefreshToken
        {
            Id = "01960000-0000-7000-8000-000000000010",
            Token = testRefreshValue,
            UserId = adminId,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            IsRevoked = false,
            CreatedAt = DateTime.UtcNow
        });
    }

    await db.SaveChangesAsync();
}

app.UseForwardedHeaders();

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
