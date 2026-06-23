using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using OpenConnectApp.Services;

namespace OpenConnectApp.ViewModels;

public partial class LogViewModel : ViewModelBase
{
    private const int MaxLines = 1000;
    private const int InitialLines = 500;

    private readonly LogService _logService;

    public ObservableCollection<string> Lines { get; } = [];

    /// <summary>行が追加されたことをViewに通知する（自動スクロール用）。</summary>
    public event Action? LinesChanged;

    public LogViewModel(LogService logService)
    {
        _logService = logService;

        foreach (var line in _logService.ReadRecent(InitialLines))
            Lines.Add(line);

        _logService.LineAppended += OnLineAppended;
    }

    private void OnLineAppended(string line)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // openconnect出力など複数行メッセージにも対応する
            foreach (var l in line.Split('\n'))
                Lines.Add(l.TrimEnd('\r'));

            while (Lines.Count > MaxLines)
                Lines.RemoveAt(0);

            LinesChanged?.Invoke();
        });
    }

    [RelayCommand]
    private void Clear() => Lines.Clear();
}
