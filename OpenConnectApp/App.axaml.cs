using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using OpenConnectApp.Interfaces;
using OpenConnectApp.Services;
using OpenConnectApp.ViewModels;
using OpenConnectApp.Views;

namespace OpenConnectApp;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainVm = _serviceProvider.GetRequiredService<MainWindowViewModel>();
            mainVm.Initialize();

            desktop.MainWindow = new MainWindow
            {
                DataContext = mainVm,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Infrastructure services
        services.AddSingleton<AppConfigService>();
        services.AddSingleton<LogService>(sp =>
        {
            var cs = sp.GetRequiredService<AppConfigService>();
            return new LogService(cs.LogPath);
        });
        services.AddSingleton<CsvService>();
        services.AddSingleton<PathDetectionService>();
        services.AddSingleton<IPrivilegedExecutor, OsascriptPrivilegedExecutor>();
        services.AddSingleton<ConnectionManager>();

        // ViewModels
        services.AddSingleton<ConnectionListViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainWindowViewModel>();
    }
}