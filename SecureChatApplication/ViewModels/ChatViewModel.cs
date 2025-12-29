using SecureChatApplication.Models;
using SecureChatApplication.Services;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Windows;

namespace SecureChatApplication.ViewModels;

/// <summary>
/// ViewModel for the main chat interface.
/// Orchestrates key exchange, encryption, decryption, and message handling.
/// 
/// SECURITY FLOW:
/// 1. When selecting a new user, initiate ECDH key exchange
/// 2. Generate key pair, send public key via server
/// 3. Receive partner's public key, derive shared AES-256 key
/// 4. Encrypt all messages with AES-GCM before sending
/// 5. Decrypt received messages using shared key
/// </summary>
public sealed class ChatViewModel : ViewModelBase, IDisposable
{
    private readonly SignalRChatService _chatService;
    private readonly DiffieHellmanService _dhService;
    private readonly AesEncryptionService _aesService;
    
    // Store messages per user
    private readonly Dictionary<string, ObservableCollection<ChatMessage>> _messagesByUser = new();
    
    // Current state
    private string _currentUsername = string.Empty;
    private ChatPartner? _selectedUser;
    private string _messageText = string.Empty;
    private bool _disposed;

    public ChatViewModel(
        SignalRChatService chatService,
        DiffieHellmanService dhService,
        AesEncryptionService aesService)
    {
        _chatService = chatService;
        _dhService = dhService;
        _aesService = aesService;

        // Initialize commands
        SendMessageCommand = new AsyncRelayCommand(SendMessageAsync, CanSendMessageCheck);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync);

        // Subscribe to service events
        _chatService.OnUserJoined += OnUserJoined;
        _chatService.OnUserLeft += OnUserLeft;
        _chatService.OnUserListReceived += OnUserListReceived;
        _chatService.OnKeyExchangeReceived += OnKeyExchangeReceived;
        _chatService.OnKeyExchangeCompleted += OnKeyExchangeCompleted;
        _chatService.OnEncryptedMessageReceived += OnEncryptedMessageReceived;
        _chatService.OnMessageDelivered += OnMessageDelivered;
        _chatService.OnError += OnError;
    }

    /// <summary>
    /// The current user's username.
    /// </summary>
    public string CurrentUsername
    {
        get => _currentUsername;
        set => SetProperty(ref _currentUsername, value);
    }

    /// <summary>
    /// Collection of online users.
    /// </summary>
    public ObservableCollection<ChatPartner> OnlineUsers { get; } = new();

    /// <summary>
    /// The currently selected chat partner.
    /// </summary>
    public ChatPartner? SelectedUser
    {
        get => _selectedUser;
        set
        {
            if (SetProperty(ref _selectedUser, value))
            {
                OnSelectedUserChanged();
                OnPropertyChanged(nameof(Messages));
                OnPropertyChanged(nameof(CanSendMessage));
                SendMessageCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Messages for the currently selected user.
    /// </summary>
    public ObservableCollection<ChatMessage> Messages
    {
        get
        {
            if (_selectedUser == null) return new ObservableCollection<ChatMessage>();
            
            if (!_messagesByUser.TryGetValue(_selectedUser.Username, out var messages))
            {
                messages = new ObservableCollection<ChatMessage>();
                _messagesByUser[_selectedUser.Username] = messages;
            }
            return messages;
        }
    }

    /// <summary>
    /// The message text being typed.
    /// </summary>
    public string MessageText
    {
        get => _messageText;
        set
        {
            if (SetProperty(ref _messageText, value))
            {
                SendMessageCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Whether a message can be sent (user selected and key exchange complete).
    /// </summary>
    public bool CanSendMessage => _selectedUser?.IsKeyExchangeComplete == true 
                                  && !string.IsNullOrWhiteSpace(MessageText);

    /// <summary>
    /// Command to send a message.
    /// </summary>
    public AsyncRelayCommand SendMessageCommand { get; }

    /// <summary>
    /// Command to disconnect from the server.
    /// </summary>
    public AsyncRelayCommand DisconnectCommand { get; }

    /// <summary>
    /// Event raised when requesting to disconnect (navigate back to login).
    /// </summary>
    public event Action? OnDisconnectRequested;

    /// <summary>
    /// Initializes the chat with the current username.
    /// </summary>
    public void Initialize(string username)
    {
        CurrentUsername = username;
        
        // Set the username in the DH service for consistent key derivation
        _dhService.SetOwnUsername(username);
    }

    /// <summary>
    /// Called when a new user is selected - initiates key exchange if needed.
    /// </summary>
    private async void OnSelectedUserChanged()
    {
        if (_selectedUser == null) return;

        // Don't initiate if already complete or in progress
        if (_selectedUser.IsKeyExchangeComplete || _selectedUser.IsKeyExchangeInitiated)
            return;

        try
        {
            await InitiateKeyExchangeAsync(_selectedUser.Username);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Key exchange initiation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Initiates ECDH key exchange with a partner.
    /// </summary>
    private async Task InitiateKeyExchangeAsync(string partnerUsername)
    {
        // Step 1: Generate our ECDH key pair for this partner
        string ourPublicKey = _dhService.GenerateKeyPair(partnerUsername);

        // Step 2: Mark as initiated
        var partner = OnlineUsers.FirstOrDefault(u => u.Username == partnerUsername);
        if (partner != null)
        {
            partner.IsKeyExchangeInitiated = true;
        }

        // Step 3: Send our public key to the partner via server
        var keyExchange = new KeyExchangeMessage
        {
            SenderUsername = CurrentUsername,
            RecipientUsername = partnerUsername,
            PublicKey = ourPublicKey,
            Timestamp = DateTime.UtcNow
        };

        await _chatService.InitiateKeyExchangeAsync(keyExchange);
        System.Diagnostics.Debug.WriteLine($"Initiated key exchange with {partnerUsername}");
    }

    /// <summary>
    /// Handles incoming key exchange request from another user.
    /// </summary>
    private void OnKeyExchangeReceived(KeyExchangeMessage keyExchange)
    {
        Application.Current.Dispatcher.Invoke(async () =>
        {
            try
            {
                var senderUsername = keyExchange.SenderUsername;

                // Check if we already have a key pair for this user (we initiated first)
                bool weInitiatedFirst = _dhService.HasKeyPairFor(senderUsername);
                
                string ourPublicKey;
                if (weInitiatedFirst)
                {
                    // We already initiated - use existing key pair
                    ourPublicKey = _dhService.GetPublicKey(senderUsername);
                    System.Diagnostics.Debug.WriteLine($"Using existing key pair for {senderUsername} (we initiated first)");
                }
                else
                {
                    // They initiated first - generate our key pair
                    ourPublicKey = _dhService.GenerateKeyPair(senderUsername);
                    System.Diagnostics.Debug.WriteLine($"Generated new key pair for {senderUsername} (they initiated first)");
                }

                // Derive the shared key from their public key
                byte[] sharedKey = _dhService.DeriveSharedKey(senderUsername, keyExchange.PublicKey);

                // Store the shared key
                var partner = OnlineUsers.FirstOrDefault(u => u.Username == senderUsername);
                if (partner != null)
                {
                    // Only update if not already complete, or if keys match
                    if (!partner.IsKeyExchangeComplete)
                    {
                        partner.SharedKey = sharedKey;
                        partner.IsKeyExchangeComplete = true;
                        OnPropertyChanged(nameof(CanSendMessage));
                        SendMessageCommand.RaiseCanExecuteChanged();
                    }
                }

                // Send our public key back to complete the handshake
                var response = new KeyExchangeMessage
                {
                    SenderUsername = CurrentUsername,
                    RecipientUsername = senderUsername,
                    PublicKey = ourPublicKey,
                    Timestamp = DateTime.UtcNow
                };

                await _chatService.RespondToKeyExchangeAsync(response);
                System.Diagnostics.Debug.WriteLine($"Responded to key exchange from {senderUsername}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Key exchange response failed: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Handles completion of key exchange (receiving partner's public key response).
    /// </summary>
    private void OnKeyExchangeCompleted(KeyExchangeMessage keyExchange)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                var senderUsername = keyExchange.SenderUsername;
                
                var partner = OnlineUsers.FirstOrDefault(u => u.Username == senderUsername);
                
                // Skip if already completed (avoid overwriting with different key)
                if (partner?.IsKeyExchangeComplete == true)
                {
                    System.Diagnostics.Debug.WriteLine($"Key exchange already completed with {senderUsername}, skipping");
                    return;
                }

                // Derive the shared key from their public key
                byte[] sharedKey = _dhService.DeriveSharedKey(senderUsername, keyExchange.PublicKey);

                // Store the shared key
                if (partner != null)
                {
                    partner.SharedKey = sharedKey;
                    partner.IsKeyExchangeComplete = true;
                    partner.IsKeyExchangeInitiated = false;
                    
                    // Refresh UI
                    OnPropertyChanged(nameof(CanSendMessage));
                    SendMessageCommand.RaiseCanExecuteChanged();
                }

                System.Diagnostics.Debug.WriteLine($"Key exchange completed with {senderUsername}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Key exchange completion failed: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Sends an encrypted message to the selected user.
    /// </summary>
    private async Task SendMessageAsync()
    {
        if (_selectedUser == null || string.IsNullOrWhiteSpace(MessageText))
            return;

        if (_selectedUser.SharedKey == null)
        {
            System.Diagnostics.Debug.WriteLine("Cannot send: no shared key established");
            return;
        }

        var messageId = Guid.NewGuid().ToString();
        var plaintext = MessageText.Trim();
        var timestamp = DateTime.UtcNow;

        try
        {
            var (ciphertext, iv) = _aesService.Encrypt(plaintext, _selectedUser.SharedKey);

            var encryptedMessage = new EncryptedMessage
            {
                MessageId = messageId,
                SenderUsername = CurrentUsername,
                RecipientUsername = _selectedUser.Username,
                Ciphertext = ciphertext,
                IV = iv,
                Timestamp = timestamp
            };

            var chatMessage = new ChatMessage
            {
                MessageId = messageId,
                SenderUsername = CurrentUsername,
                Content = plaintext,
                Timestamp = timestamp,
                IsOwnMessage = true,
                IsDelivered = false
            };

            Messages.Add(chatMessage);
            MessageText = string.Empty;

            await _chatService.SendEncryptedMessageAsync(encryptedMessage);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to send message: {ex.Message}");
        }
    }

    /// <summary>
    /// Check method for command CanExecute.
    /// </summary>
    private bool CanSendMessageCheck()
    {
        return _selectedUser?.IsKeyExchangeComplete == true 
               && !string.IsNullOrWhiteSpace(MessageText);
    }

    /// <summary>
    /// Handles receiving an encrypted message.
    /// </summary>
    private void OnEncryptedMessageReceived(EncryptedMessage encryptedMessage)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                var senderUsername = encryptedMessage.SenderUsername;

                var sender = OnlineUsers.FirstOrDefault(u => u.Username == senderUsername);
                if (sender?.SharedKey == null)
                {
                    System.Diagnostics.Debug.WriteLine($"Received message from {senderUsername} but no shared key");
                    return;
                }

                string plaintext = _aesService.Decrypt(
                    encryptedMessage.Ciphertext,
                    encryptedMessage.IV,
                    sender.SharedKey
                );

                var chatMessage = new ChatMessage
                {
                    MessageId = encryptedMessage.MessageId,
                    SenderUsername = senderUsername,
                    Content = plaintext,
                    Timestamp = encryptedMessage.Timestamp,
                    IsOwnMessage = false,
                    IsDelivered = true
                };

                if (!_messagesByUser.TryGetValue(senderUsername, out var messages))
                {
                    messages = new ObservableCollection<ChatMessage>();
                    _messagesByUser[senderUsername] = messages;
                }
                messages.Add(chatMessage);

                if (_selectedUser?.Username == senderUsername)
                {
                    OnPropertyChanged(nameof(Messages));
                }

                System.Diagnostics.Debug.WriteLine($"Decrypted message from {senderUsername}: {plaintext.Substring(0, Math.Min(20, plaintext.Length))}...");
            }
            catch (CryptographicException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to decrypt message: {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing message: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Handles message delivery confirmation.
    /// </summary>
    private void OnMessageDelivered(string messageId)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            foreach (var messages in _messagesByUser.Values)
            {
                var message = messages.FirstOrDefault(m => m.MessageId == messageId);
                while (message != null)
                {
                    message.IsDelivered = true;
                    break;
                }
            }
        });
    }

    /// <summary>
    /// Handles a new user joining.
    /// </summary>
    private void OnUserJoined(string username)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (username == CurrentUsername) return;
            
            if (!OnlineUsers.Any(u => u.Username == username))
            {
                OnlineUsers.Add(new ChatPartner { Username = username });
            }
        });
    }

    /// <summary>
    /// Handles a user leaving.
    /// </summary>
    private void OnUserLeft(string username)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var user = OnlineUsers.FirstOrDefault(u => u.Username == username);
            if (user != null)
            {
                OnlineUsers.Remove(user);
                
                // Clear shared key securely
                if (user.SharedKey != null)
                {
                    CryptographicOperations.ZeroMemory(user.SharedKey);
                }
                
                // Remove DH key pair
                _dhService.RemoveKeyPair(username);
                
                // Clear selection if this user was selected
                if (_selectedUser?.Username == username)
                {
                    SelectedUser = null;
                }
            }
        });
    }

    /// <summary>
    /// Handles receiving the initial user list.
    /// </summary>
    private void OnUserListReceived(List<string> usernames)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            OnlineUsers.Clear();
            foreach (var username in usernames)
            {
                if (username != CurrentUsername)
                {
                    OnlineUsers.Add(new ChatPartner { Username = username });
                }
            }
        });
    }

    /// <summary>
    /// Handles errors from the service.
    /// </summary>
    private void OnError(string message)
    {
        System.Diagnostics.Debug.WriteLine($"Chat error: {message}");
    }

    /// <summary>
    /// Disconnects from the chat and clears all keys.
    /// </summary>
    private async Task DisconnectAsync()
    {
        // Clear all shared keys securely
        foreach (var user in OnlineUsers)
        {
            if (user.SharedKey != null)
            {
                CryptographicOperations.ZeroMemory(user.SharedKey);
            }
        }

        // Clear DH service keys
        _dhService.ClearAllKeys();

        // Disconnect from server
        await _chatService.DisconnectAsync();

        // Notify to navigate back to login
        OnDisconnectRequested?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;

        // Unsubscribe from events
        _chatService.OnUserJoined -= OnUserJoined;
        _chatService.OnUserLeft -= OnUserLeft;
        _chatService.OnUserListReceived -= OnUserListReceived;
        _chatService.OnKeyExchangeReceived -= OnKeyExchangeReceived;
        _chatService.OnKeyExchangeCompleted -= OnKeyExchangeCompleted;
        _chatService.OnEncryptedMessageReceived -= OnEncryptedMessageReceived;
        _chatService.OnMessageDelivered -= OnMessageDelivered;
        _chatService.OnError -= OnError;

        // Clear all shared keys
        foreach (var user in OnlineUsers)
        {
            if (user.SharedKey != null)
            {
                CryptographicOperations.ZeroMemory(user.SharedKey);
            }
        }

        _disposed = true;
    }
}
