#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
REPO="$(cd "$ROOT/.." && pwd)"
cd "$ROOT"

cargo build --release --manifest-path "$REPO/core/Cargo.toml"

STATIC_LIB="$REPO/core/target/release/libcursor_core.a"
if [[ ! -f "$STATIC_LIB" ]]; then
  echo "error: missing $STATIC_LIB (staticlib required for macOS packaging)" >&2
  exit 1
fi

# Cargo also emits a cdylib for Windows. Hide it before Swift links so a
# future Package.swift regression cannot silently pick the dylib again.
stash_dir="$REPO/core/target/release/.cursor-core-dylib-stash"
mkdir -p "$stash_dir"
shopt -s nullglob
for dylib in \
  "$REPO/core/target/release/libcursor_core.dylib" \
  "$REPO/core/target/release/deps/libcursor_core.dylib"
do
  base="$(basename "$(dirname "$dylib")")-$(basename "$dylib")"
  mv -f "$dylib" "$stash_dir/$base"
done
shopt -u nullglob
restore_dylibs() {
  shopt -s nullglob
  for stashed in "$stash_dir"/*; do
    name="$(basename "$stashed")"
    case "$name" in
      deps-*) mv -f "$stashed" "$REPO/core/target/release/deps/${name#deps-}" ;;
      release-*) mv -f "$stashed" "$REPO/core/target/release/${name#release-}" ;;
      *) mv -f "$stashed" "$REPO/core/target/release/$name" ;;
    esac
  done
  shopt -u nullglob
  rmdir "$stash_dir" 2>/dev/null || true
}
trap restore_dylibs EXIT

swift build -c release --product CursorRemain
BIN="$(swift build -c release --show-bin-path)/CursorRemain"

assert_no_cursor_core_dylib() {
  local binary="$1"
  local deps
  deps="$(otool -L "$binary")"
  if printf '%s\n' "$deps" | grep -E 'libcursor_core\.dylib|/Users/runner/' >/dev/null; then
    echo "error: $binary still depends on libcursor_core.dylib or a CI absolute path:" >&2
    printf '%s\n' "$deps" >&2
    echo "macOS builds must force-load libcursor_core.a so Release zips run on other Macs." >&2
    exit 1
  fi
}
assert_no_cursor_core_dylib "$BIN"

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
assert_no_cursor_core_dylib "$DIST/Contents/MacOS/CursorRemain"
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
