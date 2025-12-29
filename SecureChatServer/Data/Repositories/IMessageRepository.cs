using SecureChatServer.Data.Entities;

namespace SecureChatServer.Data.Repositories;

/// <summary>
/// Repository interface for message operations.
/// Note: All messages are encrypted - server cannot read content.
/// </summary>
public interface IMessageRepository
{
    /// <summary>
    /// Stores an encrypted message.
    /// </summary>
    Task<MessageEntity> SaveMessageAsync(
        string messageId,
        int senderId,
        int recipientId,
        string ciphertext,
        string iv,
        DateTime timestamp);

    /// <summary>
    /// Marks a message as delivered.
    /// </summary>
    Task MarkAsDeliveredAsync(string messageId);

    /// <summary>
    /// Gets undelivered messages for a user (for offline message support).
    /// </summary>
    Task<List<MessageEntity>> GetUndeliveredMessagesAsync(int recipientId);

    /// <summary>
    /// Gets message history between two users.
    /// </summary>
    Task<List<MessageEntity>> GetMessageHistoryAsync(
        int userId1,
        int userId2,
        int take = 50,
        int skip = 0);

    /// <summary>
    /// Gets a message by its unique message ID.
    /// </summary>
    Task<MessageEntity?> GetByMessageIdAsync(string messageId);
}
