namespace SecureChatApplication.Models;

/// <summary>
/// Represents a chat message in the UI (decrypted).
/// </summary>
public sealed class ChatMessage
{
    /// <summary>
    /// Unique identifier for the message.
    /// </summary>
    public required string MessageId { get; init; }

    /// <summary>
    /// Username of the message sender.
    /// </summary>
    public required string SenderUsername { get; init; }

    /// <summary>
    /// The decrypted plaintext message content.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// UTC timestamp when the message was sent.
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// Whether this message was sent by the current user.
    /// </summary>
    public required bool IsOwnMessage { get; init; }

    /// <summary>
    /// Whether the message has been delivered to the server.
    /// </summary>
    public bool IsDelivered { get; set; }
}
