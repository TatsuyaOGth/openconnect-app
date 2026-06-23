using CommunityToolkit.Mvvm.ComponentModel;
using OpenConnectApp.Interfaces;
using OpenConnectApp.Models;
using OpenConnectApp.Services;

namespace OpenConnectApp.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppConfigService _configService;
    private readonly PathDetectionService _pathDetection;
    private readonly LogService _logger;
    private readonly ConnectionManager _connectionManager;

    public ConnectionListViewModel ConnectionListVM { get; }
    public SettingsViewModel SettingsVM { get; }
    public LogViewModel LogVM { get; }

    [ObservableProperty]
    private int _selectedTabIndex;

    private AppConfig _config = new();
    private ICredentialStore? _credentialStore;

    public MainWindowViewModel(
        AppConfigService configService,
        PathDetectionService pathDetection,
        LogService logger,
        ConnectionManager connectionManager,
        ConnectionListViewModel connectionListVM,
        SettingsViewModel settingsVM,
        LogViewModel logVM)
    {
        _configService = configService;
        _pathDetection = pathDetection;
        _logger = logger;
        _connectionManager = connectionManager;
        ConnectionListVM = connectionListVM;
        SettingsVM = settingsVM;
        LogVM = logVM;
    }

    /// <summary>アプリ起動時の初期化処理。</summary>
    public void Initialize()
    {
        _config = _configService.Load();
        _config = _pathDetection.DetectAndCache(_config);

        _credentialStore = CreateCredentialStore(_config);

        ConnectionListVM.Initialize(_credentialStore, _config);
        SettingsVM.Initialize(_credentialStore, _config);

        _connectionManager.RestoreState();

        SettingsVM.ConfirmPlaintextStore = _ => Task.FromResult(true); // Viewから差し替え
    }

    /// <summary>アプリ終了時の後処理。接続中なら切断してからリソースを解放する。</summary>
    public async Task ShutdownAsync()
    {
        if (_connectionManager.Status == ConnectionStatus.Connected
            || _connectionManager.Status == ConnectionStatus.Connecting)
        {
            await _connectionManager.DisconnectAsync();
        }
    }

    private ICredentialStore CreateCredentialStore(AppConfig config)
    {
        return config.CredentialStoreType == "plaintext"
            ? new PlaintextCredentialStore(_configService.PlainCredentialPath, config.CommonUsername)
            : new KeychainCredentialStore();
    }
}

