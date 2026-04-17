using SecureChatApplication.Models;
using System.IO;
using System.Text.Json;

namespace SecureChatApplication.Services;

/// <summary>
/// Persists decrypted chat messages locally so history survives across sessions
/// (server-side history is encrypted with ephemeral keys that change each session).
/// </summary>
public sealed class ChatHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _historyDirectory;

    public ChatHistoryService()
    {
        _historyDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SecureChatApplication", "History");

        Directory.CreateDirectory(_historyDirectory);
    }

    /// <summary>
    /// Returns the file path for chat history between two users.
    /// The key is sorted alphabetically so both sides map to the same file.
    /// </summary>
    private string GetFilePath(string currentUser, string partnerUser)
    {
        var users = new[] { currentUser, partnerUser };
        Array.Sort(users, StringComparer.OrdinalIgnoreCase);
        var safeKey = $"{SanitizeFileName(users[0])}_{SanitizeFileName(users[1])}.json";
        return Path.Combine(_historyDirectory, safeKey);
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    /// <summary>
    /// Loads all saved messages for the given conversation.
    /// </summary>
    public List<ChatHistoryEntry> LoadHistory(string currentUser, string partnerUser)
    {
        var path = GetFilePath(currentUser, partnerUser);
        if (!File.Exists(path)) return [];

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<ChatHistoryEntry>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Appends a single message to the local history file.
    /// </summary>
    public void SaveMessage(string currentUser, string partnerUser, ChatMessage message)
    {
        if (message.IsFromHistory) return; // don't re-save history entries

        var history = LoadHistory(currentUser, partnerUser);

        // Avoid duplicates
        if (history.Exists(h => h.MessageId == message.MessageId)) return;

        history.Add(new ChatHistoryEntry
        {
            MessageId = message.MessageId,
            SenderUsername = message.SenderUsername,
            Content = message.Content,
            Timestamp = message.Timestamp,
            MessageType = message.MessageType,
            FileName = message.FileName,
            MediaBytesBase64 = message.MediaBytes != null ? Convert.ToBase64String(message.MediaBytes) : null
        });

        var path = GetFilePath(currentUser, partnerUser);
        File.WriteAllText(path, JsonSerializer.Serialize(history, JsonOptions));
    }

    /// <summary>
    /// Converts stored history entries into ChatMessage objects for display.
    /// </summary>
    public List<ChatMessage> LoadAsChatMessages(string currentUser, string partnerUser)
    {
        var entries = LoadHistory(currentUser, partnerUser);
        return entries.Select(e => new ChatMessage
        {
            MessageId = e.MessageId,
            SenderUsername = e.SenderUsername,
            Content = e.Content,
            Timestamp = e.Timestamp,
            IsOwnMessage = e.SenderUsername == currentUser,
            IsDelivered = true,
            IsFromHistory = true,
            MessageType = e.MessageType,
            FileName = e.FileName,
            MediaBytes = e.MediaBytesBase64 != null ? Convert.FromBase64String(e.MediaBytesBase64) : null
        }).ToList();
    }

    /// <summary>
    /// Scans history files to find all partner usernames that the current user has chatted with.
    /// </summary>
    public List<string> GetKnownPartners(string currentUser)
    {
        var partners = new List<string>();
        var sanitizedCurrent = SanitizeFileName(currentUser);

        foreach (var file in Directory.GetFiles(_historyDirectory, "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var parts = name.Split('_', 2);
            if (parts.Length != 2) continue;

            if (string.Equals(parts[0], sanitizedCurrent, StringComparison.OrdinalIgnoreCase))
                partners.Add(parts[1]);
            else if (string.Equals(parts[1], sanitizedCurrent, StringComparison.OrdinalIgnoreCase))
                partners.Add(parts[0]);
        }

        return partners;
    }
}

/// <summary>
/// Serializable record for a single chat history entry.
/// </summary>
public sealed class ChatHistoryEntry
{
    public required string MessageId { get; set; }
    public required string SenderUsername { get; set; }
    public required string Content { get; set; }
    public DateTime Timestamp { get; set; }
    public int MessageType { get; set; }
    public string? FileName { get; set; }
    public string? MediaBytesBase64 { get; set; }
}
