using System.ComponentModel;

namespace SecureChatApplication.Models;

/// <summary>
/// Represents a chat message in the UI (decrypted).
/// </summary>
public sealed class ChatMessage : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

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
    /// Whether the message was loaded from encrypted history storage.
    /// </summary>
    public bool IsFromHistory { get; init; }

    private bool _isDelivered;

    /// <summary>
    /// Whether the message has been delivered to the server.
    /// </summary>
    public bool IsDelivered
    {
        get => _isDelivered;
        set
        {
            if (_isDelivered != value)
            {
                _isDelivered = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDelivered)));
            }
        }
    }

    /// <summary>
    /// Message type: 0 = Text, 1 = Image, 2 = File.
    /// </summary>
    public int MessageType { get; init; }

    /// <summary>
    /// Original filename for image/file messages.
    /// </summary>
    public string? FileName { get; init; }

    /// <summary>
    /// Decrypted raw bytes for image/file messages.
    /// </summary>
    public byte[]? MediaBytes { get; init; }

    /// <summary>Whether this is a media (image or file) message.</summary>
    public bool IsMedia => MessageType != 0;

    /// <summary>Whether this is an inline image message.</summary>
    public bool IsImage => MessageType == 1;

    /// <summary>Whether this is a file attachment message.</summary>
    public bool IsFile => MessageType == 2;
}
