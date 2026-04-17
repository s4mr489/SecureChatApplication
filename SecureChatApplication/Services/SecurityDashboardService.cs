using SecureChatApplication.Models;
using System.Net.Http;
using System.Text.Json;

namespace SecureChatApplication.Services;

/// <summary>
/// Calls the server's /security/* REST endpoints to retrieve logs, alerts, and run simulations.
/// </summary>
public sealed class SecurityDashboardService : ISecurityDashboardService
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private string _baseUrl = string.Empty;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public void SetBaseUrl(string serverBaseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverBaseUrl);
        _baseUrl = serverBaseUrl.TrimEnd('/') + "/";
    }

    private string Url(string relative) => _baseUrl + relative;

    public async Task<List<SecurityLogEntry>> GetLogsAsync(CancellationToken ct = default)
    {
        var json = await _httpClient.GetStringAsync(Url("security/logs"), ct);
        return JsonSerializer.Deserialize<List<SecurityLogEntry>>(json, JsonOptions) ?? [];
    }

    public async Task<List<SecurityAlertEntry>> GetAlertsAsync(CancellationToken ct = default)
    {
        var json = await _httpClient.GetStringAsync(Url("security/alerts"), ct);
        return JsonSerializer.Deserialize<List<SecurityAlertEntry>>(json, JsonOptions) ?? [];
    }

    public async Task<DashboardSummary> GetDashboardAsync(CancellationToken ct = default)
    {
        var json = await _httpClient.GetStringAsync(Url("security/dashboard"), ct);
        return JsonSerializer.Deserialize<DashboardSummary>(json, JsonOptions) ?? new DashboardSummary();
    }

    public async Task<string> SimulateAttackAsync(string attackType, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attackType);

        var response = await _httpClient.PostAsync(Url($"security/simulate/{attackType}"), content: null, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            return $"Error ({response.StatusCode}): {errorBody}";
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<SimulationResult>(json, JsonOptions);
        return result?.Message ?? "Simulation executed.";
    }

    private sealed class SimulationResult
    {
        public string Message { get; init; } = string.Empty;
    }
}
