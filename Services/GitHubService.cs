using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HngStageZeroClean.Services;

public class GitHubService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;

    public GitHubService(IHttpClientFactory httpClientFactory, IConfiguration config)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
    }

    public string ClientId => _config["GitHub:ClientId"] ?? "";
    public string ClientSecret => _config["GitHub:ClientSecret"] ?? "";

    public string GetAuthorizationUrl(string state, string redirectUri, string? codeChallenge = null)
    {
        var url = $"https://github.com/login/oauth/authorize?client_id={ClientId}&redirect_uri={Uri.EscapeDataString(redirectUri)}&state={state}&scope=read:user user:email";
        if (codeChallenge != null)
            url += $"&code_challenge={codeChallenge}&code_challenge_method=S256";
        return url;
    }

    public async Task<string?> ExchangeCodeForToken(string code, string redirectUri, string? codeVerifier = null)
    {
        var client = _httpClientFactory.CreateClient();
        var requestBody = new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = redirectUri
        };

        if (codeVerifier != null)
            requestBody["code_verifier"] = codeVerifier;

        var request = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token")
        {
            Content = new FormUrlEncodedContent(requestBody)
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<GitHubTokenResponse>(json);
        return result?.AccessToken;
    }

    public async Task<GitHubUser?> GetUserInfo(string accessToken)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("InsightaLabs/1.0");

        var response = await client.GetAsync("https://api.github.com/user");
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<GitHubUser>(json);
    }

    public async Task<string?> GetUserEmail(string accessToken)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("InsightaLabs/1.0");

        var response = await client.GetAsync("https://api.github.com/user/emails");
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        var emails = JsonSerializer.Deserialize<List<GitHubEmail>>(json);
        return emails?.FirstOrDefault(e => e.Primary)?.Email ?? emails?.FirstOrDefault()?.Email;
    }
}

public class GitHubTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }
}

public class GitHubUser
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("login")]
    public string Login { get; set; } = "";

    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }
}

public class GitHubEmail
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = "";

    [JsonPropertyName("primary")]
    public bool Primary { get; set; }

    [JsonPropertyName("verified")]
    public bool Verified { get; set; }
}
