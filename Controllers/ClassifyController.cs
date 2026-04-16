using System.Text.Json;
using HngStageZeroClean.Models;
using Microsoft.AspNetCore.Mvc;

namespace HngStageZeroClean.Controllers;

[ApiController]
[Route("api")]
public class ClassifyController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ClassifyController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet("classify")]
    public async Task<IActionResult> Classify([FromQuery] string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new
            {
                status = "error",
                message = "Missing or empty name parameter"
            });
        }

        try
        {
            var client = _httpClientFactory.CreateClient();

            var response = await client.GetAsync(
                $"https://api.genderize.io/?name={Uri.EscapeDataString(name)}"
            );

            if (!response.IsSuccessStatusCode)
            {
                return StatusCode(502, new
                {
                    status = "error",
                    message = "Failed to fetch data from upstream service"
                });
            }

            var json = await response.Content.ReadAsStringAsync();

            var result = JsonSerializer.Deserialize<GenderizeResponse>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }
            );

            if (result == null)
            {
                return StatusCode(502, new
                {
                    status = "error",
                    message = "Failed to process upstream response"
                });
            }

            if (result.Gender == null || result.Count == 0)
            {
                return UnprocessableEntity(new
                {
                    status = "error",
                    message = "No prediction available for the provided name"
                });
            }

            var isConfident = result.Probability >= 0.7 && result.Count >= 100;

            return Ok(new
            {
                status = "success",
                data = new
                {
                    name = result.Name?.ToLower(),
                    gender = result.Gender,
                    probability = result.Probability,
                    sample_size = result.Count,
                    is_confident = isConfident,
                    processed_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                }
            });
        }
        catch
        {
            return StatusCode(500, new
            {
                status = "error",
                message = "Internal server error"
            });
        }
    }
}