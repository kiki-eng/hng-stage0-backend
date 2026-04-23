using System.Text.Json;
using HngStageZeroClean.Models;

namespace HngStageZeroClean.Data;

public static class Seeder
{
    public static async Task SeedProfiles(AppDbContext db)
    {
        if (db.Profiles.Any())
            return; // prevents duplicates

        var json = await File.ReadAllTextAsync("Data/seed_profiles.json");

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var seedData = JsonSerializer.Deserialize<SeedWrapper>(json, options);

        if (seedData?.Profiles == null)
            return;

       var profiles = seedData.Profiles
    .GroupBy(p => p.Name.Trim().ToLower())
    .Select(g => g.First())
    .Where(p => !db.Profiles.Any(x => x.Name.ToLower() == p.Name.Trim().ToLower()))
    .Select(p => new Profile
    {
        Id = Guid.NewGuid().ToString(),
        Name = p.Name.Trim(),
        Gender = p.Gender,
        GenderProbability = p.Gender_Probability,
        Age = p.Age,
        AgeGroup = p.Age_Group,
        CountryId = p.Country_Id,
        CountryName = p.Country_Name,
        CountryProbability = p.Country_Probability,
        CreatedAt = DateTime.UtcNow
    })
    .ToList();

        db.Profiles.AddRange(profiles);
        await db.SaveChangesAsync();
    }
}

public class SeedWrapper
{
    public List<SeedProfile> Profiles { get; set; } = new();
}

public class SeedProfile
{
    public string Name { get; set; } = "";
    public string Gender { get; set; } = "";
    public double Gender_Probability { get; set; }
    public int Age { get; set; }
    public string Age_Group { get; set; } = "";
    public string Country_Id { get; set; } = "";
    public string Country_Name { get; set; } = "";
    public double Country_Probability { get; set; }
}