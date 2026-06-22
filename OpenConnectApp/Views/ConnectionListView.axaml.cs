using Avalonia.Controls;
using OpenConnectApp.ViewModels;

namespace OpenConnectApp.Views;

public partial class ConnectionListView : UserControl
{
    public ConnectionListView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ConnectionListViewModel vm)
        {
            vm.ErrorOccurred += msg => ShowDialog("エラー", msg);
            vm.WarningOccurred += msg => ShowDialog("警告", msg);
        }
    }

    private void ShowDialog(string title, string message)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            await AlertDialog.ShowAsync(owner, message, title);
        });
    }
}
