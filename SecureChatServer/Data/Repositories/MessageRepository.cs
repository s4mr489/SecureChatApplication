using Microsoft.EntityFrameworkCore;
using SecureChatServer.Data.Entities;

namespace SecureChatServer.Data.Repositories;

/// <summary>
/// Repository implementation for message operations.
/// </summary>
public sealed class MessageRepository : IMessageRepository
{
    private readonly SecureChatDbContext _context;

    public MessageRepository(SecureChatDbContext context)
    {
        _context = context;
    }

    public async Task<MessageEntity> SaveMessageAsync(
        string messageId,
        int senderId,
        int recipientId,
        string ciphertext,
        string iv,
        DateTime timestamp)
    {
        var message = new MessageEntity
        {
            MessageId = messageId,
            SenderId = senderId,
            RecipientId = recipientId,
            Ciphertext = ciphertext,
            IV = iv,
            Timestamp = timestamp,
            IsDelivered = false
        };

        _context.Messages.Add(message);
        await _context.SaveChangesAsync();

        return message;
    }

    public async Task MarkAsDeliveredAsync(string messageId)
    {
        var message = await _context.Messages
            .FirstOrDefaultAsync(m => m.MessageId == messageId);

        if (message != null)
        {
            message.IsDelivered = true;
            message.DeliveredAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<List<MessageEntity>> GetUndeliveredMessagesAsync(int recipientId)
    {
        return await _context.Messages
            .Where(m => m.RecipientId == recipientId && !m.IsDelivered)
            .OrderBy(m => m.Timestamp)
            .Include(m => m.Sender)
            .ToListAsync();
    }

    public async Task<List<MessageEntity>> GetMessageHistoryAsync(
        int userId1,
        int userId2,
        int take = 50,
        int skip = 0)
    {
        return await _context.Messages
            .Where(m =>
                (m.SenderId == userId1 && m.RecipientId == userId2) ||
                (m.SenderId == userId2 && m.RecipientId == userId1))
            .OrderByDescending(m => m.Timestamp)
            .Skip(skip)
            .Take(take)
            .Include(m => m.Sender)
            .Include(m => m.Recipient)
            .ToListAsync();
    }

    public async Task<MessageEntity?> GetByMessageIdAsync(string messageId)
    {
        return await _context.Messages
            .FirstOrDefaultAsync(m => m.MessageId == messageId);
    }
}
