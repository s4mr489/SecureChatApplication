using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SecureChatApplication.Services;

/// <summary>
/// Checks URLs against Kaspersky OpenTIP API.
/// </summary>
public sealed partial class SafeBrowsingService : IDisposable
{
    private const string ApiKey = "zsEIsWWvSKiZfWc5P28LiA==";
    private const string BaseUrl = "https://opentip.kaspersky.com/api/v1/search/url";

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
            return "⚠️ Invalid URL format";
        }

        try
        {
            var requestUrl = $"{BaseUrl}?request={Uri.EscapeDataString(url)}";

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Add("x-api-key", ApiKey);

            using var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"Kaspersky API error: {response.StatusCode} - {body}");
                return $"⚠️ Could not verify link safety: {url}";
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("Zone", out var zone))
            {
                var zoneValue = zone.GetString();
                if (string.Equals(zoneValue, "Green", StringComparison.OrdinalIgnoreCase))
                {
                    return "✅ Valid";
                }
                else
                {
                    return $"🚨 Not Valid";
                }
            }

            return "⚠️ Could not verify";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"URL safety check failed: {ex.Message}");
            return $"⚠️ Could not verify link safety: {url}";
        }
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

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
