using Microsoft.AspNetCore.SignalR.Client;
using SecureChatApplication.Models;

namespace SecureChatApplication.Services;

/// <summary>
/// SignalR client service for secure chat communication.
/// Handles connection management, message relay, and key exchange transport.
/// 
/// NOTE: This service only handles TRANSPORT of encrypted messages.
/// All encryption/decryption happens at the view model layer.
/// </summary>
public sealed class SignalRChatService : IAsyncDisposable
{
    private HubConnection? _hubConnection;
    private string? _currentUsername;
    private bool _disposed;

    /// <summary>
    /// Event raised when successfully connected and joined the chat.
    /// </summary>
    public event Action<string>? OnJoinConfirmed;

    /// <summary>
    /// Event raised when an error occurs.
    /// </summary>
    public event Action<string>? OnError;

    /// <summary>
    /// Event raised when a user joins the chat.
    /// </summary>
    public event Action<string>? OnUserJoined;

    /// <summary>
    /// Event raised when a user leaves the chat.
    /// </summary>
    public event Action<string>? OnUserLeft;

    /// <summary>
    /// Event raised with the list of online users.
    /// </summary>
    public event Action<List<string>>? OnUserListReceived;

    /// <summary>
    /// Event raised when a key exchange request is received from another user.
    /// </summary>
    public event Action<KeyExchangeMessage>? OnKeyExchangeReceived;

    /// <summary>
    /// Event raised when a key exchange response completes the handshake.
    /// </summary>
    public event Action<KeyExchangeMessage>? OnKeyExchangeCompleted;

    /// <summary>
    /// Event raised when an encrypted message is received.
    /// </summary>
    public event Action<EncryptedMessage>? OnEncryptedMessageReceived;

    /// <summary>
    /// Event raised when a message delivery is confirmed.
    /// </summary>
    public event Action<string>? OnMessageDelivered;

    /// <summary>
    /// Event raised when the connection state changes.
    /// </summary>
    public event Action<HubConnectionState>? OnConnectionStateChanged;

    /// <summary>
    /// Current connection state.
    /// </summary>
    public HubConnectionState ConnectionState => _hubConnection?.State ?? HubConnectionState.Disconnected;

    /// <summary>
    /// The current user's username.
    /// </summary>
    public string? CurrentUsername => _currentUsername;

    /// <summary>
    /// Connects to the SignalR hub at the specified URL.
    /// </summary>
    /// <param name="hubUrl">The URL of the SignalR hub (e.g., "http://localhost:5000/chathub").</param>
    public async Task ConnectAsync(string hubUrl)
    {
        ThrowIfDisposed();

        if (_hubConnection != null)
        {
            await DisconnectAsync();
        }

        // Build the SignalR connection with automatic reconnection
        _hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect(new[] { 
                TimeSpan.Zero, 
                TimeSpan.FromSeconds(2), 
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(30)
            })
            .Build();

        // Register event handlers
        RegisterEventHandlers();

        // Handle connection state changes
        _hubConnection.Closed += async (error) =>
        {
            OnConnectionStateChanged?.Invoke(HubConnectionState.Disconnected);
            await Task.CompletedTask;
        };

        _hubConnection.Reconnecting += async (error) =>
        {
            OnConnectionStateChanged?.Invoke(HubConnectionState.Reconnecting);
            await Task.CompletedTask;
        };

        _hubConnection.Reconnected += async (connectionId) =>
        {
            OnConnectionStateChanged?.Invoke(HubConnectionState.Connected);
            // Re-join with current username after reconnection
            if (!string.IsNullOrEmpty(_currentUsername))
            {
                await JoinChatAsync(_currentUsername);
            }
        };

        // Start the connection
        await _hubConnection.StartAsync();
        OnConnectionStateChanged?.Invoke(HubConnectionState.Connected);
    }

    /// <summary>
    /// Registers handlers for all SignalR hub events.
    /// </summary>
    private void RegisterEventHandlers()
    {
        if (_hubConnection == null) return;

        _hubConnection.On<string>("JoinConfirmed", (username) =>
        {
            _currentUsername = username;
            OnJoinConfirmed?.Invoke(username);
        });

        _hubConnection.On<string>("Error", (message) =>
        {
            OnError?.Invoke(message);
        });

        _hubConnection.On<string>("UserJoined", (username) =>
        {
            OnUserJoined?.Invoke(username);
        });

        _hubConnection.On<string>("UserLeft", (username) =>
        {
            OnUserLeft?.Invoke(username);
        });

        _hubConnection.On<List<string>>("UserList", (users) =>
        {
            OnUserListReceived?.Invoke(users);
        });

        _hubConnection.On<KeyExchangeMessage>("KeyExchangeReceived", (keyExchange) =>
        {
            OnKeyExchangeReceived?.Invoke(keyExchange);
        });

        _hubConnection.On<KeyExchangeMessage>("KeyExchangeCompleted", (keyExchange) =>
        {
            OnKeyExchangeCompleted?.Invoke(keyExchange);
        });

        _hubConnection.On<EncryptedMessage>("EncryptedMessageReceived", (message) =>
        {
            OnEncryptedMessageReceived?.Invoke(message);
        });

        _hubConnection.On<string>("MessageDelivered", (messageId) =>
        {
            OnMessageDelivered?.Invoke(messageId);
        });
    }

    /// <summary>
    /// Joins the chat with the specified username.
    /// </summary>
    public async Task JoinChatAsync(string username)
    {
        ThrowIfDisposed();
        EnsureConnected();

        await _hubConnection!.InvokeAsync("JoinChat", username);
    }

    /// <summary>
    /// Initiates a key exchange with another user by sending our public key.
    /// </summary>
    public async Task InitiateKeyExchangeAsync(KeyExchangeMessage keyExchange)
    {
        ThrowIfDisposed();
        EnsureConnected();

        await _hubConnection!.InvokeAsync("InitiateKeyExchange", keyExchange);
    }

    /// <summary>
    /// Responds to a key exchange request with our public key.
    /// </summary>
    public async Task RespondToKeyExchangeAsync(KeyExchangeMessage keyExchange)
    {
        ThrowIfDisposed();
        EnsureConnected();

        await _hubConnection!.InvokeAsync("RespondToKeyExchange", keyExchange);
    }

    /// <summary>
    /// Sends an encrypted message to a specific user.
    /// </summary>
    public async Task SendEncryptedMessageAsync(EncryptedMessage message)
    {
        ThrowIfDisposed();
        EnsureConnected();

        await _hubConnection!.InvokeAsync("SendEncryptedMessage", message);
    }

    /// <summary>
    /// Requests the current list of online users.
    /// </summary>
    public async Task GetOnlineUsersAsync()
    {
        ThrowIfDisposed();
        EnsureConnected();

        await _hubConnection!.InvokeAsync("GetOnlineUsers");
    }

    /// <summary>
    /// Disconnects from the SignalR hub.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_hubConnection != null)
        {
            await _hubConnection.StopAsync();
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }
        _currentUsername = null;
        OnConnectionStateChanged?.Invoke(HubConnectionState.Disconnected);
    }

    private void EnsureConnected()
    {
        if (_hubConnection == null || _hubConnection.State != HubConnectionState.Connected)
        {
            throw new InvalidOperationException("Not connected to the chat server.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SignalRChatService));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        await DisconnectAsync();
        _disposed = true;
    }
}
