using System.Text.Json;
using HngStageZeroClean.Data;
using HngStageZeroClean.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HngStageZeroClean.Helpers;

namespace HngStageZeroClean.Controllers;

[ApiController]
[Route("api/profiles")]
public class ProfilesController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AppDbContext _db;

    public ProfilesController(IHttpClientFactory httpClientFactory, AppDbContext db)
    {
        _httpClientFactory = httpClientFactory;
        _db = db;
    }

    [HttpPost]
    public async Task<IActionResult> CreateProfile([FromBody] CreateProfileRequest? request)
    {
        if (request == null || request.Name == null)
        {
            return UnprocessableEntity(new
            {
                status = "error",
                message = "Invalid type"
            });
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new
            {
                status = "error",
                message = "Missing or empty name"
            });
        }

        var normalizedName = request.Name.Trim().ToLower();

        var existingProfile = await _db.Profiles
            .FirstOrDefaultAsync(p => p.Name.ToLower() == normalizedName);

        if (existingProfile != null)
        {
            return Ok(new
            {
                status = "success",
                message = "Profile already exists",
                data = ToDetailResponse(existingProfile)
            });
        }

        try
        {
            var client = _httpClientFactory.CreateClient();

            var genderTask = client.GetAsync($"https://api.genderize.io?name={Uri.EscapeDataString(normalizedName)}");
            var ageTask = client.GetAsync($"https://api.agify.io?name={Uri.EscapeDataString(normalizedName)}");
            var countryTask = client.GetAsync($"https://api.nationalize.io?name={Uri.EscapeDataString(normalizedName)}");

            await Task.WhenAll(genderTask, ageTask, countryTask);

            var genderResponse = await genderTask;
            var ageResponse = await ageTask;
            var countryResponse = await countryTask;

            if (!genderResponse.IsSuccessStatusCode)
                return StatusCode(502, new { status = "error", message = "Genderize returned an invalid response" });

            if (!ageResponse.IsSuccessStatusCode)
                return StatusCode(502, new { status = "error", message = "Agify returned an invalid response" });

            if (!countryResponse.IsSuccessStatusCode)
                return StatusCode(502, new { status = "error", message = "Nationalize returned an invalid response" });

            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var genderJson = await genderResponse.Content.ReadAsStringAsync();
            var ageJson = await ageResponse.Content.ReadAsStringAsync();
            var countryJson = await countryResponse.Content.ReadAsStringAsync();

            var genderData = JsonSerializer.Deserialize<GenderizeResponse>(genderJson, jsonOptions);
            var ageData = JsonSerializer.Deserialize<AgifyResponse>(ageJson, jsonOptions);
            var countryData = JsonSerializer.Deserialize<NationalizeResponse>(countryJson, jsonOptions);

            if (genderData == null || genderData.Gender == null || genderData.Count == 0)
                return StatusCode(502, new { status = "error", message = "Genderize returned an invalid response" });

            if (ageData == null || ageData.Age == null)
                return StatusCode(502, new { status = "error", message = "Agify returned an invalid response" });

            if (countryData == null || countryData.Country == null || !countryData.Country.Any())
                return StatusCode(502, new { status = "error", message = "Nationalize returned an invalid response" });

            var bestCountry = countryData.Country
                .OrderByDescending(c => c.Probability)
                .First();

            var profile = new Profile
            {
                Id = UuidV7Generator.Create().ToString(),
                Name = normalizedName,
                Gender = genderData.Gender,
                GenderProbability = genderData.Probability,
                SampleSize = genderData.Count,
                Age = ageData.Age.Value,
                AgeGroup = GetAgeGroup(ageData.Age.Value),
                CountryId = bestCountry.Country_Id,
                CountryProbability = bestCountry.Probability,
                CreatedAt = DateTime.UtcNow
            };

            _db.Profiles.Add(profile);
            await _db.SaveChangesAsync();

            return StatusCode(201, new
            {
                status = "success",
                data = ToDetailResponse(profile)
            });
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new
            {
                status = "error",
                message = "Upstream or server failure"
            });
        }
        catch
        {
            return StatusCode(500, new
            {
                status = "error",
                message = "Upstream or server failure"
            });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetProfileById(string id)
    {
        var profile = await _db.Profiles.FindAsync(id);

        if (profile == null)
        {
            return NotFound(new
            {
                status = "error",
                message = "Profile not found"
            });
        }

        return Ok(new
        {
            status = "success",
            data = ToDetailResponse(profile)
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetProfiles([FromQuery] string? gender, [FromQuery] string? country_id, [FromQuery] string? age_group)
    {
        var query = _db.Profiles.AsQueryable();

        if (!string.IsNullOrWhiteSpace(gender))
        {
            var value = gender.Trim().ToLower();
            query = query.Where(p => p.Gender.ToLower() == value);
        }

        if (!string.IsNullOrWhiteSpace(country_id))
        {
            var value = country_id.Trim().ToUpper();
            query = query.Where(p => p.CountryId.ToUpper() == value);
        }

        if (!string.IsNullOrWhiteSpace(age_group))
        {
            var value = age_group.Trim().ToLower();
            query = query.Where(p => p.AgeGroup.ToLower() == value);
        }

        var profiles = await query
            .OrderBy(p => p.Name)
            .ToListAsync();

        return Ok(new
        {
            status = "success",
            count = profiles.Count,
            data = profiles.Select(p => new
            {
                id = p.Id,
                name = p.Name,
                gender = p.Gender,
                age = p.Age,
                age_group = p.AgeGroup,
                country_id = p.CountryId
            })
        });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteProfile(string id)
    {
        var profile = await _db.Profiles.FindAsync(id);

        if (profile == null)
        {
            return NotFound(new
            {
                status = "error",
                message = "Profile not found"
            });
        }

        _db.Profiles.Remove(profile);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    private static string GetAgeGroup(int age)
    {
        if (age <= 12) return "child";
        if (age <= 19) return "teenager";
        if (age <= 59) return "adult";
        return "senior";
    }

    private static object ToDetailResponse(Profile p) => new
    {
        id = p.Id,
        name = p.Name,
        gender = p.Gender,
        gender_probability = p.GenderProbability,
        sample_size = p.SampleSize,
        age = p.Age,
        age_group = p.AgeGroup,
        country_id = p.CountryId,
        country_probability = p.CountryProbability,
        created_at = p.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
    };
}