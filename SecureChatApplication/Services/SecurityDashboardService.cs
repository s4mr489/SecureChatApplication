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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public void SetBaseUrl(string serverBaseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverBaseUrl);
        var trimmed = serverBaseUrl.TrimEnd('/') + "/";
        _httpClient.BaseAddress = new Uri(trimmed);
    }

    public async Task<List<SecurityLogEntry>> GetLogsAsync(CancellationToken ct = default)
    {
        var json = await _httpClient.GetStringAsync("security/logs", ct);
        return JsonSerializer.Deserialize<List<SecurityLogEntry>>(json, JsonOptions) ?? [];
    }

    public async Task<List<SecurityAlertEntry>> GetAlertsAsync(CancellationToken ct = default)
    {
        var json = await _httpClient.GetStringAsync("security/alerts", ct);
        return JsonSerializer.Deserialize<List<SecurityAlertEntry>>(json, JsonOptions) ?? [];
    }

    public async Task<DashboardSummary> GetDashboardAsync(CancellationToken ct = default)
    {
        var json = await _httpClient.GetStringAsync("security/dashboard", ct);
        return JsonSerializer.Deserialize<DashboardSummary>(json, JsonOptions) ?? new DashboardSummary();
    }

    public async Task<string> SimulateAttackAsync(string attackType, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attackType);

        var response = await _httpClient.PostAsync($"security/simulate/{attackType}", content: null, ct);

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
