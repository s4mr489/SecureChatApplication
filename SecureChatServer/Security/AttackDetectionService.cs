using System.Collections.Concurrent;

namespace SecureChatServer.Security;

public sealed class AttackDetectionService
{
    private readonly ConcurrentQueue<SecurityEventLog> _events = new();
    private readonly ConcurrentQueue<SecurityAlert> _alerts = new();
    private readonly TimeSpan _retention = TimeSpan.FromMinutes(30);

    public void LogEvent(string eventType, string? username, string? ipAddress, bool isSuccessful, string details)
    {
        var entry = new SecurityEventLog(DateTime.UtcNow, eventType, username, ipAddress, isSuccessful, details);
        _events.Enqueue(entry);
        Trim(_events, e => e.Timestamp);

        DetectBruteForce(username, ipAddress);
        DetectMessageFlood(username, ipAddress);
        DetectRapidReconnect(username, ipAddress);
        DetectSuspiciousKeyExchange(username, ipAddress);
    }

    public IReadOnlyList<SecurityEventLog> GetLogs(int take = 200)
    {
        return _events.Reverse().Take(take).ToList();
    }

    public IReadOnlyList<SecurityAlert> GetAlerts(int take = 200)
    {
        return _alerts.Reverse().Take(take).ToList();
    }

    public void SimulateBruteForce(string username, string ipAddress)
    {
        for (var i = 0; i < 8; i++)
        {
            LogEvent("JoinChat", username, ipAddress, false, "Simulated failed login/join attempt");
        }
    }

    public void SimulateMessageFlood(string username, string ipAddress)
    {
        for (var i = 0; i < 30; i++)
        {
            LogEvent("SendEncryptedMessage", username, ipAddress, true, "Simulated message flood event");
        }
    }

    public void SimulateFakeKeyExchange(string username, string ipAddress)
    {
        for (var i = 0; i < 10; i++)
        {
            LogEvent("InitiateKeyExchange", username, ipAddress, false, "Simulated fake key exchange attempt");
        }
    }

    private void DetectBruteForce(string? username, string? ipAddress)
    {
        var now = DateTime.UtcNow;
        var failedAttempts = _events.Count(e =>
            e.EventType == "JoinChat" &&
            !e.IsSuccessful &&
            e.IpAddress == ipAddress &&
            now - e.Timestamp <= TimeSpan.FromMinutes(2));

        if (failedAttempts >= 6)
        {
            AddAlert("BruteForce", "High", "Multiple failed join attempts detected.", username, ipAddress);
        }
    }

    private void DetectMessageFlood(string? username, string? ipAddress)
    {
        var now = DateTime.UtcNow;
        var sentMessages = _events.Count(e =>
            e.EventType == "SendEncryptedMessage" &&
            e.IsSuccessful &&
            e.Username == username &&
            now - e.Timestamp <= TimeSpan.FromSeconds(10));

        if (sentMessages >= 20)
        {
            AddAlert("MessageFlood", "High", "High message rate detected.", username, ipAddress);
        }
    }

    private void DetectRapidReconnect(string? username, string? ipAddress)
    {
        var now = DateTime.UtcNow;
        var reconnects = _events.Count(e =>
            e.EventType == "JoinChat" &&
            e.IsSuccessful &&
            e.Username == username &&
            now - e.Timestamp <= TimeSpan.FromMinutes(1));

        if (reconnects >= 5)
        {
            AddAlert("RapidReconnect", "Medium", "Rapid reconnect behavior detected.", username, ipAddress);
        }
    }

    private void DetectSuspiciousKeyExchange(string? username, string? ipAddress)
    {
        var now = DateTime.UtcNow;
        var invalidKeyExchanges = _events.Count(e =>
            (e.EventType == "InitiateKeyExchange" || e.EventType == "RespondToKeyExchange") &&
            !e.IsSuccessful &&
            e.Username == username &&
            now - e.Timestamp <= TimeSpan.FromMinutes(1));

        if (invalidKeyExchanges >= 5)
        {
            AddAlert("FakeKeyExchange", "High", "Suspicious key exchange behavior detected.", username, ipAddress);
        }
    }

    private void AddAlert(string alertType, string severity, string details, string? username, string? ipAddress)
    {
        var now = DateTime.UtcNow;
        var alreadyRaised = _alerts.Any(a =>
            a.AlertType == alertType &&
            a.Username == username &&
            a.IpAddress == ipAddress &&
            now - a.Timestamp <= TimeSpan.FromMinutes(2));

        if (!alreadyRaised)
        {
            _alerts.Enqueue(new SecurityAlert(now, alertType, severity, details, username, ipAddress));
            Trim(_alerts, a => a.Timestamp);
        }
    }

    private void Trim<T>(ConcurrentQueue<T> queue, Func<T, DateTime> timestampSelector)
    {
        while (queue.TryPeek(out var entry) && DateTime.UtcNow - timestampSelector(entry) > _retention)
        {
            queue.TryDequeue(out _);
        }
    }
}
