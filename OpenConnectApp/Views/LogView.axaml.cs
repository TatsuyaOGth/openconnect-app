using Avalonia.Controls;
using Avalonia.Threading;
using OpenConnectApp.ViewModels;

namespace OpenConnectApp.Views;

public partial class LogView : UserControl
{
    private LogViewModel? _vm;

    public LogView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null)
            _vm.LinesChanged -= OnLinesChanged;

        _vm = DataContext as LogViewModel;

        if (_vm != null)
        {
            _vm.LinesChanged += OnLinesChanged;
            ScrollToEnd();
        }
    }

    private void OnLinesChanged() => Dispatcher.UIThread.Post(ScrollToEnd);

    private void ScrollToEnd()
    {
        if (LogList.ItemCount > 0)
            LogList.ScrollIntoView(LogList.ItemCount - 1);
    }
}
