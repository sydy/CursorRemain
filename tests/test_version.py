"""VERSION 文件是正式版号的唯一来源，必须写进安装器、客户端和 CI。"""

from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VERSION_RE = re.compile(r"^\d+\.\d+\.\d+$")


def read_version() -> str:
    value = (ROOT / "VERSION").read_text(encoding="utf-8").strip()
    if not VERSION_RE.match(value):
        raise AssertionError(f"invalid VERSION: {value!r}")
    return value


class VersionConsistencyTests(unittest.TestCase):
    def test_version_file_is_semver(self) -> None:
        self.assertRegex(read_version(), VERSION_RE.pattern)

    def test_native_constants_match_version_file(self) -> None:
        version = read_version()
        swift = (ROOT / "macos" / "Sources" / "CursorTokenCore" / "AppUpdate.swift").read_text(
            encoding="utf-8"
        )
        plist = (ROOT / "macos" / "Resources" / "Info.plist").read_text(encoding="utf-8")
        iss = (ROOT / "windows" / "packaging" / "setup.iss").read_text(encoding="utf-8")
        self.assertIn(f'public static let productVersion = "{version}"', swift)
        self.assertIn(f"<string>{version}</string>", plist)
        self.assertGreaterEqual(plist.count(f"<string>{version}</string>"), 2)
        self.assertIn(f'#define AppVersion "{version}"', iss)

    def test_windows_reads_version_file(self) -> None:
        props = (ROOT / "windows" / "Directory.Build.props").read_text(encoding="utf-8")
        self.assertIn("VERSION", props)
        self.assertIn("ReadAllText", props)
        csproj = (ROOT / "windows" / "CursorRemain" / "CursorRemain.csproj").read_text(
            encoding="utf-8"
        )
        self.assertNotIn("<Version>2.0.0</Version>", csproj)
        self.assertIn("$(Version)+$(SourceRevisionId)", csproj)
        installer = (ROOT / "windows" / "packaging" / "build_installer.ps1").read_text(
            encoding="utf-8"
        )
        self.assertIn("VERSION", installer)
        self.assertIn("$AppVersion", installer)

    def test_ci_publishes_official_version(self) -> None:
        workflow = (ROOT / ".github" / "workflows" / "build.yml").read_text(encoding="utf-8")
        notes = (ROOT / ".github" / "scripts" / "update-latest-release.sh").read_text(
            encoding="utf-8"
        )
        official = (ROOT / ".github" / "scripts" / "publish-official-release.sh").read_text(
            encoding="utf-8"
        )
        self.assertIn("publish-official-release.sh", workflow)
        self.assertIn("tests.test_version", workflow)
        self.assertIn('TAG="v${VERSION}"', official)
        self.assertIn("--latest", official)
        self.assertIn("already shipped", official)
        self.assertIn("**版本**", notes)
        self.assertIn("releases/latest", notes)
        self.assertIn("正式版见", notes)

    def test_package_app_stamps_version(self) -> None:
        script = (ROOT / "macos" / "scripts" / "package_app.sh").read_text(encoding="utf-8")
        self.assertIn('REPO/VERSION', script)
        self.assertIn("CFBundleShortVersionString", script)
        self.assertIn("CFBundleVersion", script)
        self.assertIn("assert_portable_linkage", script)
        self.assertIn("normalize_linkage.sh", script)
        self.assertIn("libcursor_core.a", script)
        self.assertIn("libcursor_core.dylib", script)
        # Cargo does not always write deps/libcursor_core.dylib; an unguarded
        # mv under `set -e` aborted macOS packaging on CI.
        self.assertIn('[[ -e "$dylib" ]] || continue', script)

    def test_normalize_linkage_makes_app_portable(self) -> None:
        script = (ROOT / "macos" / "scripts" / "normalize_linkage.sh").read_text(encoding="utf-8")
        self.assertIn("install_name_tool -change", script)
        self.assertIn("@executable_path/../Frameworks/libcursor_core.dylib", script)
        self.assertIn("install_name_tool -id", script)
        self.assertIn("codesign --force --deep --sign", script)

    def test_macos_package_force_loads_static_core(self) -> None:
        manifest = (ROOT / "macos" / "Package.swift").read_text(encoding="utf-8")
        self.assertIn("-force_load", manifest)
        self.assertIn("-Xlinker", manifest)
        self.assertIn("libcursor_core.a", manifest)
        self.assertIn("#filePath", manifest)
        self.assertNotIn('.linkedLibrary("cursor_core"', manifest)

    def test_account_list_avoids_native_table_scrollers(self) -> None:
        # 原生 SwiftUI Table 在账号很少时也会同时冒出横向与纵向滚动条，
        # 把最后一行盖住；账号列表因此改为自绘行。
        view = (ROOT / "macos" / "Sources" / "CursorRemain" / "SettingsView.swift").read_text(encoding="utf-8")
        self.assertNotIn("Table(store.config.accounts", view)
        self.assertNotIn("AccountTableColumnFit", view)
        self.assertIn("private enum AccountTableLayout", view)
        self.assertIn("static let rowHeight", view)
        self.assertIn("var accountHeaderRow", view)
        self.assertIn("func accountRow(", view)

    def test_snapshot_json_round_trips_quota_fields(self) -> None:
        # Grok Bot 合并会经 Rust 往返一次快照；回传 JSON 缺字段就会把
        # auto/api/cents 清成 nil，悬浮窗只剩一条 Grok Bot 进度条。
        parser = (ROOT / "macos" / "Sources" / "CursorTokenCore" / "UsageParser.swift").read_text(encoding="utf-8")
        for key in (
            "auto_percent_used",
            "api_percent_used",
            "total_percent_used",
            "used_cents",
            "limit_cents",
            "remaining_cents",
            "billing_cycle_start",
            "billing_cycle_end",
            "limit_type",
            "prepaid_remaining_cents",
        ):
            self.assertIn(f'"{key}"', parser)


if __name__ == "__main__":
    unittest.main()
