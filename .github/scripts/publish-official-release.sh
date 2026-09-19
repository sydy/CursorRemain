#!/usr/bin/env bash
# Create the official v{VERSION} GitHub release on the first main build of
# that version, or attach this platform's assets when the other job created
# the same-SHA release first. Later main commits only update `latest`.
set -euo pipefail

if [[ -z "${GITHUB_SHA:-}" || -z "${GITHUB_REPOSITORY:-}" ]]; then
  echo "GITHUB_SHA and GITHUB_REPOSITORY are required" >&2
  exit 1
fi
if [[ "$#" -lt 1 ]]; then
  echo "usage: $0 <asset> [asset...]" >&2
  exit 1
fi

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
VERSION="$(tr -d '[:space:]' < "${ROOT}/VERSION")"
if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Invalid VERSION: ${VERSION}" >&2
  exit 1
fi
TAG="v${VERSION}"
SHORT_SHA="${GITHUB_SHA:0:7}"
SUBJECT="$(git -C "${ROOT}" log -1 --format=%s "${GITHUB_SHA}" | tr -d '\r')"
TIME_UTC="$(date -u +"%Y-%m-%d %H:%M UTC")"
SERVER_URL="${GITHUB_SERVER_URL:-https://github.com}"
RUN_URL="${SERVER_URL}/${GITHUB_REPOSITORY}/actions/runs/${GITHUB_RUN_ID}"
COMMIT_URL="${SERVER_URL}/${GITHUB_REPOSITORY}/commit/${GITHUB_SHA}"
NOTES_FILE="${GITHUB_WORKSPACE:-$ROOT}/official-release-notes.md"

same_sha_release() {
  local body
  body="$(gh release view "$TAG" --json body -q .body 2>/dev/null || true)"
  grep -Fq "${GITHUB_SHA}" <<<"${body}"
}

write_notes() {
  {
    printf '%s\n' "Cursor 余量 **${VERSION}** 正式版。"
    printf '%s\n' ""
    printf '%s\n' "- **版本**: \`${VERSION}\`"
    printf '%s\n' "- **提交**: [\`${SHORT_SHA}\`](${COMMIT_URL}) \`${GITHUB_SHA}\`"
    printf '%s\n' "- **说明**: ${SUBJECT}"
    printf '%s\n' "- **打包时间**: ${TIME_UTC}"
    printf '%s\n' "- **构建**: [Actions run ${GITHUB_RUN_ID}](${RUN_URL})"
    printf '%s\n' ""
    printf '%s\n' "Windows 安装版与 zip 为自包含包，无需另装 .NET。自动更新在本机已有 .NET 8 Desktop Runtime 时会下载不含运行时的 \`CursorRemain-windows-light.zip\`；否则仍下自包含 zip。macOS 为 Swift \`.app\`。"
    printf '%s\n' "从浏览器下载的 macOS 包若提示「已损坏」，请双击 zip 内的「首次打开.command」，或执行 \`xattr -cr CursorRemain.app\`。"
  } > "${NOTES_FILE}"
}

upload_assets() {
  gh release upload "$TAG" "$@" --clobber
}

write_notes

if gh release view "$TAG" >/dev/null 2>&1; then
  if same_sha_release; then
    echo "Official ${TAG} already exists for ${SHORT_SHA}; attaching assets"
    upload_assets "$@"
    exit 0
  fi
  echo "Official ${TAG} already shipped; leaving it unchanged"
  exit 0
fi

if gh release create "$TAG" \
  --title "Cursor 余量 ${VERSION}" \
  --notes-file "${NOTES_FILE}" \
  --latest \
  --target "${GITHUB_SHA}" \
  "$@"; then
  echo "Created official ${TAG} (${SHORT_SHA})"
  exit 0
fi

sleep 3
if same_sha_release; then
  echo "Official ${TAG} was created by the other job; attaching assets"
  upload_assets "$@"
  exit 0
fi

echo "Could not create or attach official ${TAG}" >&2
exit 1
