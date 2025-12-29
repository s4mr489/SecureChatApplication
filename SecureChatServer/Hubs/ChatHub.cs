using Microsoft.AspNetCore.SignalR;
using SecureChatServer.Data.Repositories;
using SecureChatServer.Models;

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
    private readonly IUserRepository _userRepository;
    private readonly IMessageRepository _messageRepository;

    public ChatHub(IUserRepository userRepository, IMessageRepository messageRepository)
    {
        _userRepository = userRepository;
        _messageRepository = messageRepository;
    }

    /// <summary>
    /// Called when a user joins the chat with their username.
    /// </summary>
    /// <param name="username">The display name for the user.</param>
    public async Task JoinChat(string username)
    {
        // Validate username
        if (string.IsNullOrWhiteSpace(username))
        {
            await Clients.Caller.SendAsync("Error", "Username cannot be empty.");
            return;
        }

        // Check if username is already taken by an online user
        if (await _userRepository.IsUsernameTakenAsync(username))
        {
            await Clients.Caller.SendAsync("Error", "Username is already taken.");
            return;
        }

        try
        {
            // Create or update user in database
            var user = await _userRepository.CreateOrUpdateOnJoinAsync(username, Context.ConnectionId);

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
                var encryptedMsg = new EncryptedMessage
                {
                    MessageId = msg.MessageId,
                    SenderUsername = msg.Sender.Username,
                    RecipientUsername = username,
                    Ciphertext = msg.Ciphertext,
                    IV = msg.IV,
                    Timestamp = msg.Timestamp
                };
                await Clients.Caller.SendAsync("EncryptedMessageReceived", encryptedMsg);
                await _messageRepository.MarkAsDeliveredAsync(msg.MessageId);
            }
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("Error", $"Failed to join chat: {ex.Message}");
        }
    }

    /// <summary>
    /// Initiates a key exchange by sending the caller's public key to a specific user.
    /// This is the first step in establishing end-to-end encryption between two clients.
    /// </summary>
    /// <param name="keyExchange">The key exchange message containing the public key.</param>
    public async Task InitiateKeyExchange(KeyExchangeMessage keyExchange)
    {
        // Get recipient's connection ID from database
        var connectionId = await _userRepository.GetConnectionIdAsync(keyExchange.RecipientUsername);

        if (connectionId == null)
        {
            await Clients.Caller.SendAsync("Error", $"User '{keyExchange.RecipientUsername}' is not online.");
            return;
        }

        // Relay the public key to the recipient (server cannot derive the shared secret)
        await Clients.Client(connectionId).SendAsync("KeyExchangeReceived", keyExchange);
    }

    /// <summary>
    /// Responds to a key exchange request with the recipient's public key.
    /// After both parties exchange public keys, they can derive the shared secret.
    /// </summary>
    /// <param name="keyExchange">The key exchange response containing the public key.</param>
    public async Task RespondToKeyExchange(KeyExchangeMessage keyExchange)
    {
        // Get original sender's connection ID from database
        var connectionId = await _userRepository.GetConnectionIdAsync(keyExchange.RecipientUsername);

        if (connectionId == null)
        {
            await Clients.Caller.SendAsync("Error", $"User '{keyExchange.RecipientUsername}' is not online.");
            return;
        }

        // Relay the response public key back to the original sender
        await Clients.Client(connectionId).SendAsync("KeyExchangeCompleted", keyExchange);
    }

    /// <summary>
    /// Relays an encrypted message from sender to recipient.
    /// The server only sees ciphertext - it cannot decrypt the message content.
    /// </summary>
    /// <param name="message">The encrypted message to relay.</param>
    public async Task SendEncryptedMessage(EncryptedMessage message)
    {
        try
        {
            // Get sender and recipient from database
            var sender = await _userRepository.GetByUsernameAsync(message.SenderUsername);
            var recipient = await _userRepository.GetByUsernameAsync(message.RecipientUsername);

            if (sender == null)
            {
                await Clients.Caller.SendAsync("Error", "Sender not found.");
                return;
            }

            if (recipient == null)
            {
                await Clients.Caller.SendAsync("Error", $"User '{message.RecipientUsername}' not found.");
                return;
            }

            // Save encrypted message to database
            await _messageRepository.SaveMessageAsync(
                message.MessageId,
                sender.Id,
                recipient.Id,
                message.Ciphertext,
                message.IV,
                message.Timestamp);

            // If recipient is online, relay the message immediately
            if (recipient.IsOnline && recipient.ConnectionId != null)
            {
                await Clients.Client(recipient.ConnectionId).SendAsync("EncryptedMessageReceived", message);
                await _messageRepository.MarkAsDeliveredAsync(message.MessageId);
            }

            // Send confirmation back to sender
            await Clients.Caller.SendAsync("MessageDelivered", message.MessageId);
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("Error", $"Failed to send message: {ex.Message}");
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
}
