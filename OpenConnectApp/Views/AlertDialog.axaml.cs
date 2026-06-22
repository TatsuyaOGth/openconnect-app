using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OpenConnectApp.Views;

public partial class AlertDialog : Window
{
    public AlertDialog() { InitializeComponent(); }

    public AlertDialog(string message, string title = "通知")
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
    }

    public static Task ShowAsync(Window? owner, string message, string title = "通知")
    {
        var dialog = new AlertDialog(message, title);
        if (owner != null)
            return dialog.ShowDialog(owner);
        dialog.Show();
        return Task.CompletedTask;
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
