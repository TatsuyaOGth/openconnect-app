using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenConnectApp.Interfaces;
using OpenConnectApp.Models;
using OpenConnectApp.Services;

namespace OpenConnectApp.ViewModels;

public partial class ConnectionListViewModel : ViewModelBase
{
    private readonly ConnectionManager _connectionManager;
    private readonly CsvService _csvService;
    private readonly AppConfigService _configService;
    private readonly LogService _logger;

    private ICredentialStore? _credentialStore;
    private AppConfig? _config;

    public ObservableCollection<VpnConnection> Connections { get; } = [];

    [ObservableProperty]
    private VpnConnection? _selectedConnection;

    [ObservableProperty]
    private string _statusText = "未接続";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isConnected;

    public event Action<string>? ErrorOccurred;
    public event Action<string>? WarningOccurred;

    public ConnectionListViewModel(
        ConnectionManager connectionManager,
        CsvService csvService,
        AppConfigService configService,
        LogService logger)
    {
        _connectionManager = connectionManager;
        _csvService = csvService;
        _configService = configService;
        _logger = logger;

        _connectionManager.StateChanged += OnConnectionStateChanged;
    }

    public void Initialize(ICredentialStore credentialStore, AppConfig config)
    {
        _credentialStore = credentialStore;
        _config = config;
        LoadCsv();
    }

    [RelayCommand]
    private void Reload()
    {
        LoadCsv();
    }

    private void LoadCsv()
    {
        try
        {
            var (connections, wasCreated) = _csvService.LoadOrCreate(_configService.CsvPath);
            Connections.Clear();
            foreach (var c in connections)
                Connections.Add(c);

            if (wasCreated)
                WarningOccurred?.Invoke(
                    $"接続先設定ファイルが見つからなかったため、雛形を作成しました: {_configService.CsvPath}");
        }
        catch (InvalidDataException ex)
        {
            ErrorOccurred?.Invoke(ex.Message);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"CSV読み込みエラー: {ex.Message}");
            _logger.Log("CSV読み込みエラー", ex);
        }
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        if (SelectedConnection == null || _credentialStore == null || _config == null) return;

        IsBusy = true;
        try
        {
            await _connectionManager.ConnectAsync(SelectedConnection, _credentialStore, _config);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanConnect()
        => !IsBusy && SelectedConnection != null
           && (_connectionManager.Status == ConnectionStatus.Disconnected
               || _connectionManager.Status == ConnectionStatus.Connected);

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task DisconnectAsync()
    {
        IsBusy = true;
        try
        {
            await _connectionManager.DisconnectAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanDisconnect()
        => !IsBusy && _connectionManager.Status == ConnectionStatus.Connected;

    private void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsConnected = e.Status == ConnectionStatus.Connected;

            StatusText = e.Status switch
            {
                ConnectionStatus.Connected => $"{e.ConnectedName} に接続中",
                ConnectionStatus.Connecting => $"{e.ConnectedName} に接続中...",
                ConnectionStatus.Disconnecting => "切断中...",
                _ => "未接続",
            };

            ConnectCommand.NotifyCanExecuteChanged();
            DisconnectCommand.NotifyCanExecuteChanged();

            if (e.ErrorMessage != null)
                ErrorOccurred?.Invoke(e.ErrorMessage);
        });
    }

    partial void OnIsBusyChanged(bool value)
    {
        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedConnectionChanged(VpnConnection? value)
    {
        ConnectCommand.NotifyCanExecuteChanged();
    }
}
