namespace OpenConnectApp.Interfaces;

public interface IPrivilegedExecutor
{
    /// <summary>
    /// 管理者権限でシェルコマンドを実行し、標準出力を返す。
    /// 失敗時は例外（標準エラー出力を含む）をスローする。
    /// </summary>
    Task<string> RunAsync(string shellCommand, TimeSpan timeout);
}
