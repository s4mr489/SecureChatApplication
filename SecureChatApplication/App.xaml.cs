using Microsoft.Extensions.DependencyInjection;
using SecureChatApplication.Services;
using SecureChatApplication.ViewModels;
using System.Windows;

namespace SecureChatApplication;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// The service provider for dependency injection.
    /// </summary>
    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Configure dependency injection
        var services = new ServiceCollection();
        ConfigureServices(services);
        ServiceProvider = services.BuildServiceProvider();

        // Create and show main window
        var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    /// <summary>
    /// Configures all services for dependency injection.
    /// </summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        // Register cryptographic services (singleton for key management)
        services.AddSingleton<DiffieHellmanService>();
        services.AddSingleton<AesEncryptionService>();

        // Register SignalR chat service (singleton for connection management)
        services.AddSingleton<SignalRChatService>();

        // Register view models (transient - new instance each time)
        services.AddTransient<LoginViewModel>();
        services.AddTransient<ChatViewModel>();

        // Register main window
        services.AddSingleton<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Cleanup services
        if (ServiceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnExit(e);
    }
}
