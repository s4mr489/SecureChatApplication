using Microsoft.Extensions.DependencyInjection;
using SecureChatApplication.ViewModels;
using System.Windows;

namespace SecureChatApplication;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// Main window that handles navigation between Login and Chat views.
/// </summary>
public partial class MainWindow : Window
{
    private readonly LoginViewModel _loginViewModel;
    private readonly ChatViewModel _chatViewModel;

    public MainWindow()
    {
        InitializeComponent();

        // Get view models from DI container
        _loginViewModel = App.ServiceProvider.GetRequiredService<LoginViewModel>();
        _chatViewModel = App.ServiceProvider.GetRequiredService<ChatViewModel>();

        // Set data contexts
        LoginView.DataContext = _loginViewModel;
        ChatView.DataContext = _chatViewModel;

        // Subscribe to navigation events
        _loginViewModel.OnLoginSuccess += OnLoginSuccess;
        _chatViewModel.OnDisconnectRequested += OnDisconnectRequested;
    }

    /// <summary>
    /// Handles successful login - navigates to chat view.
    /// </summary>
    private void OnLoginSuccess(string username)
    {
        Dispatcher.Invoke(() =>
        {
            // Initialize chat with username
            _chatViewModel.Initialize(username);

            // Navigate to chat view
            LoginView.Visibility = Visibility.Collapsed;
            ChatView.Visibility = Visibility.Visible;

            Title = $"Secure Chat - {username} (End-to-End Encrypted)";
        });
    }

    /// <summary>
    /// Handles disconnect request - navigates back to login view.
    /// </summary>
    private void OnDisconnectRequested()
    {
        Dispatcher.Invoke(() =>
        {
            // Navigate back to login view
            ChatView.Visibility = Visibility.Collapsed;
            LoginView.Visibility = Visibility.Visible;

            // Reset login status
            _loginViewModel.StatusMessage = "Disconnected. Enter username to reconnect.";
            _loginViewModel.IsConnected = false;

            Title = "Secure Chat - End-to-End Encrypted";
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        // Cleanup subscriptions
        _loginViewModel.OnLoginSuccess -= OnLoginSuccess;
        _chatViewModel.OnDisconnectRequested -= OnDisconnectRequested;

        // Cleanup view models
        _loginViewModel.Cleanup();
        _chatViewModel.Dispose();

        base.OnClosed(e);
    }
}