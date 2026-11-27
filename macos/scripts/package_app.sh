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
# Cargo 1.99 only writes libcursor_core.dylib at target/release (deps keeps
# hashed names), so guard each path: a bare `mv` under `set -e` killed
# packaging with "No such file or directory".
stash_dir="$REPO/core/target/release/.cursor-core-dylib-stash"
mkdir -p "$stash_dir"
shopt -s nullglob
for dylib in \
  "$REPO/core/target/release/libcursor_core.dylib" \
  "$REPO/core/target/release/deps/libcursor_core.dylib"
do
  [[ -e "$dylib" ]] || continue
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

# Only absolute load paths are fatal: cargo embeds the build machine's
# target/release path as LC_ID_DYLIB, which breaks launch on other Macs.
# An @... reference is fine because normalize_linkage.sh rewrites it to the
# bundled Frameworks copy below.
assert_portable_linkage() {
  local binary="$1"
  local bad
  bad="$(otool -L "$binary" | awk '/libcursor_core\.dylib/ && $1 !~ /^@/ {print $1}')"
  if [[ -n "$bad" ]]; then
    echo "error: $binary has an absolute libcursor_core load path:" >&2
    printf '%s\n' "$bad" >&2
    echo "macOS builds must force-load libcursor_core.a or bundle the dylib." >&2
    exit 1
  fi
}

DIST="$ROOT/dist/CursorRemain.app"
rm -rf "$DIST"
mkdir -p "$DIST/Contents/MacOS" "$DIST/Contents/Resources"
cp "$BIN" "$DIST/Contents/MacOS/CursorRemain"

# Swift sometimes still picks the cdylib over libcursor_core.a. Bundle it and
# let normalize_linkage.sh repoint the binary, so the .app boots on any Mac.
if otool -L "$BIN" | grep -q 'libcursor_core\.dylib'; then
  src_dylib=""
  for cand in \
    "$stash_dir/deps-libcursor_core.dylib" \
    "$stash_dir/release-libcursor_core.dylib" \
    "$REPO/core/target/release/libcursor_core.dylib"
  do
    if [[ -f "$cand" ]]; then
      src_dylib="$cand"
      break
    fi
  done
  if [[ -z "$src_dylib" ]]; then
    echo "error: binary links libcursor_core.dylib but no dylib is available to bundle" >&2
    exit 1
  fi
  mkdir -p "$DIST/Contents/Frameworks"
  cp "$src_dylib" "$DIST/Contents/Frameworks/libcursor_core.dylib"
  echo "note: bundling libcursor_core.dylib from $src_dylib"
fi

cp "$ROOT/Resources/Info.plist" "$DIST/Contents/Info.plist"
VERSION="$(tr -d '[:space:]' < "$REPO/VERSION" 2>/dev/null || true)"
# 每次发版的版本号 = 提交时间戳（北京时间 `YYYY.MM.DD.HHMM`）。CI 通过
# BUILD_VERSION 传入（与 Windows 同值），本地打包按当前提交时间推算；
# 都拿不到时退回 VERSION，避免打出一个没有版本号的包。
BUILD_VERSION="${BUILD_VERSION:-}"
if [[ -z "$BUILD_VERSION" ]]; then
  BUILD_VERSION="$(TZ=Asia/Shanghai git -C "$REPO" log -1 --format=%cd --date=format:'%Y.%m.%d.%H%M' 2>/dev/null || true)"
fi
if [[ "$BUILD_VERSION" =~ ^[0-9]{4}\.[0-9]{2}\.[0-9]{2}\.[0-9]{4}$ ]]; then
  # 展示用的发版版本号走 BuildVersion；CFBundleVersion 按 Apple 约定存构建号。
  if [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    /usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString ${VERSION}" "$DIST/Contents/Info.plist"
  fi
  /usr/libexec/PlistBuddy -c "Set :CFBundleVersion ${BUILD_VERSION}" "$DIST/Contents/Info.plist"
  /usr/libexec/PlistBuddy -c "Add :BuildVersion string ${BUILD_VERSION}" "$DIST/Contents/Info.plist" 2>/dev/null \
    || /usr/libexec/PlistBuddy -c "Set :BuildVersion ${BUILD_VERSION}" "$DIST/Contents/Info.plist"
elif [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
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
if [[ -f "$DIST/Contents/Frameworks/libcursor_core.dylib" ]]; then
  "$ROOT/scripts/normalize_linkage.sh" "$DIST"
fi
assert_portable_linkage "$DIST/Contents/MacOS/CursorRemain"
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
