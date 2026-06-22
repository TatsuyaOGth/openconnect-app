using Avalonia.Controls;
using OpenConnectApp.Services;
using OpenConnectApp.ViewModels;

namespace OpenConnectApp.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {            // 平文ストアへの切替確認ダイアログをViewで処理する
            vm.ConfirmPlaintextStore = async message =>
            {
                var owner = TopLevel.GetTopLevel(this) as Window;
                return await ConfirmDialog.ShowAsync(owner, message, "警告");
            };

            vm.InfoOccurred += msg => ShowInfo(msg);
            vm.ErrorOccurred += msg => ShowError(msg);

            // UseKeychain プロパティの変更を監視してストア切替を行う
            vm.PropertyChanged += async (_, args) =>
            {
                if (args.PropertyName == nameof(SettingsViewModel.UseKeychain)
                    && vm.CurrentCredentialStore != null)
                {
                    await vm.SwitchCredentialStoreAsync(
                        !vm.UseKeychain,
                        plaintextFactory: () =>
                        {
                            // AppConfigService は DI から取得できないので、
                            // SettingsVM のプロパティ経由でパスを取得
                            var path = System.IO.Path.Combine(
                                vm.AppDataDir, "credentials.plain.json");
                            return new PlaintextCredentialStore(path, vm.Username);
                        },
                        keychainFactory: () => new KeychainCredentialStore());
                }
            };
        }
    }

    private void ShowInfo(string message)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            await AlertDialog.ShowAsync(owner, message, "情報");
        });
    }

    private void ShowError(string message)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            await AlertDialog.ShowAsync(owner, message, "エラー");
        });
    }
}
