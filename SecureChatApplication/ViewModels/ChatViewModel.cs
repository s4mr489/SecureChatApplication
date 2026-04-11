using SecureChatApplication.Models;
using SecureChatApplication.Services;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Windows;

namespace SecureChatApplication.ViewModels;

public sealed class ChatViewModel : ViewModelBase, IDisposable
{
    private readonly SignalRChatService _chatService;
    private readonly KeyExchangeService _keyExchangeService;
    private readonly CryptoService _cryptoService;
    private readonly Dictionary<string, ObservableCollection<ChatMessage>> _messagesByUser = new();
    private readonly ConcurrentDictionary<string, string> _trustedFingerprints = new(StringComparer.Ordinal);

    private string _currentUsername = string.Empty;
    private ChatPartner? _selectedUser;
    private string _messageText = string.Empty;
    private bool _disposed;

    public ChatViewModel(
        SignalRChatService chatService,
        KeyExchangeService keyExchangeService,
        CryptoService cryptoService)
    {
        _chatService = chatService;
        _keyExchangeService = keyExchangeService;
        _cryptoService = cryptoService;

        SendMessageCommand = new AsyncRelayCommand(SendMessageAsync, CanSendMessageCheck);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync);

        _chatService.OnUserJoined += OnUserJoined;
        _chatService.OnUserLeft += OnUserLeft;
        _chatService.OnUserListReceived += OnUserListReceived;
        _chatService.OnKeyExchangeReceived += OnKeyExchangeReceived;
        _chatService.OnKeyExchangeCompleted += OnKeyExchangeCompleted;
        _chatService.OnEncryptedMessageReceived += OnEncryptedMessageReceived;
        _chatService.OnMessageDelivered += OnMessageDelivered;
        _chatService.OnError += OnError;
    }

    public string CurrentUsername
    {
        get => _currentUsername;
        set => SetProperty(ref _currentUsername, value);
    }

    public ObservableCollection<ChatPartner> OnlineUsers { get; } = new();

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

    public ObservableCollection<ChatMessage> Messages
    {
        get
        {
            if (_selectedUser == null)
            {
                return new ObservableCollection<ChatMessage>();
            }

            if (!_messagesByUser.TryGetValue(_selectedUser.Username, out var messages))
            {
                messages = new ObservableCollection<ChatMessage>();
                _messagesByUser[_selectedUser.Username] = messages;
            }

            return messages;
        }
    }

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

    public bool CanSendMessage => _selectedUser?.IsKeyExchangeComplete == true
                                  && !string.IsNullOrWhiteSpace(MessageText);

    public AsyncRelayCommand SendMessageCommand { get; }

    public AsyncRelayCommand DisconnectCommand { get; }

    public event Action? OnDisconnectRequested;

    public void Initialize(string username)
    {
        CurrentUsername = username;
    }

    private async void OnSelectedUserChanged()
    {
        if (_selectedUser == null)
        {
            return;
        }

        if (_selectedUser.IsKeyExchangeComplete || _selectedUser.IsKeyExchangeInitiated)
        {
            return;
        }

        try
        {
            await InitiateKeyExchangeAsync(_selectedUser.Username);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Key exchange initiation failed: {ex.Message}");
        }
    }

    private async Task InitiateKeyExchangeAsync(string partnerUsername)
    {
        var publicKey = _keyExchangeService.GeneratePublicKey(partnerUsername);
        var fingerprint = KeyExchangeService.ComputePublicKeyFingerprint(publicKey);

        var partner = OnlineUsers.FirstOrDefault(u => u.Username == partnerUsername);
        if (partner != null)
        {
            partner.IsKeyExchangeInitiated = true;
        }

        await _chatService.InitiateKeyExchangeAsync(new KeyExchangeMessage
        {
            SenderUserId = CurrentUsername,
            SenderUsername = CurrentUsername,
            RecipientUsername = partnerUsername,
            PublicKey = publicKey,
            PublicKeyFingerprint = fingerprint,
            Timestamp = DateTime.UtcNow
        });
    }

    private void OnKeyExchangeReceived(KeyExchangeMessage keyExchange)
    {
        Application.Current.Dispatcher.Invoke(async () =>
        {
            try
            {
                if (!ValidateKeyExchangeIdentity(keyExchange))
                {
                    return;
                }

                var senderUsername = keyExchange.SenderUsername;

                var weInitiatedFirst = _keyExchangeService.HasKeyPairFor(senderUsername);
                var ourPublicKey = weInitiatedFirst
                    ? _keyExchangeService.GetPublicKey(senderUsername)
                    : _keyExchangeService.GeneratePublicKey(senderUsername);

                var sharedKey = _keyExchangeService.DeriveSharedKey(senderUsername, keyExchange.PublicKey, CurrentUsername);

                var partner = OnlineUsers.FirstOrDefault(u => u.Username == senderUsername);
                if (partner != null && !partner.IsKeyExchangeComplete)
                {
                    partner.SharedKey = sharedKey;
                    partner.PublicKeyFingerprint = keyExchange.PublicKeyFingerprint;
                    partner.IsKeyExchangeComplete = true;
                    OnPropertyChanged(nameof(CanSendMessage));
                    SendMessageCommand.RaiseCanExecuteChanged();
                }

                await _chatService.RespondToKeyExchangeAsync(new KeyExchangeMessage
                {
                    SenderUserId = CurrentUsername,
                    SenderUsername = CurrentUsername,
                    RecipientUsername = senderUsername,
                    PublicKey = ourPublicKey,
                    PublicKeyFingerprint = KeyExchangeService.ComputePublicKeyFingerprint(ourPublicKey),
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Key exchange response failed: {ex.Message}");
            }
        });
    }

    private void OnKeyExchangeCompleted(KeyExchangeMessage keyExchange)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                if (!ValidateKeyExchangeIdentity(keyExchange))
                {
                    return;
                }

                var senderUsername = keyExchange.SenderUsername;
                var partner = OnlineUsers.FirstOrDefault(u => u.Username == senderUsername);

                if (partner?.IsKeyExchangeComplete == true)
                {
                    return;
                }

                var sharedKey = _keyExchangeService.DeriveSharedKey(senderUsername, keyExchange.PublicKey, CurrentUsername);

                if (partner != null)
                {
                    partner.SharedKey = sharedKey;
                    partner.PublicKeyFingerprint = keyExchange.PublicKeyFingerprint;
                    partner.IsKeyExchangeComplete = true;
                    partner.IsKeyExchangeInitiated = false;
                    OnPropertyChanged(nameof(CanSendMessage));
                    SendMessageCommand.RaiseCanExecuteChanged();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Key exchange completion failed: {ex.Message}");
            }
        });
    }

    private bool ValidateKeyExchangeIdentity(KeyExchangeMessage keyExchange)
    {
        if (keyExchange.RecipientUsername != CurrentUsername)
        {
            System.Diagnostics.Debug.WriteLine("Rejected key exchange: recipient mismatch.");
            return false;
        }

        if (!string.Equals(keyExchange.SenderUserId, keyExchange.SenderUsername, StringComparison.Ordinal))
        {
            System.Diagnostics.Debug.WriteLine("Rejected key exchange: sender identity mismatch.");
            return false;
        }

        var computedFingerprint = KeyExchangeService.ComputePublicKeyFingerprint(keyExchange.PublicKey);
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(computedFingerprint),
                Convert.FromBase64String(keyExchange.PublicKeyFingerprint)))
        {
            System.Diagnostics.Debug.WriteLine("Rejected key exchange: fingerprint mismatch.");
            return false;
        }

        if (_trustedFingerprints.TryGetValue(keyExchange.SenderUsername, out var existingFingerprint)
            && !string.Equals(existingFingerprint, keyExchange.PublicKeyFingerprint, StringComparison.Ordinal))
        {
            System.Diagnostics.Debug.WriteLine($"Possible MITM detected for {keyExchange.SenderUsername}. Fingerprint changed.");
            return false;
        }

        _trustedFingerprints[keyExchange.SenderUsername] = keyExchange.PublicKeyFingerprint;
        return true;
    }

    private async Task SendMessageAsync()
    {
        if (_selectedUser == null || string.IsNullOrWhiteSpace(MessageText) || _selectedUser.SharedKey == null)
        {
            return;
        }

        var messageId = Guid.NewGuid().ToString();
        var plaintext = MessageText.Trim();
        var timestamp = DateTime.UtcNow;

        try
        {
            var payload = _cryptoService.Encrypt(plaintext, _selectedUser.SharedKey);

            var encryptedMessage = new EncryptedMessage
            {
                MessageId = messageId,
                SenderUsername = CurrentUsername,
                RecipientUsername = _selectedUser.Username,
                Ciphertext = payload.Ciphertext,
                Nonce = payload.Nonce,
                Tag = payload.Tag,
                Timestamp = timestamp
            };

            Messages.Add(new ChatMessage
            {
                MessageId = messageId,
                SenderUsername = CurrentUsername,
                Content = plaintext,
                Timestamp = timestamp,
                IsOwnMessage = true,
                IsDelivered = false
            });

            MessageText = string.Empty;
            await _chatService.SendEncryptedMessageAsync(encryptedMessage);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to send message: {ex.Message}");
        }
    }

    private bool CanSendMessageCheck()
    {
        return _selectedUser?.IsKeyExchangeComplete == true
               && !string.IsNullOrWhiteSpace(MessageText);
    }

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
                    return;
                }

                var plaintext = _cryptoService.Decrypt(
                    encryptedMessage.Ciphertext,
                    encryptedMessage.Nonce,
                    encryptedMessage.Tag,
                    sender.SharedKey);

                if (!_messagesByUser.TryGetValue(senderUsername, out var messages))
                {
                    messages = new ObservableCollection<ChatMessage>();
                    _messagesByUser[senderUsername] = messages;
                }

                messages.Add(new ChatMessage
                {
                    MessageId = encryptedMessage.MessageId,
                    SenderUsername = senderUsername,
                    Content = plaintext,
                    Timestamp = encryptedMessage.Timestamp,
                    IsOwnMessage = false,
                    IsDelivered = true
                });

                if (_selectedUser?.Username == senderUsername)
                {
                    OnPropertyChanged(nameof(Messages));
                }
            }
            catch (CryptographicException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to decrypt message: {ex.Message}");
            }
        });
    }

    private void OnMessageDelivered(string messageId)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            foreach (var messages in _messagesByUser.Values)
            {
                var message = messages.FirstOrDefault(m => m.MessageId == messageId);
                if (message != null)
                {
                    message.IsDelivered = true;
                }
            }
        });
    }

    private void OnUserJoined(string username)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (username == CurrentUsername)
            {
                return;
            }

            if (!OnlineUsers.Any(u => u.Username == username))
            {
                OnlineUsers.Add(new ChatPartner { Username = username });
            }
        });
    }

    private void OnUserLeft(string username)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var user = OnlineUsers.FirstOrDefault(u => u.Username == username);
            if (user == null)
            {
                return;
            }

            OnlineUsers.Remove(user);

            if (user.SharedKey != null)
            {
                CryptographicOperations.ZeroMemory(user.SharedKey);
            }

            _trustedFingerprints.TryRemove(username, out _);
            _keyExchangeService.RemoveKeyPair(username);

            if (_selectedUser?.Username == username)
            {
                SelectedUser = null;
            }
        });
    }

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

    private void OnError(string message)
    {
        System.Diagnostics.Debug.WriteLine($"Chat error: {message}");
    }

    private async Task DisconnectAsync()
    {
        foreach (var user in OnlineUsers)
        {
            if (user.SharedKey != null)
            {
                CryptographicOperations.ZeroMemory(user.SharedKey);
            }
        }

        _keyExchangeService.ClearAllKeys();
        _trustedFingerprints.Clear();

        await _chatService.DisconnectAsync();
        OnDisconnectRequested?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _chatService.OnUserJoined -= OnUserJoined;
        _chatService.OnUserLeft -= OnUserLeft;
        _chatService.OnUserListReceived -= OnUserListReceived;
        _chatService.OnKeyExchangeReceived -= OnKeyExchangeReceived;
        _chatService.OnKeyExchangeCompleted -= OnKeyExchangeCompleted;
        _chatService.OnEncryptedMessageReceived -= OnEncryptedMessageReceived;
        _chatService.OnMessageDelivered -= OnMessageDelivered;
        _chatService.OnError -= OnError;

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
