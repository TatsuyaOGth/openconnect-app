using Avalonia.Controls;
using OpenConnectApp.ViewModels;

namespace OpenConnectApp.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += OnWindowClosing;
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        e.Cancel = true;
        await vm.ShutdownAsync();
        Closing -= OnWindowClosing;
        Close();
    }
}