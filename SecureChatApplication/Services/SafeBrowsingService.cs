using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SecureChatApplication.Services;

/// <summary>
/// Checks URLs against Google Safe Browsing API v5.
/// </summary>
public sealed partial class SafeBrowsingService : IDisposable
{
    private const string ApiKey = "AIzaSyAcCJtSyvCgsFPBLemz7cHY_-JvguQDQJk";
    private const string BaseUrl = "https://safebrowsing.googleapis.com/v5/urls:search";

    private readonly HttpClient _httpClient = new();

    [GeneratedRegex(@"https?://[^\s""'<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    public static List<string> ExtractUrls(string text)
    {
        var matches = UrlPattern().Matches(text);
        return matches.Select(m => m.Value).Distinct().ToList();
    }

    public async Task<string> CheckUrlAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            return "Invalid URL format";
        }

        var baseUrl = "https://safebrowsing.googleapis.com/v5/urls:search";

        var queryParams = new Dictionary<string, string>
        {
            ["key"] = ApiKey,
            ["urls[]"] = url
        };

        var queryString = await new FormUrlEncodedContent(queryParams).ReadAsStringAsync();
        var requestUrl = $"{baseUrl}?{queryString}";

        using var response = await _httpClient.GetAsync(requestUrl);
        var body = await response.Content.ReadAsStringAsync();

        return $"Status: {(int)response.StatusCode}\nBody: {body}";
    }

    public async Task<List<string>> CheckAllUrlsAsync(string text)
    {
        var urls = ExtractUrls(text);
        if (urls.Count == 0) return [];

        var results = new List<string>();
        foreach (var url in urls)
        {
            var result = await CheckUrlAsync(url);
            results.Add(result);
        }

        return results;
    }

    private static string FormatThreatType(string threatType) => threatType switch
    {
        "MALWARE" => "Malware",
        "SOCIAL_ENGINEERING" => "Phishing/Social Engineering",
        "UNWANTED_SOFTWARE" => "Unwanted Software",
        "POTENTIALLY_HARMFUL_APPLICATION" => "Potentially Harmful App",
        _ => threatType
    };

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
