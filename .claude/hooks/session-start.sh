#!/bin/bash
set -euo pipefail

# Claude Code on the web 用の SessionStart フック。
# このリポジトリは .NET 10 SDK が必要だが、リモート実行環境には未インストールのため
# ここで導入する。ローカル環境では何もしない。
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

# すでに .NET 10 SDK が入っていれば再インストールしない（冪等性）。
if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  echo "[session-start] .NET 10 SDK は既にインストール済みです。"
  exit 0
fi

echo "[session-start] .NET 10 SDK をインストールします..."
export DEBIAN_FRONTEND=noninteractive
sudo apt-get update -qq || apt-get update -qq
sudo apt-get install -y dotnet-sdk-10.0 || apt-get install -y dotnet-sdk-10.0

echo "[session-start] 完了: $(dotnet --version)"
