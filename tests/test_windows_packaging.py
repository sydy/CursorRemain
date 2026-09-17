"""Windows 发布必须是自包含包，并带上 VERSION 里的正式版本号。"""

from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


class SelfContainedPublishTests(unittest.TestCase):
    def test_csproj_is_self_contained(self) -> None:
        csproj = (ROOT / "windows" / "CursorRemain" / "CursorRemain.csproj").read_text(
            encoding="utf-8"
        )
        self.assertIn("<PublishSingleFile>true</PublishSingleFile>", csproj)
        self.assertIn("<SelfContained>true</SelfContained>", csproj)
        self.assertNotIn("<SelfContained>false</SelfContained>", csproj)
        self.assertIn("<EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>", csproj)
        self.assertNotIn("<EnableCompressionInSingleFile>false</EnableCompressionInSingleFile>", csproj)
        self.assertIn("<InvariantGlobalization>true</InvariantGlobalization>", csproj)

    def test_ci_publish_is_self_contained(self) -> None:
        workflow = (ROOT / ".github" / "workflows" / "build.yml").read_text(encoding="utf-8")
        self.assertIn("--self-contained true", workflow)
        self.assertNotIn("--self-contained false", workflow)
        self.assertIn("EnableCompressionInSingleFile=true", workflow)
        self.assertNotIn("EnableCompressionInSingleFile=false", workflow)
        self.assertIn("20MB", workflow)
        self.assertIn("120MB", workflow)
        self.assertIn("SourceRevisionId", workflow)
        self.assertIn("self-contained exe is unexpectedly small", workflow)
        bat = (ROOT / "build.bat").read_text(encoding="utf-8")
        self.assertIn("--self-contained true", bat)
        self.assertNotIn("--self-contained false", bat)

    def test_runtime_readme_does_not_require_dotnet(self) -> None:
        readme = ROOT / "windows" / "packaging" / "首次运行.txt"
        self.assertTrue(readme.is_file())
        text = readme.read_text(encoding="utf-8")
        self.assertIn("无需另装", text)
        self.assertNotIn("windowsdesktop-runtime-win-x64.exe", text)
        self.assertNotIn("Microsoft.WindowsDesktop.App", text)
        workflow = (ROOT / ".github" / "workflows" / "build.yml").read_text(encoding="utf-8")
        self.assertIn("首次运行.txt", workflow)
        self.assertIn("CursorRemain.exe", text)
        self.assertIn("CursorRemain-windows.zip", workflow)
        self.assertIn("CursorRemain-windows-setup.exe", workflow)
        self.assertIn("CursorTokenTray-windows.zip", workflow)


class WindowsInstallerPackagingTests(unittest.TestCase):
    def test_inno_script_is_per_user_self_contained(self) -> None:
        iss = (ROOT / "windows" / "packaging" / "setup.iss").read_text(encoding="utf-8")
        self.assertIn("OutputBaseFilename=CursorRemain-windows-setup", iss)
        self.assertIn("DefaultDirName={localappdata}\\Programs\\CursorRemain", iss)
        self.assertIn("PrivilegesRequired=lowest", iss)
        self.assertIn("CursorRemain.exe", iss)
        self.assertNotIn("windowsdesktop-runtime-win-x64.exe", iss)
        self.assertNotIn("Microsoft.WindowsDesktop.App", iss)
        self.assertIn("releases/latest", iss)
        self.assertIn("Local\\CursorTokenTray_SingleInstance_v2", iss)
        self.assertIn("ChineseSimplified.isl", iss)
        self.assertIn("CursorRemain", iss)
        self.assertIn("Software\\Microsoft\\Windows\\CurrentVersion\\Run", iss)

    def test_installer_language_file_is_present(self) -> None:
        isl = ROOT / "windows" / "packaging" / "ChineseSimplified.isl"
        self.assertTrue(isl.is_file())
        text = isl.read_text(encoding="utf-8-sig")
        self.assertIn("LanguageName=简体中文", text)
        self.assertIn("[Messages]", text)

    def test_ci_builds_and_publishes_installer(self) -> None:
        workflow = (ROOT / ".github" / "workflows" / "build.yml").read_text(encoding="utf-8")
        self.assertIn("build_installer.ps1", workflow)
        self.assertIn("innosetup", workflow)
        self.assertIn("CursorRemain-windows-setup.exe", workflow)
        self.assertGreaterEqual(workflow.count("CursorRemain-windows-setup.exe"), 3)
        notes = (ROOT / ".github" / "scripts" / "update-latest-release.sh").read_text(encoding="utf-8")
        self.assertIn("CursorRemain-windows-setup.exe", notes)
        official = (ROOT / ".github" / "scripts" / "publish-official-release.sh").read_text(
            encoding="utf-8"
        )
        self.assertIn("Cursor 余量", official)

    def test_installer_ps1_is_ascii(self) -> None:
        raw = (ROOT / "windows" / "packaging" / "build_installer.ps1").read_bytes()
        if raw.startswith(b"\xef\xbb\xbf"):
            raw = raw[3:]
        raw.decode("ascii")

    def test_program_mutex_matches_installer(self) -> None:
        program = (ROOT / "windows" / "CursorRemain" / "Program.cs").read_text(encoding="utf-8")
        iss = (ROOT / "windows" / "packaging" / "setup.iss").read_text(encoding="utf-8")
        self.assertIn('@"Local\\CursorTokenTray_SingleInstance_v2"', program)
        self.assertIn("Local\\CursorTokenTray_SingleInstance_v2", iss)


class ConfigDirMigrationTests(unittest.TestCase):
    def test_moves_legacy_folder_when_dest_missing(self) -> None:
        from config import migrate_legacy_config_dir

        with tempfile.TemporaryDirectory() as tmp:
            source = Path(tmp) / "CursorTokenTray"
            dest = Path(tmp) / "CursorRemain"
            source.mkdir()
            (source / "config.json").write_text("{\"ok\": true}", encoding="utf-8")
            self.assertTrue(migrate_legacy_config_dir(source, dest))
            self.assertTrue((dest / "config.json").is_file())
            self.assertFalse(source.exists())

    def test_keeps_dest_when_it_already_has_config(self) -> None:
        from config import migrate_legacy_config_dir

        with tempfile.TemporaryDirectory() as tmp:
            source = Path(tmp) / "CursorTokenTray"
            dest = Path(tmp) / "CursorRemain"
            source.mkdir()
            dest.mkdir()
            (source / "config.json").write_text("{\"from\": \"old\"}", encoding="utf-8")
            (dest / "config.json").write_text("{\"from\": \"new\"}", encoding="utf-8")
            self.assertFalse(migrate_legacy_config_dir(source, dest))
            self.assertEqual((dest / "config.json").read_text(encoding="utf-8"), "{\"from\": \"new\"}")
            self.assertTrue((source / "config.json").is_file())
