# openconnect-app

macOS 向け `openconnect` GUI ラッパーアプリ。接続先を一覧から選択して VPN 接続/切断できます。

## 機能

- 接続先をCSVで一括管理
- ユーザー名・パスワードを macOS Keychain（または平文）に保存
- 接続/切断/状態監視をGUIで操作
- `openconnect` / `vpnc-script` のパスを自動検出

## 必要なもの

- macOS (Apple Silicon, `osx-arm64`)
- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- `openconnect` コマンド（例: `brew install openconnect`）

## ビルド

```bash
cd OpenConnectApp
dotnet publish -r osx-arm64 -c Release --self-contained true -p:PublishSingleFile=true
```

ビルド成果物: `bin/Release/net9.0/osx-arm64/publish/OpenConnectApp`

## 初回起動

macOS の Gatekeeper により「開発元が未確認」と表示される場合は、以下を実行してください。

```bash
xattr -cr /path/to/OpenConnectApp
```

## 設定ファイルの場所

すべての設定・ログは以下のディレクトリに保存されます。

```
~/Library/Application Support/OpenConnectApp/
├── config.json           # アプリ設定（パス、認証ストア種別など）
├── connections.csv       # 接続先一覧（手動編集可）
├── credentials.plain.json  # 平文モード時のみ生成
├── openconnect.pid       # 接続中のプロセスPID
└── app.log               # 実行ログ
```

## CSV形式

```csv
DisplayName,Host,UserGroup,Protocol,ServerCert
社内VPN,vpn.example.com,,,
```

| 列 | 必須 | 説明 |
|---|---|---|
| `DisplayName` | ✅ | 一覧に表示する名前 |
| `Host` | ✅ | 接続先ホスト名/IP |
| `UserGroup` | - | `--usergroup` に渡す値 |
| `Protocol` | - | `--protocol` に渡す値（空の場合は anyconnect） |
| `ServerCert` | - | `--servercert` に渡す値 |
