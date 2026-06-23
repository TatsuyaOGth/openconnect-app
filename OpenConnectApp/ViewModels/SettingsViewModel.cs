using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenConnectApp.Interfaces;
using OpenConnectApp.Models;
using OpenConnectApp.Services;

namespace OpenConnectApp.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly AppConfigService _configService;
    private readonly LogService _logger;

    private ICredentialStore? _credentialStore;
    private AppConfig _config = new();
    private bool _suppressUseKeychainHandler;

    public event Action<string>? ErrorOccurred;
    public event Action<string>? InfoOccurred;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _useKeychain = true;

    [ObservableProperty]
    private string _openConnectPath = string.Empty;

    [ObservableProperty]
    private string _vpncScriptPath = string.Empty;

    public string AppDataDir => _configService.AppDataDir;

    public SettingsViewModel(AppConfigService configService, LogService logger)
    {
        _configService = configService;
        _logger = logger;
    }

    public void Initialize(ICredentialStore credentialStore, AppConfig config)
    {
        _credentialStore = credentialStore;
        _config = config;

        // UI に現在値をセット
        _suppressUseKeychainHandler = true;
        Username = config.CommonUsername;
        UseKeychain = config.CredentialStoreType == "keychain";
        OpenConnectPath = config.OpenConnectPath ?? string.Empty;
        VpncScriptPath = config.VpncScriptPath ?? string.Empty;
        _suppressUseKeychainHandler = false;

        _ = LoadPasswordAsync(credentialStore);
    }

    /// <summary>
    /// 設定タブのストア変更イベント。外部から呼ばれてストア切替を処理する。
    /// </summary>
    public Func<string, Task<bool>>? ConfirmPlaintextStore { get; set; }

    [RelayCommand]
    private async Task SaveCredentialsAsync()
    {
        if (_credentialStore == null) return;

        try
        {
            var username = Username;
            var password = Password;
            await Task.Run(() => _credentialStore.Save(username, password));
            _config.CommonUsername = Username;
            _configService.Save(_config);
            InfoOccurred?.Invoke("認証情報を保存しました。");
            _logger.Log($"認証情報を保存しました（ユーザー名: {Username}）");
        }
        catch (Exception ex)
        {
            _logger.Log("認証情報保存エラー", ex);
            ErrorOccurred?.Invoke($"保存に失敗しました: {ex.Message}");
        }
    }

    [RelayCommand]
    private void SavePaths()
    {
        _config.OpenConnectPath = string.IsNullOrWhiteSpace(OpenConnectPath) ? null : OpenConnectPath;
        _config.VpncScriptPath = string.IsNullOrWhiteSpace(VpncScriptPath) ? null : VpncScriptPath;
        _configService.Save(_config);
        InfoOccurred?.Invoke("パス設定を保存しました。");
        _logger.Log($"パス設定を保存: openconnect={_config.OpenConnectPath}, vpnc-script={_config.VpncScriptPath}");
    }

    [RelayCommand]
    private void OpenCsv()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "open",
                UseShellExecute = false,
            };
            psi.ArgumentList.Add(_configService.CsvPath);
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"CSVファイルを開けませんでした: {ex.Message}");
        }
    }

    /// <summary>
    /// 認証ストアの種別を切り替える。
    /// 平文への切替時は確認ダイアログを表示し、了承後に切替を実行する。
    /// </summary>
    public async Task SwitchCredentialStoreAsync(
        bool toPlaintext,
        Func<ICredentialStore> plaintextFactory,
        Func<ICredentialStore> keychainFactory)
    {
        if (_credentialStore == null) return;

        if (toPlaintext)
        {
            if (ConfirmPlaintextStore != null)
            {
                var confirmed = await ConfirmPlaintextStore(
                    "パスワードが暗号化されずにディスク上に保存されます。共有環境のマシンでは使用しないでください。");
                if (!confirmed)
                {
                    // キャンセルされたら元のUI状態に戻す（再帰防止）
                    _suppressUseKeychainHandler = true;
                    UseKeychain = true;
                    _suppressUseKeychainHandler = false;
                    return;
                }
            }
        }

        var oldStore = _credentialStore;
        var newStore = toPlaintext ? plaintextFactory() : keychainFactory();

        await Task.Run(() =>
        {
            // 旧ストアから認証情報を取得して新ストアへ移行する
            var oldCreds = oldStore.Load();
            if (oldCreds.HasValue)
                newStore.Save(oldCreds.Value.Username, oldCreds.Value.Password);

            oldStore.Clear();

            _config.CredentialStoreType = toPlaintext ? "plaintext" : "keychain";
            _configService.Save(_config);
        });

        _credentialStore = newStore;

        InfoOccurred?.Invoke($"認証ストアを {(toPlaintext ? "平文" : "Keychain")} に切り替えました。");
        _logger.Log($"認証ストア切替: {_config.CredentialStoreType}");
    }

    public ICredentialStore? CurrentCredentialStore => _credentialStore;

    private async Task LoadPasswordAsync(ICredentialStore credentialStore)
    {
        try
        {
            var creds = await Task.Run(() => credentialStore.Load());
            if (creds.HasValue)
                Password = creds.Value.Password;
        }
        catch (Exception ex)
        {
            _logger.Log("認証情報の読み込みに失敗しました", ex);
        }
    }

    partial void OnUseKeychainChanged(bool value)
    {
        if (_suppressUseKeychainHandler || _credentialStore == null)
            return;

        _ = HandleCredentialStoreSwitchAsync(value);
    }

    private Task HandleCredentialStoreSwitchAsync(bool useKeychain)
    {
        return SwitchCredentialStoreAsync(
            toPlaintext: !useKeychain,
            plaintextFactory: () => new PlaintextCredentialStore(_configService.PlainCredentialPath, Username),
            keychainFactory: () => new KeychainCredentialStore());
    }
}
