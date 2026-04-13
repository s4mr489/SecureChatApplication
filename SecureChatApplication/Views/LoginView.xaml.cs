using SecureChatApplication.ViewModels;
using System.Windows.Controls;

namespace SecureChatApplication.Views;

/// <summary>
/// Interaction logic for LoginView.xaml.
/// Handles PasswordBox wiring because PasswordBox intentionally does not support binding.
/// </summary>
public partial class LoginView : UserControl
{
    private LoginViewModel? _viewModel;

    public LoginView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => _viewModel = e.NewValue as LoginViewModel;
    }

    private void PasswordBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is PasswordBox pb)
            _viewModel?.SetPassword(pb.Password);
    }

    private void ConfirmPasswordBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is PasswordBox pb)
            _viewModel?.SetConfirmPassword(pb.Password);
    }
}
