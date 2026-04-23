using System.Text.Json;
using System.Text.RegularExpressions;
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

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

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
                Age = ageData.Age.Value,
                AgeGroup = GetAgeGroup(ageData.Age.Value),
                CountryId = bestCountry.Country_Id,
                CountryName = GetCountryName(bestCountry.Country_Id),
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
    public async Task<IActionResult> GetProfiles(
        [FromQuery] string? gender,
        [FromQuery] string? age_group,
        [FromQuery] string? country_id,
        [FromQuery] int? min_age,
        [FromQuery] int? max_age,
        [FromQuery] double? min_gender_probability,
        [FromQuery] double? min_country_probability,
        [FromQuery] string? sort_by,
        [FromQuery] string? order = "asc",
        [FromQuery] int page = 1,
        [FromQuery] int limit = 10)
    {
        if (page < 1 || limit < 1 || limit > 50)
        {
            return BadRequest(new
            {
                status = "error",
                message = "Invalid query parameters"
            });
        }

        if (min_age.HasValue && max_age.HasValue && min_age > max_age)
        {
            return BadRequest(new
            {
                status = "error",
                message = "Invalid query parameters"
            });
        }

        if (!string.IsNullOrWhiteSpace(sort_by) &&
            sort_by.ToLower() != "age" &&
            sort_by.ToLower() != "created_at" &&
            sort_by.ToLower() != "gender_probability")
        {
            return BadRequest(new
            {
                status = "error",
                message = "Invalid query parameters"
            });
        }

        if (!string.IsNullOrWhiteSpace(order) &&
            order.ToLower() != "asc" &&
            order.ToLower() != "desc")
        {
            return BadRequest(new
            {
                status = "error",
                message = "Invalid query parameters"
            });
        }

        var query = _db.Profiles.AsQueryable();

        if (!string.IsNullOrWhiteSpace(gender))
            query = query.Where(p => p.Gender.ToLower() == gender.ToLower());

        if (!string.IsNullOrWhiteSpace(age_group))
            query = query.Where(p => p.AgeGroup.ToLower() == age_group.ToLower());

        if (!string.IsNullOrWhiteSpace(country_id))
            query = query.Where(p => p.CountryId.ToUpper() == country_id.ToUpper());

        if (min_age.HasValue)
            query = query.Where(p => p.Age >= min_age.Value);

        if (max_age.HasValue)
            query = query.Where(p => p.Age <= max_age.Value);

        if (min_gender_probability.HasValue)
            query = query.Where(p => p.GenderProbability >= min_gender_probability.Value);

        if (min_country_probability.HasValue)
            query = query.Where(p => p.CountryProbability >= min_country_probability.Value);

        var sortField = sort_by?.ToLower();
        var sortOrder = order?.ToLower() ?? "asc";

        query = (sortField, sortOrder) switch
        {
            ("age", "desc") => query.OrderByDescending(p => p.Age),
            ("age", _) => query.OrderBy(p => p.Age),

            ("created_at", "desc") => query.OrderByDescending(p => p.CreatedAt),
            ("created_at", _) => query.OrderBy(p => p.CreatedAt),

            ("gender_probability", "desc") => query.OrderByDescending(p => p.GenderProbability),
            ("gender_probability", _) => query.OrderBy(p => p.GenderProbability),

            _ => query.OrderBy(p => p.Name)
        };

        var totalCount = await query.CountAsync();

        var result = await query
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(p => new
            {
                id = p.Id,
                name = p.Name,
                gender = p.Gender,
                gender_probability = p.GenderProbability,
                age = p.Age,
                age_group = p.AgeGroup,
                country_id = p.CountryId,
                country_name = p.CountryName,
                country_probability = p.CountryProbability,
                created_at = p.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
            })
            .ToListAsync();

        return Ok(new
        {
            status = "success",
            page,
            limit,
            total = totalCount,
            data = result
        });
    }

    [HttpGet("search")]
    public async Task<IActionResult> SearchProfiles(
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return BadRequest(new
            {
                status = "error",
                message = "Missing or empty parameter"
            });
        }

        if (page < 1 || limit < 1 || limit > 50)
        {
            return BadRequest(new
            {
                status = "error",
                message = "Invalid query parameters"
            });
        }

        q = q.Trim().ToLower();

        string? gender = null;
        string? ageGroup = null;
        string? countryId = null;
        int? minAge = null;
        int? maxAge = null;

        if (q.Contains("female") || q.Contains("females"))
            gender = "female";
        else if (q.Contains("male") || q.Contains("males"))
            gender = "male";

        if (q.Contains("young"))
        {
            minAge = 16;
            maxAge = 24;
        }

        if (q.Contains("child") || q.Contains("children"))
            ageGroup = "child";
        else if (q.Contains("teenager") || q.Contains("teenagers"))
            ageGroup = "teenager";
        else if (q.Contains("adult") || q.Contains("adults"))
            ageGroup = "adult";
        else if (q.Contains("senior") || q.Contains("seniors"))
            ageGroup = "senior";

        var aboveMatch = Regex.Match(q, @"above\s+(\d+)");
        if (aboveMatch.Success)
        {
            minAge = int.Parse(aboveMatch.Groups[1].Value);
        }

        if (q.Contains("nigeria"))
            countryId = "NG";
        else if (q.Contains("kenya"))
            countryId = "KE";
        else if (q.Contains("angola"))
            countryId = "AO";
        else if (q.Contains("uganda"))
            countryId = "UG";
        else if (q.Contains("cameroon"))
            countryId = "CM";
        else if (q.Contains("ghana"))
            countryId = "GH";

        var interpreted =
            gender != null ||
            ageGroup != null ||
            countryId != null ||
            minAge.HasValue ||
            maxAge.HasValue;

        if (!interpreted)
        {
            return UnprocessableEntity(new
            {
                status = "error",
                message = "Unable to interpret query"
            });
        }

        var query = _db.Profiles.AsQueryable();

        if (gender != null)
            query = query.Where(p => p.Gender.ToLower() == gender);

        if (ageGroup != null)
            query = query.Where(p => p.AgeGroup.ToLower() == ageGroup);

        if (countryId != null)
            query = query.Where(p => p.CountryId.ToUpper() == countryId);

        if (minAge.HasValue)
            query = query.Where(p => p.Age >= minAge.Value);

        if (maxAge.HasValue)
            query = query.Where(p => p.Age <= maxAge.Value);

        var totalCount = await query.CountAsync();

        var result = await query
            .OrderBy(p => p.Name)
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(p => new
            {
                id = p.Id,
                name = p.Name,
                gender = p.Gender,
                gender_probability = p.GenderProbability,
                age = p.Age,
                age_group = p.AgeGroup,
                country_id = p.CountryId,
                country_name = p.CountryName,
                country_probability = p.CountryProbability,
                created_at = p.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
            })
            .ToListAsync();

        return Ok(new
        {
            status = "success",
            page,
            limit,
            total = totalCount,
            data = result
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

    private static string GetCountryName(string countryId)
    {
        return countryId.ToUpper() switch
        {
            "NG" => "Nigeria",
            "KE" => "Kenya",
            "AO" => "Angola",
            "UG" => "Uganda",
            "CM" => "Cameroon",
            "GH" => "Ghana",
            _ => countryId
        };
    }

    private static object ToDetailResponse(Profile p) => new
    {
        id = p.Id,
        name = p.Name,
        gender = p.Gender,
        gender_probability = p.GenderProbability,
        age = p.Age,
        age_group = p.AgeGroup,
        country_id = p.CountryId,
        country_name = p.CountryName,
        country_probability = p.CountryProbability,
        created_at = p.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
    };
}