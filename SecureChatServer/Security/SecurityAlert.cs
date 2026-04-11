namespace SecureChatServer.Security;

public sealed record SecurityAlert(
    DateTime Timestamp,
    string AlertType,
    string Severity,
    string Details,
    string? Username,
    string? IpAddress);
