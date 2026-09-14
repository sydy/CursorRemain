"""Windows 发布必须是框架依赖，不能把 CLR/BCL 打进包。"""

from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


class FrameworkDependentPublishTests(unittest.TestCase):
    def test_csproj_is_framework_dependent(self) -> None:
        csproj = (ROOT / "windows" / "CursorTokenTray" / "CursorTokenTray.csproj").read_text(
            encoding="utf-8"
        )
        self.assertIn("<PublishSingleFile>true</PublishSingleFile>", csproj)
        self.assertIn("<SelfContained>false</SelfContained>", csproj)
        self.assertNotIn("<SelfContained>true</SelfContained>", csproj)
        self.assertIn("<EnableCompressionInSingleFile>false</EnableCompressionInSingleFile>", csproj)
        self.assertNotIn("<EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>", csproj)
        self.assertIn("<InvariantGlobalization>true</InvariantGlobalization>", csproj)
        self.assertIn("<RollForward>LatestMinor</RollForward>", csproj)

    def test_ci_publish_is_framework_dependent(self) -> None:
        workflow = (ROOT / ".github" / "workflows" / "build.yml").read_text(encoding="utf-8")
        self.assertIn("--self-contained false", workflow)
        self.assertNotIn("--self-contained true", workflow)
        self.assertIn("coreclr", workflow)
        self.assertIn("15MB", workflow)

    def test_runtime_readme_is_shipped(self) -> None:
        readme = ROOT / "windows" / "packaging" / "首次运行.txt"
        self.assertTrue(readme.is_file())
        text = readme.read_text(encoding="utf-8")
        self.assertIn("Desktop Runtime", text)
        self.assertIn("windowsdesktop-runtime-win-x64.exe", text)
        self.assertIn("Microsoft.WindowsDesktop.App", text)
        workflow = (ROOT / ".github" / "workflows" / "build.yml").read_text(encoding="utf-8")
        self.assertIn("首次运行.txt", workflow)
