using Microsoft.Win32;
using SecureChatApplication.Models;
using SecureChatApplication.Services;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Windows;

namespace SecureChatApplication.ViewModels;

public sealed class ChatViewModel : ViewModelBase, IDisposable
{
    private const long MaxMediaSizeBytes = 8L * 1024 * 1024; // 8 MB

    private readonly SignalRChatService _chatService;
    private readonly KeyExchangeService _keyExchangeService;
    private readonly CryptoService _cryptoService;
    private readonly ChatHistoryService _chatHistoryService;
    private readonly SafeBrowsingService _safeBrowsingService;
    private readonly Dictionary<string, ObservableCollection<ChatMessage>> _messagesByUser = new();
    private readonly ConcurrentDictionary<string, string> _trustedFingerprints = new(StringComparer.Ordinal);

    private string _currentUsername = string.Empty;
    private ChatPartner? _selectedUser;
    private string _messageText = string.Empty;
    private bool _disposed;

    public ChatViewModel(
        SignalRChatService chatService,
        KeyExchangeService keyExchangeService,
        CryptoService cryptoService,
        ChatHistoryService chatHistoryService,
        SafeBrowsingService safeBrowsingService)
    {
        _chatService = chatService;
        _keyExchangeService = keyExchangeService;
        _cryptoService = cryptoService;
        _chatHistoryService = chatHistoryService;
        _safeBrowsingService = safeBrowsingService;

        SendMessageCommand = new AsyncRelayCommand(SendMessageAsync, CanSendMessageCheck);
        SendMediaCommand = new AsyncRelayCommand(SendMediaAsync, CanSendMediaCheck);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync);
        OpenSecurityDashboardCommand = new RelayCommand(() => OnSecurityDashboardRequested?.Invoke());

        _chatService.OnUserJoined += OnUserJoined;
        _chatService.OnUserLeft += OnUserLeft;
        _chatService.OnUserListReceived += OnUserListReceived;
        _chatService.OnKeyExchangeReceived += OnKeyExchangeReceived;
        _chatService.OnKeyExchangeCompleted += OnKeyExchangeCompleted;
        _chatService.OnEncryptedMessageReceived += OnEncryptedMessageReceived;
        _chatService.OnMessageDelivered += OnMessageDelivered;
        _chatService.OnMessageHistoryReceived += OnMessageHistoryReceived;
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
                OnPropertyChanged(nameof(CanSendMedia));
                SendMessageCommand.RaiseCanExecuteChanged();
                SendMediaCommand.RaiseCanExecuteChanged();
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

    public bool CanSendMedia => _selectedUser?.IsKeyExchangeComplete == true;

    public AsyncRelayCommand SendMessageCommand { get; }

    public AsyncRelayCommand SendMediaCommand { get; }

    public AsyncRelayCommand DisconnectCommand { get; }

    public RelayCommand OpenSecurityDashboardCommand { get; }

    public event Action? OnDisconnectRequested;

    public event Action? OnSecurityDashboardRequested;

    public void Initialize(string username)
    {
        CurrentUsername = username;
        LoadKnownPartners();
    }

    /// <summary>
    /// Loads previously chatted partners from local history as offline entries.
    /// </summary>
    private void LoadKnownPartners()
    {
        var known = _chatHistoryService.GetKnownPartners(CurrentUsername);
        foreach (var partner in known)
        {
            if (partner != CurrentUsername && !OnlineUsers.Any(u => u.Username == partner))
            {
                OnlineUsers.Add(new ChatPartner { Username = partner, IsOnline = false });
            }
        }
    }

    private async void OnSelectedUserChanged()
    {
        if (_selectedUser == null)
        {
            return;
        }

        // Load local chat history when selecting a user
        LoadLocalHistory(_selectedUser.Username);

        // Don't initiate key exchange with offline users
        if (!_selectedUser.IsOnline)
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

    private void LoadLocalHistory(string partnerUsername)
    {
        if (!_messagesByUser.TryGetValue(partnerUsername, out var messages))
        {
            messages = new ObservableCollection<ChatMessage>();
            _messagesByUser[partnerUsername] = messages;
        }

        var history = _chatHistoryService.LoadAsChatMessages(CurrentUsername, partnerUsername);
        if (history.Count == 0) return;

        // Build a set of existing message IDs to avoid duplicates
        var existingIds = new HashSet<string>(messages.Select(m => m.MessageId));
        var newHistory = history.Where(h => !existingIds.Contains(h.MessageId)).ToList();
        if (newHistory.Count == 0) return;

        // Insert history messages at the beginning, preserving chronological order
        for (var i = 0; i < newHistory.Count; i++)
        {
            messages.Insert(i, newHistory[i]);
        }

        OnPropertyChanged(nameof(Messages));
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
                    OnPropertyChanged(nameof(CanSendMedia));
                    SendMessageCommand.RaiseCanExecuteChanged();
                    SendMediaCommand.RaiseCanExecuteChanged();

                    // Load message history now that we have the shared key
                    _ = _chatService.GetMessageHistoryAsync(senderUsername);
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
                    OnPropertyChanged(nameof(CanSendMedia));
                    SendMessageCommand.RaiseCanExecuteChanged();
                    SendMediaCommand.RaiseCanExecuteChanged();

                    // Load message history now that we have the shared key
                    _ = _chatService.GetMessageHistoryAsync(senderUsername);
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

            var chatMsg = new ChatMessage
            {
                MessageId = messageId,
                SenderUsername = CurrentUsername,
                Content = plaintext,
                Timestamp = timestamp,
                IsOwnMessage = true,
                IsDelivered = false
            };
            Messages.Add(chatMsg);
            _chatHistoryService.SaveMessage(CurrentUsername, _selectedUser.Username, chatMsg);

            // Check URLs in the message for safety
            _ = CheckAndNotifyUrlSafetyAsync(plaintext, _selectedUser.Username);

            MessageText = string.Empty;
            await _chatService.SendEncryptedMessageAsync(encryptedMessage);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to send message: {ex.Message}");
        }
    }

    private async Task CheckAndNotifyUrlSafetyAsync(string text, string partnerUsername)
    {
        try
        {
            var notices = await _safeBrowsingService.CheckAllUrlsAsync(text);
            if (notices.Count == 0) return;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (!_messagesByUser.TryGetValue(partnerUsername, out var messages))
                {
                    messages = new ObservableCollection<ChatMessage>();
                    _messagesByUser[partnerUsername] = messages;
                }

                foreach (var notice in notices)
                {
                    messages.Add(new ChatMessage
                    {
                        MessageId = Guid.NewGuid().ToString(),
                        SenderUsername = "🔒 Safe Browsing",
                        Content = notice,
                        Timestamp = DateTime.UtcNow,
                        IsOwnMessage = false,
                        IsDelivered = true
                    });
                }

                if (_selectedUser?.Username == partnerUsername)
                {
                    OnPropertyChanged(nameof(Messages));
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"URL safety check failed: {ex.Message}");
        }
    }

    private bool CanSendMessageCheck()
    {
        return _selectedUser?.IsKeyExchangeComplete == true
               && !string.IsNullOrWhiteSpace(MessageText);
    }

    private bool CanSendMediaCheck()
    {
        return _selectedUser?.IsKeyExchangeComplete == true;
    }

    private async Task SendMediaAsync()
    {
        if (_selectedUser?.SharedKey == null) return;

        var dialog = new OpenFileDialog
        {
            Title = "Select a file to send (max 8 MB)",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true) return;

        var filePath = dialog.FileName;
        var fileName = Path.GetFileName(filePath);
        var fileInfo = new FileInfo(filePath);

        if (fileInfo.Length > MaxMediaSizeBytes)
        {
            MessageBox.Show("File exceeds the 8 MB limit.", "File Too Large",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var fileBytes = await File.ReadAllBytesAsync(filePath);
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var messageType = ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" ? 1 : 2;
        var messageId = Guid.NewGuid().ToString();
        var timestamp = DateTime.UtcNow;

        try
        {
            var payload = _cryptoService.EncryptBytes(fileBytes, _selectedUser.SharedKey);

            var encryptedMessage = new EncryptedMessage
            {
                MessageId = messageId,
                SenderUsername = CurrentUsername,
                RecipientUsername = _selectedUser.Username,
                Ciphertext = payload.Ciphertext,
                Nonce = payload.Nonce,
                Tag = payload.Tag,
                Timestamp = timestamp,
                MessageType = messageType,
                FileName = fileName
            };

            var mediaMsg = new ChatMessage
            {
                MessageId = messageId,
                SenderUsername = CurrentUsername,
                Content = fileName,
                Timestamp = timestamp,
                IsOwnMessage = true,
                IsDelivered = false,
                MessageType = messageType,
                FileName = fileName,
                MediaBytes = fileBytes
            };
            Messages.Add(mediaMsg);
            _chatHistoryService.SaveMessage(CurrentUsername, _selectedUser.Username, mediaMsg);

            await _chatService.SendEncryptedMessageAsync(encryptedMessage);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to send media: {ex.Message}");
        }
    }

    private void OnEncryptedMessageReceived(EncryptedMessage encryptedMessage)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                var senderUsername = encryptedMessage.SenderUsername;
                var sender = OnlineUsers.FirstOrDefault(u => u.Username == senderUsername);
                if (sender?.SharedKey == null) return;

                string content;
                byte[]? mediaBytes = null;

                if (encryptedMessage.MessageType == 0)
                {
                    content = _cryptoService.Decrypt(
                        encryptedMessage.Ciphertext,
                        encryptedMessage.Nonce,
                        encryptedMessage.Tag,
                        sender.SharedKey);
                }
                else
                {
                    mediaBytes = _cryptoService.DecryptBytes(
                        encryptedMessage.Ciphertext,
                        encryptedMessage.Nonce,
                        encryptedMessage.Tag,
                        sender.SharedKey);
                    content = encryptedMessage.FileName ?? "attachment";
                }

                if (!_messagesByUser.TryGetValue(senderUsername, out var messages))
                {
                    messages = new ObservableCollection<ChatMessage>();
                    _messagesByUser[senderUsername] = messages;
                }

                var receivedMsg = new ChatMessage
                {
                    MessageId = encryptedMessage.MessageId,
                    SenderUsername = senderUsername,
                    Content = content,
                    Timestamp = encryptedMessage.Timestamp,
                    IsOwnMessage = false,
                    IsDelivered = true,
                    MessageType = encryptedMessage.MessageType,
                    FileName = encryptedMessage.FileName,
                    MediaBytes = mediaBytes
                };
                messages.Add(receivedMsg);
                _chatHistoryService.SaveMessage(CurrentUsername, senderUsername, receivedMsg);

                // Check URLs in received messages for safety
                if (encryptedMessage.MessageType == 0)
                {
                    _ = CheckAndNotifyUrlSafetyAsync(content, senderUsername);
                }

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

            var existing = OnlineUsers.FirstOrDefault(u => u.Username == username);
            if (existing != null)
            {
                existing.IsOnline = true;
            }
            else
            {
                OnlineUsers.Add(new ChatPartner { Username = username, IsOnline = true });
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

            // Mark as offline but keep in the list so chat history is still accessible
            user.IsOnline = false;

            if (user.SharedKey != null)
            {
                CryptographicOperations.ZeroMemory(user.SharedKey);
                user.SharedKey = null;
            }

            user.IsKeyExchangeComplete = false;
            user.IsKeyExchangeInitiated = false;
            _trustedFingerprints.TryRemove(username, out _);
            _keyExchangeService.RemoveKeyPair(username);

            OnPropertyChanged(nameof(CanSendMessage));
            OnPropertyChanged(nameof(CanSendMedia));
            SendMessageCommand.RaiseCanExecuteChanged();
            SendMediaCommand.RaiseCanExecuteChanged();
        });
    }

    private void OnUserListReceived(List<string> usernames)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var onlineSet = new HashSet<string>(usernames.Where(u => u != CurrentUsername));

            // Mark all existing users as offline first
            foreach (var user in OnlineUsers)
            {
                user.IsOnline = onlineSet.Contains(user.Username);
            }

            // Add any new online users not already in the list
            foreach (var username in onlineSet)
            {
                if (!OnlineUsers.Any(u => u.Username == username))
                {
                    OnlineUsers.Add(new ChatPartner { Username = username, IsOnline = true });
                }
            }
        });
    }

    private void OnError(string message)
    {
        System.Diagnostics.Debug.WriteLine($"Chat error: {message}");
    }

    private void OnMessageHistoryReceived(string partnerUsername, List<EncryptedMessage> encryptedMessages)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var partner = OnlineUsers.FirstOrDefault(u => u.Username == partnerUsername);
            if (partner?.SharedKey == null) return;

            if (!_messagesByUser.TryGetValue(partnerUsername, out var messages))
            {
                messages = new ObservableCollection<ChatMessage>();
                _messagesByUser[partnerUsername] = messages;
            }

            // Only load history if we haven't already
            if (messages.Any(m => m.IsFromHistory)) return;

            var historyMessages = new List<ChatMessage>();
            foreach (var enc in encryptedMessages)
            {
                try
                {
                    string content;
                    byte[]? mediaBytes = null;

                    if (enc.MessageType == 0)
                    {
                        content = _cryptoService.Decrypt(enc.Ciphertext, enc.Nonce, enc.Tag, partner.SharedKey);
                    }
                    else
                    {
                        mediaBytes = _cryptoService.DecryptBytes(enc.Ciphertext, enc.Nonce, enc.Tag, partner.SharedKey);
                        content = enc.FileName ?? "attachment";
                    }

                    historyMessages.Add(new ChatMessage
                    {
                        MessageId = enc.MessageId,
                        SenderUsername = enc.SenderUsername,
                        Content = content,
                        Timestamp = enc.Timestamp,
                        IsOwnMessage = enc.SenderUsername == CurrentUsername,
                        IsDelivered = true,
                        IsFromHistory = true,
                        MessageType = enc.MessageType,
                        FileName = enc.FileName,
                        MediaBytes = mediaBytes
                    });
                }
                catch (CryptographicException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to decrypt history message: {ex.Message}");
                }
            }

            // Insert history at the beginning
            for (var i = historyMessages.Count - 1; i >= 0; i--)
            {
                messages.Insert(0, historyMessages[i]);
            }

            if (_selectedUser?.Username == partnerUsername)
            {
                OnPropertyChanged(nameof(Messages));
            }
        });
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
        _messagesByUser.Clear();
        OnlineUsers.Clear();
        _selectedUser = null;

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
        _chatService.OnMessageHistoryReceived -= OnMessageHistoryReceived;
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
