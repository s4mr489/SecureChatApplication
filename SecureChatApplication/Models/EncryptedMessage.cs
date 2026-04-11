namespace SecureChatApplication.Models;

/// <summary>
/// Represents an encrypted message sent between clients.
/// The server never sees the plaintext - only this encrypted payload.
/// </summary>
public sealed class EncryptedMessage
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
    /// Username of the intended recipient.
    /// </summary>
    public required string RecipientUsername { get; init; }

    /// <summary>
    /// Base64-encoded ciphertext encrypted with AES-GCM.
    /// </summary>
    public required string Ciphertext { get; init; }

    /// <summary>
    /// Base64-encoded nonce for AES-GCM (12 bytes).
    /// </summary>
    public required string Nonce { get; init; }

    /// <summary>
    /// Base64-encoded authentication tag for AES-GCM (16 bytes).
    /// </summary>
    public required string Tag { get; init; }

    /// <summary>
    /// UTC timestamp when the message was sent.
    /// </summary>
    public required DateTime Timestamp { get; init; }
}
