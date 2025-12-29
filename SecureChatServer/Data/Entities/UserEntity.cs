namespace SecureChatServer.Data.Entities;

/// <summary>
/// Represents a user stored in the database.
/// </summary>
public sealed class UserEntity
{
    public int Id { get; set; }

    /// <summary>
    /// Unique username for the user.
    /// </summary>
    public required string Username { get; set; }

    /// <summary>
    /// Hashed password for authentication.
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <summary>
    /// When the user account was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last time the user logged in.
    /// </summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Whether the user is currently online.
    /// </summary>
    public bool IsOnline { get; set; }

    /// <summary>
    /// Current SignalR connection ID (null if offline).
    /// </summary>
    public string? ConnectionId { get; set; }

    /// <summary>
    /// Messages sent by this user.
    /// </summary>
    public ICollection<MessageEntity> SentMessages { get; set; } = new List<MessageEntity>();

    /// <summary>
    /// Messages received by this user.
    /// </summary>
    public ICollection<MessageEntity> ReceivedMessages { get; set; } = new List<MessageEntity>();
}
