#!/usr/bin/env bash
set -euo pipefail

# OpenConnectApp を macOS の .app バンドルとしてパッケージするスクリプト。
#
# 素の実行ファイル（bin/.../publish/OpenConnectApp）を直接起動すると
# macOS が Terminal 経由で起動してしまいターミナルが開く。
# .app バンドル化して起動することで、ターミナルなしで GUI が立ち上がる。

RID="${RID:-osx-arm64}"
CONFIG="${CONFIG:-Release}"
APP_NAME="OpenConnectApp"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR/../OpenConnectApp"
cd "$PROJECT_DIR"

echo "==> dotnet publish ($CONFIG / $RID)"
dotnet publish -r "$RID" -c "$CONFIG" --self-contained true -p:PublishSingleFile=true

PUBLISH_DIR="bin/$CONFIG/net10.0/$RID/publish"
APP_BUNDLE="$PUBLISH_DIR/$APP_NAME.app"

echo "==> .app バンドルを構築: $APP_BUNDLE"
rm -rf "$APP_BUNDLE"
mkdir -p "$APP_BUNDLE/Contents/MacOS"
mkdir -p "$APP_BUNDLE/Contents/Resources"

cp "Info.plist" "$APP_BUNDLE/Contents/Info.plist"

# publish 成果物（実行ファイル＋同梱ネイティブライブラリ）を MacOS/ へコピーする。
# 生成中の .app 自身はコピー対象から除外する。
find "$PUBLISH_DIR" -maxdepth 1 -mindepth 1 ! -name "$APP_NAME.app" \
    -exec cp -R {} "$APP_BUNDLE/Contents/MacOS/" \;

chmod +x "$APP_BUNDLE/Contents/MacOS/$APP_NAME"

# 未署名アプリの Gatekeeper 隔離属性を除去する。
xattr -cr "$APP_BUNDLE" || true

echo "==> 完了: $APP_BUNDLE"
echo "    Finder からダブルクリック、または次で起動できます:"
echo "    open \"$APP_BUNDLE\""
