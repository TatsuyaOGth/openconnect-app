using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OpenConnectApp.Views;

public partial class ConfirmDialog : Window
{
    private bool _result;

    public ConfirmDialog() { InitializeComponent(); }

    public ConfirmDialog(string message, string title = "確認")
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
    }

    public static async Task<bool> ShowAsync(Window? owner, string message, string title = "確認")
    {
        var dialog = new ConfirmDialog(message, title);
        if (owner != null)
        {
            await dialog.ShowDialog(owner);
        }
        else
        {
            dialog.Show();
        }
        return dialog._result;
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        _result = true;
        Close();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        _result = false;
        Close();
    }
}
