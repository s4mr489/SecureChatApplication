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
    /// Base64-encoded ciphertext (AES-CBC encrypted message).
    /// </summary>
    public required string Ciphertext { get; init; }

    /// <summary>
    /// Base64-encoded initialization vector (IV) for AES-CBC (16 bytes).
    /// Must be random and unique for each message with the same key.
    /// </summary>
    public required string IV { get; init; }

    /// <summary>
    /// UTC timestamp when the message was sent.
    /// </summary>
    public required DateTime Timestamp { get; init; }
}
