using SecureChatApplication.Services;
using System.Windows;

namespace SecureChatApplication.ViewModels;

/// <summary>
/// ViewModel for the login / register screen.
/// Handles username/password input, mode toggle, server connection, and navigation to chat.
/// </summary>
public sealed class LoginViewModel : ViewModelBase
{
    private readonly SignalRChatService _chatService;
    private readonly ISecurityDashboardService _dashboardService;

    private string _username = string.Empty;
    private string _serverUrl = "http://localhost:5000/chathub";
    private string _statusMessage = string.Empty;
    private bool _isConnecting;
    private bool _isConnected;
    private bool _isRegisterMode;
    private string _password = string.Empty;
    private string _confirmPassword = string.Empty;

    public LoginViewModel(SignalRChatService chatService, ISecurityDashboardService dashboardService)
    {
        _chatService = chatService;
        _dashboardService = dashboardService;

        ConnectCommand = new AsyncRelayCommand(ConnectAsync, CanConnect);
        ToggleModeCommand = new RelayCommand(ToggleMode);

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
                ConnectCommand.RaiseCanExecuteChanged();
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
                ConnectCommand.RaiseCanExecuteChanged();
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
                ConnectCommand.RaiseCanExecuteChanged();
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

    /// <summary>Whether the form is in Register mode (vs Login mode).</summary>
    public bool IsRegisterMode
    {
        get => _isRegisterMode;
        set
        {
            if (SetProperty(ref _isRegisterMode, value))
            {
                OnPropertyChanged(nameof(ToggleLabel));
                OnPropertyChanged(nameof(SubmitLabel));
                ConnectCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ToggleLabel => IsRegisterMode
        ? "Already have an account? Login"
        : "New user? Register";

    public string SubmitLabel => IsRegisterMode ? "Register" : "Connect";

    /// <summary>
    /// Command to connect to the server.
    /// </summary>
    public AsyncRelayCommand ConnectCommand { get; }

    /// <summary>
    /// Command to toggle between login and register mode.
    /// </summary>
    public RelayCommand ToggleModeCommand { get; }

    /// <summary>
    /// Event raised when login is successful and should navigate to chat.
    /// </summary>
    public event Action<string>? OnLoginSuccess;

    /// <summary>Called from code-behind to pass the PasswordBox value securely.</summary>
    public void SetPassword(string password)
    {
        _password = password;
        OnPropertyChanged(nameof(PasswordHint));
        ConnectCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Called from code-behind to pass the ConfirmPassword PasswordBox value.</summary>
    public void SetConfirmPassword(string password)
    {
        _confirmPassword = password;
        OnPropertyChanged(nameof(PasswordHint));
        ConnectCommand.RaiseCanExecuteChanged();
    }

    private void ToggleMode()
    {
        IsRegisterMode = !IsRegisterMode;
        StatusMessage = string.Empty;
    }

    private bool CanConnect()
    {
        if (IsConnecting) return false;
        if (string.IsNullOrWhiteSpace(Username) || Username.Length is < 2 or > 20) return false;
        if (string.IsNullOrWhiteSpace(ServerUrl)) return false;
        if (string.IsNullOrWhiteSpace(_password) || _password.Length < 6) return false;
        if (IsRegisterMode && _password != _confirmPassword) return false;
        return true;
    }

    /// <summary>
    /// Non-blocking hint shown below the password field (empty when everything looks good).
    /// </summary>
    public string PasswordHint
    {
        get
        {
            if (_password.Length is > 0 and < 6)
                return "Password must be at least 6 characters.";
            if (IsRegisterMode && _password.Length >= 6 && _password != _confirmPassword)
                return "Passwords do not match.";
            return string.Empty;
        }
    }

    private async Task ConnectAsync()
    {
        if (!CanConnect()) return;

        IsConnecting = true;
        StatusMessage = IsRegisterMode ? "Creating account..." : "Signing in...";

        try
        {
            // Configure the security dashboard HTTP base URL derived from the hub URL
            var baseUrl = ServerUrl
                .Replace("/chathub", string.Empty, StringComparison.OrdinalIgnoreCase)
                .TrimEnd('/');
            _dashboardService.SetBaseUrl(baseUrl);

            // Connect to the SignalR hub
            await _chatService.ConnectAsync(ServerUrl);

            // Authenticate and join
            if (IsRegisterMode)
            {
                await _chatService.RegisterAsync(Username.Trim(), _password);
            }
            else
            {
                await _chatService.LoginAsync(Username.Trim(), _password);
            }
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
                    StatusMessage = "Disconnected from server.";
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
