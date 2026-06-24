# openconnect-app

macOS 向け `openconnect` GUI ラッパーアプリ。接続先を一覧から選択して VPN 接続/切断できます。

## 機能

- 接続先をCSVで一括管理
- ユーザー名・パスワードを macOS Keychain（または平文）に保存
- 接続/切断/状態監視をGUIで操作
- `openconnect` / `vpnc-script` のパスを自動検出

## 必要なもの

- macOS (Apple Silicon, `osx-arm64`)
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- `openconnect` コマンド（例: `brew install openconnect`）

## ビルド / パッケージング

`.app` バンドルとしてパッケージするには、付属スクリプトを使います。

```bash
./scripts/package-macos.sh
```

これは `dotnet publish` を実行した後、`OpenConnectApp.app` バンドルを生成します。

ビルド成果物: `OpenConnectApp/bin/Release/net10.0/osx-arm64/publish/OpenConnectApp.app`

> **重要:** 素の実行ファイル（`.../publish/OpenConnectApp`）を直接起動すると、
> macOS が Terminal 経由で起動するためターミナルが開きます。
> 必ず `.app` バンドルを起動してください（ターミナルは開きません）。
> 実行中のステータスやログは、アプリの「ログ」タブから確認できます。

publish のみ行いたい場合は従来どおり次でも可能です（ただし `.app` 化推奨）。

```bash
cd OpenConnectApp
dotnet publish -r osx-arm64 -c Release --self-contained true -p:PublishSingleFile=true
```

## 起動

```bash
open OpenConnectApp/bin/Release/net10.0/osx-arm64/publish/OpenConnectApp.app
```

または Finder から `OpenConnectApp.app` をダブルクリックします。

## 初回起動

macOS の Gatekeeper により「開発元が未確認」と表示される場合は、以下を実行してください。

```bash
xattr -cr /path/to/OpenConnectApp.app
```

## 設定ファイルの場所

すべての設定・ログは以下のディレクトリに保存されます。

```
~/Library/Application Support/OpenConnectApp/
├── config.json           # アプリ設定（パス、認証ストア種別など）
├── connections.csv       # 接続先一覧（手動編集可）
├── credentials.plain.json  # 平文モード時のみ生成
├── openconnect.pid       # 接続中のプロセスPID
├── openconnect.log       # openconnect コマンドの出力
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
| `ServerCert` | - | `--servercert` に渡す値（証明書ピン留め。下記参照） |

## サーバ証明書の扱い（セキュリティ注意）

本アプリは初回接続時に、接続先サーバの証明書を取得して **自動的にピン留め（TOFU:
Trust On First Use）** し、以後はそのピンで検証します。これにより未検証（自己署名や
社内CAなど `signer not found`）の証明書でもプロンプトなしで接続できます。ピンは
`~/Library/Application Support/OpenConnectApp/config.json` の `serverCertPins` に
ホストごとに保存されます。

サーバ証明書が変わった場合は、新しい証明書を **自動で再ピン留め** して接続を継続し、
「ログ」タブに警告を残します。なお、この方式は **中間者攻撃を検知できない**
（提示された証明書をそのまま信頼する）というセキュリティ上のトレードオフがあります。

特定の接続先を厳格に固定したい場合は、CSV の `ServerCert` 列に `pin-sha256:...`
（接続失敗時のログに表示される値）を設定してください。手動指定は自動ピン留めより
優先され、不一致なら接続は拒否されます。
