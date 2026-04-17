using SecureChatApplication.ViewModels;
using System.Windows.Controls;

namespace SecureChatApplication.Views;

/// <summary>
/// Interaction logic for SecurityDashboardView.xaml
/// </summary>
public partial class SecurityDashboardView : UserControl
{
    public SecurityDashboardView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Raised when the user clicks "Back to Chat".
    /// </summary>
    public event Action? BackToChatRequested;

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is SecurityDashboardViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }

    private void BackToChat_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        BackToChatRequested?.Invoke();
    }
}
