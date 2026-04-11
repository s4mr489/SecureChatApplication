using Microsoft.AspNetCore.SignalR;
using SecureChatServer.Data.Repositories;
using SecureChatServer.Models;
using SecureChatServer.Security;
using System.Security.Cryptography;

namespace SecureChatServer.Hubs;

/// <summary>
/// SignalR Hub for secure chat messaging.
/// 
/// SECURITY MODEL:
/// - This hub only RELAYS encrypted messages and public keys
/// - The server NEVER sees plaintext messages
/// - The server CANNOT decrypt messages (no access to private keys)
/// - All cryptographic operations happen client-side
/// 
/// DATABASE:
/// - Users and encrypted messages are persisted to SQL Server
/// - Message history can be retrieved (still encrypted)
/// </summary>
public sealed class ChatHub : Hub
{
    private const int MaxCipherBytes = 16 * 1024;
    private const int MaxPublicKeyBytes = 1024;

    private readonly IUserRepository _userRepository;
    private readonly IMessageRepository _messageRepository;
    private readonly RateLimiterService _rateLimiter;
    private readonly AttackDetectionService _attackDetection;

    public ChatHub(
        IUserRepository userRepository,
        IMessageRepository messageRepository,
        RateLimiterService rateLimiter,
        AttackDetectionService attackDetection)
    {
        _userRepository = userRepository;
        _messageRepository = messageRepository;
        _rateLimiter = rateLimiter;
        _attackDetection = attackDetection;
    }

    /// <summary>
    /// Called when a user joins the chat with their username.
    /// </summary>
    /// <param name="username">The display name for the user.</param>
    public async Task JoinChat(string username)
    {
        var ipAddress = GetIpAddress();

        if (!_rateLimiter.IsAllowed("join-ip", ipAddress, 8, TimeSpan.FromMinutes(1)) ||
            !_rateLimiter.IsAllowed("join-user", username, 5, TimeSpan.FromMinutes(1)))
        {
            _attackDetection.LogEvent("JoinChat", username, ipAddress, false, "Rate limit exceeded");
            await Clients.Caller.SendAsync("Error", "Join rate limit exceeded. Please retry later.");
            return;
        }

        if (string.IsNullOrWhiteSpace(username) || username.Length is < 2 or > 20)
        {
            _attackDetection.LogEvent("JoinChat", username, ipAddress, false, "Invalid username format");
            await Clients.Caller.SendAsync("Error", "Username must be between 2 and 20 characters.");
            return;
        }

        // Check if username is already taken by an online user
        if (await _userRepository.IsUsernameTakenAsync(username))
        {
            _attackDetection.LogEvent("JoinChat", username, ipAddress, false, "Username already online");
            await Clients.Caller.SendAsync("Error", "Username is already taken.");
            return;
        }

        try
        {
            // Create or update user in database
            var user = await _userRepository.CreateOrUpdateOnJoinAsync(username, Context.ConnectionId);
            _attackDetection.LogEvent("JoinChat", username, ipAddress, true, "User joined successfully");

            // Notify all clients about the new user
            await Clients.All.SendAsync("UserJoined", username);

            // Send current online user list to the new user
            var userList = await _userRepository.GetOnlineUsernamesAsync();
            await Clients.Caller.SendAsync("UserList", userList);

            // Confirm successful join
            await Clients.Caller.SendAsync("JoinConfirmed", username);

            // Deliver any undelivered messages (offline message support)
            var undeliveredMessages = await _messageRepository.GetUndeliveredMessagesAsync(user.Id);
            foreach (var msg in undeliveredMessages)
            {
                await Clients.Caller.SendAsync("EncryptedMessageReceived", new EncryptedMessage
                {
                    MessageId = msg.MessageId,
                    SenderUsername = msg.Sender.Username,
                    RecipientUsername = username,
                    Ciphertext = msg.Ciphertext,
                    Nonce = msg.Nonce,
                    Tag = msg.Tag,
                    Timestamp = msg.Timestamp
                });

                await _messageRepository.MarkAsDeliveredAsync(msg.MessageId);
            }
        }
        catch (InvalidOperationException ex)
        {
            _attackDetection.LogEvent("JoinChat", username, ipAddress, false, ex.Message);
            await Clients.Caller.SendAsync("Error", "Failed to join chat.");
        }
    }

    /// <summary>
    /// Initiates a key exchange by sending the caller's public key to a specific user.
    /// This is the first step in establishing end-to-end encryption between two clients.
    /// </summary>
    /// <param name="keyExchange">The key exchange message containing the public key.</param>
    public async Task InitiateKeyExchange(KeyExchangeMessage keyExchange)
    {
        await HandleKeyExchangeAsync(keyExchange, "KeyExchangeReceived", "InitiateKeyExchange");
    }

    /// <summary>
    /// Responds to a key exchange request with the recipient's public key.
    /// After both parties exchange public keys, they can derive the shared secret.
    /// </summary>
    /// <param name="keyExchange">The key exchange response containing the public key.</param>
    public async Task RespondToKeyExchange(KeyExchangeMessage keyExchange)
    {
        await HandleKeyExchangeAsync(keyExchange, "KeyExchangeCompleted", "RespondToKeyExchange");
    }

    /// <summary>
    /// Relays an encrypted message from sender to recipient.
    /// The server only sees ciphertext - it cannot decrypt the message content.
    /// </summary>
    /// <param name="message">The encrypted message to relay.</param>
    public async Task SendEncryptedMessage(EncryptedMessage message)
    {
        var ipAddress = GetIpAddress();

        if (!_rateLimiter.IsAllowed("msg-user", message.SenderUsername, 15, TimeSpan.FromSeconds(1)))
        {
            _attackDetection.LogEvent("SendEncryptedMessage", message.SenderUsername, ipAddress, false, "Message rate exceeded");
            await Clients.Caller.SendAsync("Error", "Message rate exceeded.");
            return;
        }

        if (!ValidateEncryptedMessage(message, out var validationError))
        {
            _attackDetection.LogEvent("SendEncryptedMessage", message.SenderUsername, ipAddress, false, validationError);
            await Clients.Caller.SendAsync("Error", validationError);
            return;
        }

        try
        {
            var connectedUser = await _userRepository.GetByConnectionIdAsync(Context.ConnectionId);
            if (connectedUser == null || !string.Equals(connectedUser.Username, message.SenderUsername, StringComparison.Ordinal))
            {
                _attackDetection.LogEvent("SendEncryptedMessage", message.SenderUsername, ipAddress, false, "Sender identity mismatch");
                await Clients.Caller.SendAsync("Error", "Sender identity mismatch.");
                return;
            }

            var recipient = await _userRepository.GetByUsernameAsync(message.RecipientUsername);
            if (recipient == null)
            {
                _attackDetection.LogEvent("SendEncryptedMessage", message.SenderUsername, ipAddress, false, "Recipient not found");
                await Clients.Caller.SendAsync("Error", $"User '{message.RecipientUsername}' not found.");
                return;
            }

            // Save encrypted message to database
            await _messageRepository.SaveMessageAsync(
                message.MessageId,
                connectedUser.Id,
                recipient.Id,
                message.Ciphertext,
                message.Nonce,
                message.Tag,
                message.Timestamp);

            // If recipient is online, relay the message immediately
            if (recipient.IsOnline && recipient.ConnectionId != null)
            {
                await Clients.Client(recipient.ConnectionId).SendAsync("EncryptedMessageReceived", message);
                await _messageRepository.MarkAsDeliveredAsync(message.MessageId);
            }

            _attackDetection.LogEvent("SendEncryptedMessage", message.SenderUsername, ipAddress, true, "Message delivered/queued");
            await Clients.Caller.SendAsync("MessageDelivered", message.MessageId);
        }
        catch (InvalidOperationException ex)
        {
            _attackDetection.LogEvent("SendEncryptedMessage", message.SenderUsername, ipAddress, false, ex.Message);
            await Clients.Caller.SendAsync("Error", "Failed to send message.");
        }
    }

    /// <summary>
    /// Sends an encrypted message to all connected users (broadcast).
    /// Each recipient must have already exchanged keys with the sender.
    /// </summary>
    /// <param name="messages">List of encrypted messages, one per recipient.</param>
    public async Task BroadcastEncryptedMessage(List<EncryptedMessage> messages)
    {
        foreach (var message in messages)
        {
            var connectionId = await _userRepository.GetConnectionIdAsync(message.RecipientUsername);
            if (connectionId != null)
            {
                await Clients.Client(connectionId).SendAsync("EncryptedMessageReceived", message);
            }
        }
    }

    /// <summary>
    /// Called when a client disconnects.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Get username before marking offline
        var onlineUsers = await _userRepository.GetOnlineUsersAsync();
        var user = onlineUsers.FirstOrDefault(u => u.ConnectionId == Context.ConnectionId);
        var username = user?.Username;

        // Mark user as offline in database
        await _userRepository.SetOfflineAsync(Context.ConnectionId);

        // Notify all remaining clients
        if (username != null)
        {
            _attackDetection.LogEvent("Disconnect", username, GetIpAddress(), true, "User disconnected");
            await Clients.All.SendAsync("UserLeft", username);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Returns the list of currently connected users.
    /// </summary>
    public async Task GetOnlineUsers()
    {
        var userList = await _userRepository.GetOnlineUsernamesAsync();
        await Clients.Caller.SendAsync("UserList", userList);
    }

    private async Task HandleKeyExchangeAsync(KeyExchangeMessage keyExchange, string clientEventName, string operationName)
    {
        var ipAddress = GetIpAddress();

        if (!_rateLimiter.IsAllowed("keyx-user", keyExchange.SenderUsername, 8, TimeSpan.FromSeconds(10)))
        {
            _attackDetection.LogEvent(operationName, keyExchange.SenderUsername, ipAddress, false, "Key exchange rate exceeded");
            await Clients.Caller.SendAsync("Error", "Key exchange rate exceeded.");
            return;
        }

        if (!ValidateKeyExchangeMessage(keyExchange, out var validationError))
        {
            _attackDetection.LogEvent(operationName, keyExchange.SenderUsername, ipAddress, false, validationError);
            await Clients.Caller.SendAsync("Error", validationError);
            return;
        }

        var connectedUser = await _userRepository.GetByConnectionIdAsync(Context.ConnectionId);
        if (connectedUser == null || !string.Equals(connectedUser.Username, keyExchange.SenderUsername, StringComparison.Ordinal))
        {
            _attackDetection.LogEvent(operationName, keyExchange.SenderUsername, ipAddress, false, "Sender identity mismatch");
            await Clients.Caller.SendAsync("Error", "Sender identity mismatch.");
            return;
        }

        var connectionId = await _userRepository.GetConnectionIdAsync(keyExchange.RecipientUsername);
        if (connectionId == null)
        {
            _attackDetection.LogEvent(operationName, keyExchange.SenderUsername, ipAddress, false, "Recipient offline");
            await Clients.Caller.SendAsync("Error", $"User '{keyExchange.RecipientUsername}' is not online.");
            return;
        }

        _attackDetection.LogEvent(operationName, keyExchange.SenderUsername, ipAddress, true, "Key exchange relayed");
        await Clients.Client(connectionId).SendAsync(clientEventName, keyExchange);
    }

    private static bool ValidateEncryptedMessage(EncryptedMessage message, out string error)
    {
        if (string.IsNullOrWhiteSpace(message.MessageId) ||
            string.IsNullOrWhiteSpace(message.SenderUsername) ||
            string.IsNullOrWhiteSpace(message.RecipientUsername) ||
            string.IsNullOrWhiteSpace(message.Ciphertext) ||
            string.IsNullOrWhiteSpace(message.Nonce) ||
            string.IsNullOrWhiteSpace(message.Tag))
        {
            error = "Malformed encrypted message.";
            return false;
        }

        if (!TryDecodeBase64(message.Ciphertext, out var cipherBytes) || cipherBytes.Length > MaxCipherBytes)
        {
            error = "Invalid or oversized ciphertext.";
            return false;
        }

        if (!TryDecodeBase64(message.Nonce, out var nonceBytes) || nonceBytes.Length != 12)
        {
            error = "Invalid nonce format.";
            return false;
        }

        if (!TryDecodeBase64(message.Tag, out var tagBytes) || tagBytes.Length != 16)
        {
            error = "Invalid authentication tag format.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool ValidateKeyExchangeMessage(KeyExchangeMessage keyExchange, out string error)
    {
        if (string.IsNullOrWhiteSpace(keyExchange.SenderUserId) ||
            string.IsNullOrWhiteSpace(keyExchange.SenderUsername) ||
            string.IsNullOrWhiteSpace(keyExchange.RecipientUsername) ||
            string.IsNullOrWhiteSpace(keyExchange.PublicKey) ||
            string.IsNullOrWhiteSpace(keyExchange.PublicKeyFingerprint))
        {
            error = "Malformed key exchange message.";
            return false;
        }

        if (!TryDecodeBase64(keyExchange.PublicKey, out var publicKeyBytes) || publicKeyBytes.Length > MaxPublicKeyBytes)
        {
            error = "Invalid public key payload.";
            return false;
        }

        if (!TryDecodeBase64(keyExchange.PublicKeyFingerprint, out var fingerprintBytes) || fingerprintBytes.Length != 32)
        {
            error = "Invalid public key fingerprint format.";
            return false;
        }

        var computed = SHA256.HashData(publicKeyBytes);
        if (!CryptographicOperations.FixedTimeEquals(computed, fingerprintBytes))
        {
            error = "Public key fingerprint verification failed.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryDecodeBase64(string value, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            bytes = Array.Empty<byte>();
            return false;
        }
    }

    private string GetIpAddress()
    {
        return Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
