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


if __name__ == "__main__":
    unittest.main()
