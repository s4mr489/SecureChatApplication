using SecureChatApplication.Services;
using System.Windows;

namespace SecureChatApplication.ViewModels;

/// <summary>
/// ViewModel for the login screen.
/// Handles username input, server connection, and navigation to chat.
/// </summary>
public sealed class LoginViewModel : ViewModelBase
{
    private readonly SignalRChatService _chatService;
    private string _username = string.Empty;
    private string _serverUrl = "http://localhost:5000/chathub";
    private string _statusMessage = string.Empty;
    private bool _isConnecting;
    private bool _isConnected;

    public LoginViewModel(SignalRChatService chatService)
    {
        _chatService = chatService;

        ConnectCommand = new AsyncRelayCommand(ConnectAsync, CanConnect);
        
        // Subscribe to service events
        _chatService.OnJoinConfirmed += OnJoinConfirmed;
        _chatService.OnError += OnError;
        _chatService.OnConnectionStateChanged += OnConnectionStateChanged;
    }

    /// <summary>
    /// The username to join with.
    /// </summary>
    public string Username
    {
        get => _username;
        set
        {
            if (SetProperty(ref _username, value))
            {
                ConnectCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// The SignalR server URL.
    /// </summary>
    public string ServerUrl
    {
        get => _serverUrl;
        set
        {
            if (SetProperty(ref _serverUrl, value))
            {
                ConnectCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Status message displayed to the user.
    /// </summary>
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>
    /// Whether a connection attempt is in progress.
    /// </summary>
    public bool IsConnecting
    {
        get => _isConnecting;
        set
        {
            if (SetProperty(ref _isConnecting, value))
            {
                ConnectCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Whether successfully connected and joined.
    /// </summary>
    public bool IsConnected
    {
        get => _isConnected;
        set => SetProperty(ref _isConnected, value);
    }

    /// <summary>
    /// Command to connect to the server.
    /// </summary>
    public AsyncRelayCommand ConnectCommand { get; }

    /// <summary>
    /// Event raised when login is successful and should navigate to chat.
    /// </summary>
    public event Action<string>? OnLoginSuccess;

    private bool CanConnect()
    {
        return !IsConnecting 
               && !string.IsNullOrWhiteSpace(Username) 
               && !string.IsNullOrWhiteSpace(ServerUrl)
               && Username.Length >= 2
               && Username.Length <= 20;
    }

    private async Task ConnectAsync()
    {
        if (!CanConnect()) return;

        IsConnecting = true;
        StatusMessage = "Connecting to server...";

        try
        {
            // Connect to the SignalR hub
            await _chatService.ConnectAsync(ServerUrl);
            
            StatusMessage = "Connected! Joining chat...";

            // Join the chat with username
            await _chatService.JoinChatAsync(Username.Trim());
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection failed: {ex.Message}";
            IsConnecting = false;
        }
    }

    private void OnJoinConfirmed(string username)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            IsConnecting = false;
            IsConnected = true;
            StatusMessage = $"Welcome, {username}!";
            
            // Notify that login was successful
            OnLoginSuccess?.Invoke(username);
        });
    }

    private void OnError(string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            StatusMessage = $"Error: {message}";
            IsConnecting = false;
        });
    }

    private void OnConnectionStateChanged(Microsoft.AspNetCore.SignalR.Client.HubConnectionState state)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (state == Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Disconnected)
            {
                IsConnected = false;
                if (!IsConnecting)
                {
                    StatusMessage = "Disconnected from server.";
                }
            }
        });
    }

    public void Cleanup()
    {
        _chatService.OnJoinConfirmed -= OnJoinConfirmed;
        _chatService.OnError -= OnError;
        _chatService.OnConnectionStateChanged -= OnConnectionStateChanged;
    }
}
