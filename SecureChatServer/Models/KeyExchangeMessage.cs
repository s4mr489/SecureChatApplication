namespace SecureChatServer.Models;

/// <summary>
/// Represents an ECDH public key exchange message.
/// Used to establish shared secrets between clients without the server knowing the secret.
/// </summary>
public sealed class KeyExchangeMessage
{
    /// <summary>
    /// Stable user identifier of the sender.
    /// </summary>
    public required string SenderUserId { get; init; }

    /// <summary>
    /// Username of the client sending their public key.
    /// </summary>
    public required string SenderUsername { get; init; }

    /// <summary>
    /// Username of the intended recipient of the public key.
    /// </summary>
    public required string RecipientUsername { get; init; }

    /// <summary>
    /// Base64-encoded ECDH public key (X.509 SubjectPublicKeyInfo format).
    /// </summary>
    public required string PublicKey { get; init; }

    /// <summary>
    /// Base64-encoded SHA-256 fingerprint of the provided public key.
    /// </summary>
    public required string PublicKeyFingerprint { get; init; }

    /// <summary>
    /// UTC timestamp of the key exchange request.
    /// </summary>
    public required DateTime Timestamp { get; init; }
}
