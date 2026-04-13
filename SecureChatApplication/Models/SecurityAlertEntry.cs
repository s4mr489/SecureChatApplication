namespace SecureChatApplication.Models;

/// <summary>
/// DTO matching the server's SecurityAlert record.
/// </summary>
public sealed class SecurityAlertEntry
{
    public DateTime Timestamp { get; init; }
    public string AlertType { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public string? Username { get; init; }
    public string? IpAddress { get; init; }
}
