"""云同步：注册登录后两端合并账号、设置和用量。"""

from __future__ import annotations

import os
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "server"))
os.environ.setdefault("JWT_SECRET", "test-secret-for-sync-please-use-32b+")

from app.db import reset_for_tests  # noqa: E402
from fastapi.testclient import TestClient  # noqa: E402
from app.main import app  # noqa: E402


def _requester(client: TestClient):
    def _call(method: str, url: str, headers: dict | None, body: dict | None):
        from urllib.parse import urlparse

        path = urlparse(url).path
        res = client.request(method, path, headers=headers, json=body)
        return res.status_code, res.json()

    return _call


class CloudSyncTests(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = tempfile.TemporaryDirectory()
        reset_for_tests(Path(self.tmp.name) / "sync.db")
        self.client = TestClient(app)

    def tearDown(self) -> None:
        self.tmp.cleanup()

    def test_register_then_second_device_pulls(self) -> None:
        from accounts import upsert_account
        from cloud_sync import apply_session, login, register, reconcile

        http = _requester(self.client)
        tokens = register("a@harker.cn", "password1", requester=http)
        a = {
            "accounts": [],
            "active_account_id": "",
            "session_token": "",
            "refresh_interval_minutes": 12,
            "tray_display_mode": "dot",
            "deleted_accounts": [],
        }
        upsert_account(a, "user_01CLOUD%3A%3Ajwt.part.sig", label="云号", activate=True)
        from accounts import apply_snapshot_to_account

        apply_snapshot_to_account(a["accounts"][0], remaining=44, billing_cycle_end="2026-10-01T00:00:00.000Z")
        apply_session(a, "a@harker.cn", "password1", tokens)
        _, status = reconcile(a, requester=http)
        self.assertTrue(status["ok"], status["message"])
        self.assertTrue(status["pushed"])

        b = {
            "accounts": [],
            "active_account_id": "",
            "session_token": "",
            "refresh_interval_minutes": 10,
            "tray_display_mode": "ring",
            "deleted_accounts": [],
        }
        apply_session(b, "a@harker.cn", "password1", login("a@harker.cn", "password1", requester=http))
        _, status_b = reconcile(b, requester=http)
        self.assertTrue(status_b["ok"], status_b["message"])
        self.assertTrue(status_b["changed"])
        self.assertEqual(b["accounts"][0]["label"], "云号")
        self.assertEqual(b["accounts"][0]["last_remaining"], 44)
        self.assertEqual(b["accounts"][0]["billing_cycle_end"], "2026-10-01T00:00:00.000Z")
        self.assertEqual(b["refresh_interval_minutes"], 12)
        self.assertEqual(b["tray_display_mode"], "dot")

    def test_reconcile_retries_on_409(self) -> None:
        from urllib.parse import urlparse

        from account_sync import encrypt_envelope, snapshot_from_config
        from accounts import upsert_account
        from cloud_sync import reconcile

        local = {
            "accounts": [],
            "active_account_id": "",
            "session_token": "",
            "deleted_accounts": [],
            "sync_enabled": True,
            "sync_secret": "password1",
            "cloud_access_token": "access",
            "cloud_refresh_token": "refresh",
            "sync_device_id": "dev-local",
        }
        upsert_account(local, "user_01L%3A%3Ajwt.part.sig", label="本机", activate=True)

        remote_cfg = {
            "accounts": [],
            "active_account_id": "",
            "session_token": "",
            "deleted_accounts": [],
        }
        upsert_account(remote_cfg, "user_01R%3A%3Ajwt.part.sig", label="云端", activate=True)
        envelope = encrypt_envelope(snapshot_from_config(remote_cfg), "password1")
        calls: list[tuple[str, str]] = []
        responses = [
            (200, {"revision": 1, "envelope": envelope}),
            (409, {"detail": "revision conflict"}),
            (200, {"revision": 2, "envelope": envelope}),
            (200, {"revision": 3}),
        ]

        def requester(method, url, headers, body):
            calls.append((method, urlparse(url).path))
            return responses.pop(0)

        _, status = reconcile(local, requester=requester)
        self.assertTrue(status["ok"], status["message"])
        self.assertTrue(status["pushed"])
        self.assertEqual([c[0] for c in calls], ["GET", "PUT", "GET", "PUT"])
        self.assertEqual([c[1] for c in calls], ["/v1/sync"] * 4)
        ids = {a["id"] for a in local["accounts"]}
        self.assertEqual(ids, {"user_01L", "user_01R"})
        self.assertEqual(local["cloud_revision"], 3)

    def test_reconcile_401_refresh_failure(self) -> None:
        from urllib.parse import urlparse

        from cloud_sync import reconcile

        cfg = {
            "accounts": [],
            "active_account_id": "",
            "session_token": "",
            "deleted_accounts": [],
            "sync_enabled": True,
            "sync_secret": "password1",
            "cloud_access_token": "stale-access",
            "cloud_refresh_token": "stale-refresh",
            "cloud_email": "a@harker.cn",
            "sync_device_id": "dev-1",
        }
        logs: list[str] = []
        calls: list[tuple[str, str]] = []

        def logger(msg: str, *args: object, **_kwargs: object) -> None:
            logs.append(msg % args if args else msg)

        def requester(method, url, headers, body):
            calls.append((method, urlparse(url).path))
            if urlparse(url).path == "/v1/auth/refresh":
                return 401, {"detail": "refresh failed"}
            return 401, {"detail": "unauthorized"}

        _, status = reconcile(cfg, requester=requester, logger=logger)
        self.assertFalse(status["ok"])
        self.assertEqual(status["message"], "登录已过期，请重新登录")
        self.assertEqual(cfg["sync_last_error"], "登录已过期，请重新登录")
        self.assertFalse(cfg["sync_enabled"])
        self.assertEqual(cfg["cloud_access_token"], "")
        self.assertEqual(cfg["cloud_refresh_token"], "")
        self.assertEqual(cfg["sync_secret"], "")
        self.assertEqual([c[1] for c in calls], ["/v1/sync", "/v1/auth/refresh"])
        self.assertTrue(logs)
        self.assertIn("登录已过期", logs[0])

    def test_reconcile_wrong_passphrase(self) -> None:
        from account_sync import encrypt_envelope, snapshot_from_config
        from accounts import upsert_account
        from cloud_sync import reconcile

        remote_cfg = {
            "accounts": [],
            "active_account_id": "",
            "session_token": "",
            "deleted_accounts": [],
        }
        upsert_account(remote_cfg, "user_01R%3A%3Ajwt.part.sig", label="云端", activate=True)
        envelope = encrypt_envelope(snapshot_from_config(remote_cfg), "other-pass")
        cfg = {
            "accounts": [],
            "active_account_id": "",
            "session_token": "",
            "deleted_accounts": [],
            "sync_enabled": True,
            "sync_secret": "password1",
            "cloud_access_token": "access",
            "cloud_refresh_token": "refresh",
            "sync_device_id": "dev-1",
        }
        logs: list[str] = []

        def logger(msg: str, *args: object, **_kwargs: object) -> None:
            logs.append(msg % args if args else msg)

        def requester(method, url, headers, body):
            return 200, {"revision": 1, "envelope": envelope}

        _, status = reconcile(cfg, requester=requester, logger=logger)
        self.assertFalse(status["ok"])
        self.assertIn("口令", status["message"])
        self.assertEqual(cfg["sync_last_error"], status["message"])
        self.assertTrue(cfg["sync_enabled"])
        self.assertEqual(cfg["cloud_access_token"], "access")
        self.assertTrue(logs)
        self.assertIn("口令", logs[0])

    def test_change_password_reseals_cloud_blob(self) -> None:
        from accounts import upsert_account
        from cloud_sync import apply_session, change_password, login, reconcile, register

        http = _requester(self.client)
        tokens = register("pw@harker.cn", "password1", requester=http)
        a = {"accounts": [], "active_account_id": "", "session_token": "", "deleted_accounts": []}
        upsert_account(a, "user_01PW%3A%3Ajwt.part.sig", label="改密号", activate=True)
        apply_session(a, "pw@harker.cn", "password1", tokens)
        _, status = reconcile(a, requester=http)
        self.assertTrue(status["ok"], status["message"])

        change_password(a, "password1", "password2", requester=http)
        self.assertEqual(a["sync_secret"], "password2")
        self.assertTrue(a["cloud_access_token"])

        b = {"accounts": [], "active_account_id": "", "session_token": "", "deleted_accounts": []}
        apply_session(b, "pw@harker.cn", "password2", login("pw@harker.cn", "password2", requester=http))
        _, status_b = reconcile(b, requester=http)
        self.assertTrue(status_b["ok"], status_b["message"])
        self.assertEqual(b["accounts"][0]["label"], "改密号")

    def test_delete_account_wipes_cloud(self) -> None:
        from accounts import upsert_account
        from cloud_sync import apply_session, delete_account, login, reconcile, register

        http = _requester(self.client)
        tokens = register("gone@harker.cn", "password1", requester=http)
        a = {"accounts": [], "active_account_id": "", "session_token": "", "deleted_accounts": []}
        upsert_account(a, "user_01DEL%3A%3Ajwt.part.sig", label="注销号", activate=True)
        apply_session(a, "gone@harker.cn", "password1", tokens)
        reconcile(a, requester=http)
        delete_account(a, "password1", requester=http)
        self.assertFalse(a["sync_enabled"])
        self.assertEqual(a["cloud_email"], "")
        with self.assertRaises(Exception):
            login("gone@harker.cn", "password1", requester=http)


if __name__ == "__main__":
    unittest.main()
