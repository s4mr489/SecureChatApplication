namespace SecureChatApplication.Models;

/// <summary>
/// DTO matching the server's SecurityEventLog record.
/// </summary>
public sealed class SecurityLogEntry
{
    public DateTime Timestamp { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string? Username { get; init; }
    public string? IpAddress { get; init; }
    public bool IsSuccessful { get; init; }
    public string Details { get; init; } = string.Empty;
}
