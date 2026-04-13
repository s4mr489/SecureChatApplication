namespace SecureChatApplication.Models;

/// <summary>
/// DTO matching the server's /security/dashboard response.
/// </summary>
public sealed class DashboardSummary
{
    public List<string> ActiveUsers { get; init; } = [];
    public int AlertCount { get; init; }
    public List<SecurityAlertEntry> RecentAlerts { get; init; } = [];
}
