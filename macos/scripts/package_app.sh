#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
REPO="$(cd "$ROOT/.." && pwd)"
cd "$ROOT"
swift build -c release --product CursorRemain
BIN="$(swift build -c release --show-bin-path)/CursorRemain"
DIST="$ROOT/dist/CursorRemain.app"
rm -rf "$DIST"
mkdir -p "$DIST/Contents/MacOS" "$DIST/Contents/Resources"
cp "$BIN" "$DIST/Contents/MacOS/CursorRemain"
cp "$ROOT/Resources/Info.plist" "$DIST/Contents/Info.plist"
VERSION="$(tr -d '[:space:]' < "$REPO/VERSION" 2>/dev/null || true)"
if [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  /usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString ${VERSION}" "$DIST/Contents/Info.plist"
  /usr/libexec/PlistBuddy -c "Set :CFBundleVersion ${VERSION}" "$DIST/Contents/Info.plist"
fi
SHA="${SOURCE_REVISION:-}"
if [[ -z "$SHA" ]]; then
  SHA="$(git -C "$REPO" rev-parse HEAD 2>/dev/null || true)"
fi
if [[ -n "$SHA" ]]; then
  /usr/libexec/PlistBuddy -c "Add :GitCommit string ${SHA}" "$DIST/Contents/Info.plist" 2>/dev/null \
    || /usr/libexec/PlistBuddy -c "Set :GitCommit ${SHA}" "$DIST/Contents/Info.plist"
fi
if [[ -f "$REPO/assets/app_icon.icns" ]]; then
  cp "$REPO/assets/app_icon.icns" "$DIST/Contents/Resources/AppIcon.icns"
  /usr/libexec/PlistBuddy -c 'Add :CFBundleIconFile string AppIcon' "$DIST/Contents/Info.plist" 2>/dev/null || true
elif [[ -f "$REPO/assets/app_icon.png" ]] && command -v sips >/dev/null; then
  ICONSET="$ROOT/dist/AppIcon.iconset"
  rm -rf "$ICONSET"
  mkdir -p "$ICONSET"
  for s in 16 32 128 256 512; do
    sips -z "$s" "$s" "$REPO/assets/app_icon.png" --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
    d=$((s * 2))
    sips -z "$d" "$d" "$REPO/assets/app_icon.png" --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
  done
  iconutil -c icns "$ICONSET" -o "$DIST/Contents/Resources/AppIcon.icns"
  /usr/libexec/PlistBuddy -c 'Add :CFBundleIconFile string AppIcon' "$DIST/Contents/Info.plist" 2>/dev/null || true
fi
chmod +x "$DIST/Contents/MacOS/CursorRemain"
# Ad-hoc sign so the bundle is a valid Mach-O app. Each build gets a new
# CDHash, so keychain items must not use the default app-bound ACL
# (see TokenProtector). Downloads still get Gatekeeper quarantine;
# 首次打开.command strips that attribute.
if command -v codesign >/dev/null; then
  codesign --force --deep --sign - "$DIST"
fi
STAGE="$ROOT/dist/release"
rm -rf "$STAGE"
mkdir -p "$STAGE"
cp -R "$DIST" "$STAGE/"
cat > "$STAGE/首次打开.command" << 'EOF'
#!/bin/bash
cd "$(dirname "$0")"
APP="CursorRemain.app"
if [[ ! -d "$APP" ]]; then
  osascript -e 'display alert "找不到 CursorRemain.app" message "请把本脚本和 App 放在同一文件夹后再双击。"' >/dev/null
  exit 1
fi
xattr -cr "$APP" >/dev/null 2>&1 || true
xattr -cr "$0" >/dev/null 2>&1 || true
open "$APP"
EOF
chmod +x "$STAGE/首次打开.command"
echo "Built $DIST"
echo "Release folder $STAGE"
