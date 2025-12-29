namespace SecureChatServer.Data.Entities;

/// <summary>
/// Represents an encrypted message stored in the database.
/// Note: Only ciphertext is stored - server never sees plaintext (E2E encryption).
/// </summary>
public sealed class MessageEntity
{
    public int Id { get; set; }

    /// <summary>
    /// Unique message identifier (GUID).
    /// </summary>
    public required string MessageId { get; set; }

    /// <summary>
    /// Foreign key to the sender user.
    /// </summary>
    public int SenderId { get; set; }

    /// <summary>
    /// Navigation property to sender.
    /// </summary>
    public UserEntity Sender { get; set; } = null!;

    /// <summary>
    /// Foreign key to the recipient user.
    /// </summary>
    public int RecipientId { get; set; }

    /// <summary>
    /// Navigation property to recipient.
    /// </summary>
    public UserEntity Recipient { get; set; } = null!;

    /// <summary>
    /// Base64-encoded encrypted message content.
    /// Server cannot decrypt this - only clients with the shared key can.
    /// </summary>
    public required string Ciphertext { get; set; }

    /// <summary>
    /// Base64-encoded initialization vector for AES-GCM decryption.
    /// </summary>
    public required string IV { get; set; }

    /// <summary>
    /// UTC timestamp when the message was sent.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Whether the message has been delivered to the recipient.
    /// </summary>
    public bool IsDelivered { get; set; }

    /// <summary>
    /// When the message was delivered (null if not delivered yet).
    /// </summary>
    public DateTime? DeliveredAt { get; set; }
}
