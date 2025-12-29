using Microsoft.AspNetCore.SignalR;
using SecureChatServer.Models;
using System.Collections.Concurrent;

namespace SecureChatServer.Hubs;

/// <summary>
/// SignalR Hub for secure chat messaging.
/// 
/// SECURITY MODEL:
/// - This hub only RELAYS encrypted messages and public keys
/// - The server NEVER sees plaintext messages
/// - The server CANNOT decrypt messages (no access to private keys)
/// - All cryptographic operations happen client-side
/// </summary>
public sealed class ChatHub : Hub
{
    // Thread-safe dictionary mapping username to connection info
    private static readonly ConcurrentDictionary<string, ConnectedUser> ConnectedUsers = new();
    
    // Reverse lookup: ConnectionId -> Username
    private static readonly ConcurrentDictionary<string, string> ConnectionToUsername = new();

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

        // Check if username is already taken
        if (ConnectedUsers.ContainsKey(username))
        {
            await Clients.Caller.SendAsync("Error", "Username is already taken.");
            return;
        }

        var user = new ConnectedUser
        {
            ConnectionId = Context.ConnectionId,
            Username = username,
            ConnectedAt = DateTime.UtcNow
        };

        // Register the user
        if (ConnectedUsers.TryAdd(username, user))
        {
            ConnectionToUsername.TryAdd(Context.ConnectionId, username);

            // Notify all clients about the new user
            await Clients.All.SendAsync("UserJoined", username);

            // Send current user list to the new user
            var userList = ConnectedUsers.Keys.ToList();
            await Clients.Caller.SendAsync("UserList", userList);

            // Confirm successful join
            await Clients.Caller.SendAsync("JoinConfirmed", username);
        }
        else
        {
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
        // Validate the recipient exists
        if (!ConnectedUsers.TryGetValue(keyExchange.RecipientUsername, out var recipient))
        {
            await Clients.Caller.SendAsync("Error", $"User '{keyExchange.RecipientUsername}' is not online.");
            return;
        }

        // Relay the public key to the recipient (server cannot derive the shared secret)
        await Clients.Client(recipient.ConnectionId).SendAsync("KeyExchangeReceived", keyExchange);
    }

    /// <summary>
    /// Responds to a key exchange request with the recipient's public key.
    /// After both parties exchange public keys, they can derive the shared secret.
    /// </summary>
    /// <param name="keyExchange">The key exchange response containing the public key.</param>
    public async Task RespondToKeyExchange(KeyExchangeMessage keyExchange)
    {
        // Validate the original sender exists
        if (!ConnectedUsers.TryGetValue(keyExchange.RecipientUsername, out var originalSender))
        {
            await Clients.Caller.SendAsync("Error", $"User '{keyExchange.RecipientUsername}' is not online.");
            return;
        }

        // Relay the response public key back to the original sender
        await Clients.Client(originalSender.ConnectionId).SendAsync("KeyExchangeCompleted", keyExchange);
    }

    /// <summary>
    /// Relays an encrypted message from sender to recipient.
    /// The server only sees ciphertext - it cannot decrypt the message content.
    /// </summary>
    /// <param name="message">The encrypted message to relay.</param>
    public async Task SendEncryptedMessage(EncryptedMessage message)
    {
        // Validate the recipient exists
        if (!ConnectedUsers.TryGetValue(message.RecipientUsername, out var recipient))
        {
            await Clients.Caller.SendAsync("Error", $"User '{message.RecipientUsername}' is not online.");
            return;
        }

        // Relay the encrypted message (server cannot read the content)
        await Clients.Client(recipient.ConnectionId).SendAsync("EncryptedMessageReceived", message);

        // Also send confirmation back to sender
        await Clients.Caller.SendAsync("MessageDelivered", message.MessageId);
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
            if (ConnectedUsers.TryGetValue(message.RecipientUsername, out var recipient))
            {
                await Clients.Client(recipient.ConnectionId).SendAsync("EncryptedMessageReceived", message);
            }
        }
    }

    /// <summary>
    /// Called when a client disconnects.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (ConnectionToUsername.TryRemove(Context.ConnectionId, out var username))
        {
            ConnectedUsers.TryRemove(username, out _);
            
            // Notify all remaining clients
            await Clients.All.SendAsync("UserLeft", username);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Returns the list of currently connected users.
    /// </summary>
    public async Task GetOnlineUsers()
    {
        var userList = ConnectedUsers.Keys.ToList();
        await Clients.Caller.SendAsync("UserList", userList);
    }
}
