using Microsoft.EntityFrameworkCore;
using SecureChatServer.Data.Entities;

namespace SecureChatServer.Data;

/// <summary>
/// Database context for the SecureChat application.
/// </summary>
public sealed class SecureChatDbContext : DbContext
{
    public SecureChatDbContext(DbContextOptions<SecureChatDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Users table.
    /// </summary>
    public DbSet<UserEntity> Users => Set<UserEntity>();

    /// <summary>
    /// Messages table (stores encrypted messages only).
    /// </summary>
    public DbSet<MessageEntity> Messages => Set<MessageEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure User entity
        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.Username)
                  .IsUnique();

            entity.Property(e => e.Username)
                  .HasMaxLength(50)
                  .IsRequired();

            entity.Property(e => e.PasswordHash)
                  .HasMaxLength(256);

            entity.Property(e => e.ConnectionId)
                  .HasMaxLength(100);
        });

        // Configure Message entity
        modelBuilder.Entity<MessageEntity>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.MessageId)
                  .IsUnique();

            entity.Property(e => e.MessageId)
                  .HasMaxLength(36)
                  .IsRequired();

            entity.Property(e => e.Ciphertext)
                  .IsRequired();

            entity.Property(e => e.IV)
                  .HasMaxLength(32)
                  .IsRequired();

            // Configure relationships
            entity.HasOne(e => e.Sender)
                  .WithMany(u => u.SentMessages)
                  .HasForeignKey(e => e.SenderId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Recipient)
                  .WithMany(u => u.ReceivedMessages)
                  .HasForeignKey(e => e.RecipientId)
                  .OnDelete(DeleteBehavior.Restrict);

            // Index for querying messages between users
            entity.HasIndex(e => new { e.SenderId, e.RecipientId });
            entity.HasIndex(e => e.Timestamp);
        });
    }
}
