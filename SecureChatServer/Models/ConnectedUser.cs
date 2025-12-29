namespace SecureChatServer.Models;

/// <summary>
/// Represents a connected user in the chat system.
/// </summary>
public sealed class ConnectedUser
{
    /// <summary>
    /// The SignalR connection ID for this user.
    /// </summary>
    public required string ConnectionId { get; init; }

    /// <summary>
    /// The username chosen by the user.
    /// </summary>
    public required string Username { get; init; }

    /// <summary>
    /// UTC timestamp when the user connected.
    /// </summary>
    public required DateTime ConnectedAt { get; init; }
}
