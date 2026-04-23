using HngStageZeroClean.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Use a simple SQLite path for Railway
var connectionString = "Data Source=profiles.db";

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlite(connectionString);
});

var app = builder.Build();

// Create DB and seed once
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    db.Database.EnsureCreated();

    try
    {
        if (!db.Profiles.AsNoTracking().Any())
        {
            await Seeder.SeedProfiles(db);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("SEED ERROR: " + ex.ToString());
    }
}

// Temporary debug endpoint
app.MapGet("/debug", (AppDbContext db) =>
{
    try
    {
        return Results.Ok(new
        {
            status = "success",
            count = db.Profiles.Count()
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString(), statusCode: 500);
    }
});

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("AllowAll");

app.MapControllers();

app.Run();