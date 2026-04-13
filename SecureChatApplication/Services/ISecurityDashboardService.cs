using SecureChatApplication.Models;

namespace SecureChatApplication.Services;

/// <summary>
/// Provides access to the server's security monitoring and simulation endpoints.
/// </summary>
public interface ISecurityDashboardService
{
    /// <summary>
    /// Configures the HTTP base URL (derived from the SignalR hub URL on login).
    /// </summary>
    void SetBaseUrl(string serverBaseUrl);

    Task<List<SecurityLogEntry>> GetLogsAsync(CancellationToken ct = default);
    Task<List<SecurityAlertEntry>> GetAlertsAsync(CancellationToken ct = default);
    Task<DashboardSummary> GetDashboardAsync(CancellationToken ct = default);
    Task<string> SimulateAttackAsync(string attackType, CancellationToken ct = default);
}
