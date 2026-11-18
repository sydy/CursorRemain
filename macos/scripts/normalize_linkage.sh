#!/usr/bin/env bash
# Make a packaged CursorRemain.app portable.
#
# Cargo emits both libcursor_core.a and libcursor_core.dylib. When Swift links
# the dylib, the embedded LC_ID_DYLIB is the absolute build-machine path
# (e.g. /Users/runner/work/... on CI), so the .app dies at launch with
# "Library not loaded" on every other Mac. This rewrites those references to
# the bundled Frameworks copy and re-signs the bundle.
set -euo pipefail

APP="${1:?usage: normalize_linkage.sh /path/to/CursorRemain.app}"
BIN="$APP/Contents/MacOS/CursorRemain"
DYLIB="$APP/Contents/Frameworks/libcursor_core.dylib"
REL="@executable_path/../Frameworks/libcursor_core.dylib"

[[ -x "$BIN" ]] || { echo "error: missing executable $BIN" >&2; exit 1; }
[[ -f "$DYLIB" ]] || { echo "error: missing bundled dylib $DYLIB" >&2; exit 1; }

rewrote=0
while IFS= read -r ref; do
  [[ -z "$ref" ]] && continue
  case "$ref" in
    @*) continue ;;
  esac
  echo "rewrite load path: $ref -> $REL"
  install_name_tool -change "$ref" "$REL" "$BIN"
  rewrote=$((rewrote + 1))
done < <(otool -L "$BIN" | awk '/libcursor_core\.dylib/ {print $1}')

install_name_tool -id @rpath/libcursor_core.dylib "$DYLIB" 2>/dev/null || true
install_name_tool -add_rpath @executable_path/../Frameworks "$BIN" 2>/dev/null || true

if command -v codesign >/dev/null; then
  codesign --force --deep --sign - "$APP" >/dev/null
fi

bad="$(otool -L "$BIN" | awk '/libcursor_core\.dylib/ && $1 !~ /^@/ {print $1}')"
if [[ -n "$bad" ]]; then
  echo "error: load path is still absolute:" >&2
  printf '%s\n' "$bad" >&2
  exit 1
fi

if command -v codesign >/dev/null; then
  codesign --verify --deep --strict "$APP" >/dev/null 2>&1 \
    || { echo "error: signature invalid after rewrite" >&2; exit 1; }
fi

echo "ok: $BIN resolves libcursor_core from Frameworks (rewrites=$rewrote)"
