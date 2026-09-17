#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"

echo "使用 Swift 打包 macOS 应用。"
chmod +x macos/scripts/package_app.sh
./macos/scripts/package_app.sh
echo "产物: $(pwd)/macos/dist/CursorRemain.app"
echo "运行: open macos/dist/CursorRemain.app"
