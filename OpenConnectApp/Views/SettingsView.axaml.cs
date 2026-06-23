using Avalonia.Controls;
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
        {
            // 平文ストアへの切替確認ダイアログをViewで処理する
            vm.ConfirmPlaintextStore = async message =>
            {
                var owner = TopLevel.GetTopLevel(this) as Window;
                return await ConfirmDialog.ShowAsync(owner, message, "警告");
            };

            vm.InfoOccurred += msg => ShowInfo(msg);
            vm.ErrorOccurred += msg => ShowError(msg);
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
