namespace SecureChatApplication.Models;

/// <summary>
/// Represents a chat partner with whom we have established a secure channel.
/// </summary>
public sealed class ChatPartner
{
    /// <summary>
    /// The username of the chat partner.
    /// </summary>
    public required string Username { get; init; }

    /// <summary>
    /// Whether a secure key exchange has been completed with this partner.
    /// </summary>
    public bool IsKeyExchangeComplete { get; set; }

    /// <summary>
    /// The derived AES-256 shared key for this partner (only set after key exchange).
    /// </summary>
    public byte[]? SharedKey { get; set; }

    /// <summary>
    /// Indicates if we initiated the key exchange (or are waiting for a response).
    /// </summary>
    public bool IsKeyExchangeInitiated { get; set; }
}
