namespace SecureChatServer.Security;

public sealed record SecurityEventLog(
    DateTime Timestamp,
    string EventType,
    string? Username,
    string? IpAddress,
    bool IsSuccessful,
    string Details);
