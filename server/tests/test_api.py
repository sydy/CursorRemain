from __future__ import annotations

import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

os.environ.setdefault("JWT_SECRET", "test-secret-for-sync-please-use-32b+")

from fastapi.testclient import TestClient

from app import settings
from app.auth import LIMITER
from app.db import reset_for_tests
from app.main import app
from app.settings import DEV_JWT_FALLBACK, JWT_SECRET_MIN_LEN, resolve_jwt_secret


ENVELOPE = {
    "format": "cursortokentray.sync.v2",
    "kdf": "pbkdf2-sha256",
    "iterations": 210000,
    "salt": "AQEBAQEBAQEBAQEBAQEBAQ==",
    "nonce": "AgICAgICAgICAgIC",
    "ciphertext": "AAAAAAAAAAAAAAAAAAAAAA==",
}


class ApiTests(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = tempfile.TemporaryDirectory()
        reset_for_tests(Path(self.tmp.name) / "sync.db")
        LIMITER.reset()
        self.client = TestClient(app)

    def tearDown(self) -> None:
        self.tmp.cleanup()

    def test_health(self) -> None:
        res = self.client.get("/health")
        self.assertEqual(res.status_code, 200)
        self.assertTrue(res.json()["ok"])

    def test_register_login_sync_conflict(self) -> None:
        bad = self.client.post("/v1/auth/register", json={"email": "not-email", "password": "password1"})
        self.assertEqual(bad.status_code, 400)

        short = self.client.post("/v1/auth/register", json={"email": "a@b.com", "password": "123"})
        self.assertEqual(short.status_code, 400)

        created = self.client.post(
            "/v1/auth/register",
            json={"email": "User@Harker.cn", "password": "password1"},
        )
        self.assertEqual(created.status_code, 200, created.text)
        tokens = created.json()
        self.assertEqual(tokens["email"], "user@harker.cn")
        access = tokens["access_token"]

        again = self.client.post(
            "/v1/auth/register",
            json={"email": "user@harker.cn", "password": "password1"},
        )
        self.assertEqual(again.status_code, 409)

        wrong = self.client.post(
            "/v1/auth/login",
            json={"email": "user@harker.cn", "password": "password2"},
        )
        self.assertEqual(wrong.status_code, 401)

        logged = self.client.post(
            "/v1/auth/login",
            json={"email": "user@harker.cn", "password": "password1"},
        )
        self.assertEqual(logged.status_code, 200)
        access = logged.json()["access_token"]
        refresh = logged.json()["refresh_token"]

        me = self.client.get("/v1/me", headers={"Authorization": f"Bearer {access}"})
        self.assertEqual(me.status_code, 200)
        self.assertEqual(me.json()["email"], "user@harker.cn")

        empty = self.client.get("/v1/sync", headers={"Authorization": f"Bearer {access}"})
        self.assertEqual(empty.status_code, 200)
        self.assertEqual(empty.json()["revision"], 0)
        self.assertIsNone(empty.json()["envelope"])

        put = self.client.put(
            "/v1/sync",
            headers={"Authorization": f"Bearer {access}"},
            json={"revision": 0, "envelope": ENVELOPE},
        )
        self.assertEqual(put.status_code, 200, put.text)
        self.assertEqual(put.json()["revision"], 1)

        conflict = self.client.put(
            "/v1/sync",
            headers={"Authorization": f"Bearer {access}"},
            json={"revision": 0, "envelope": ENVELOPE},
        )
        self.assertEqual(conflict.status_code, 409)

        got = self.client.get("/v1/sync", headers={"Authorization": f"Bearer {access}"})
        self.assertEqual(got.json()["revision"], 1)
        self.assertEqual(got.json()["envelope"]["format"], ENVELOPE["format"])
        self.assertNotIn("accounts", got.json()["envelope"])

        refreshed = self.client.post("/v1/auth/refresh", json={"refresh_token": refresh})
        self.assertEqual(refreshed.status_code, 200)
        new_access = refreshed.json()["access_token"]
        me2 = self.client.get("/v1/me", headers={"Authorization": f"Bearer {new_access}"})
        self.assertEqual(me2.status_code, 200)

        self.client.post("/v1/auth/logout", json={"refresh_token": refreshed.json()["refresh_token"]})
        reused = self.client.post("/v1/auth/refresh", json={"refresh_token": refreshed.json()["refresh_token"]})
        self.assertEqual(reused.status_code, 401)

    def test_rejects_plaintext_blob(self) -> None:
        created = self.client.post(
            "/v1/auth/register",
            json={"email": "b@harker.cn", "password": "password1"},
        )
        access = created.json()["access_token"]
        res = self.client.put(
            "/v1/sync",
            headers={"Authorization": f"Bearer {access}"},
            json={"revision": 0, "envelope": {"accounts": [{"token": "secret"}]}},
        )
        self.assertEqual(res.status_code, 400)

    def _register(self, email: str = "limit@harker.cn") -> str:
        created = self.client.post(
            "/v1/auth/register",
            json={"email": email, "password": "password1"},
        )
        self.assertEqual(created.status_code, 200, created.text)
        return created.json()["access_token"]

    def test_rejects_weak_jwt_secret(self) -> None:
        with self.assertRaises(RuntimeError):
            resolve_jwt_secret("", allow_dev=False)
        with self.assertRaises(RuntimeError):
            resolve_jwt_secret("   ", allow_dev=False)
        with self.assertRaises(RuntimeError):
            resolve_jwt_secret(DEV_JWT_FALLBACK, allow_dev=False)
        with self.assertRaises(RuntimeError):
            resolve_jwt_secret("short-secret", allow_dev=False)
        self.assertEqual(resolve_jwt_secret("", allow_dev=True), DEV_JWT_FALLBACK)
        self.assertEqual(resolve_jwt_secret(DEV_JWT_FALLBACK, allow_dev=True), DEV_JWT_FALLBACK)
        strong = "S" * JWT_SECRET_MIN_LEN
        self.assertEqual(resolve_jwt_secret(strong, allow_dev=False), strong)

    def test_import_rejects_empty_jwt_secret(self) -> None:
        server_root = Path(__file__).resolve().parents[1]
        env = os.environ.copy()
        env.pop("ALLOW_DEV_JWT", None)
        env["JWT_SECRET"] = ""
        env["PYTHONPATH"] = str(server_root)
        denied = subprocess.run(
            [sys.executable, "-c", "from app.settings import JWT_SECRET"],
            cwd=server_root,
            env=env,
            capture_output=True,
            text=True,
        )
        self.assertNotEqual(denied.returncode, 0, denied.stderr)
        self.assertIn("JWT_SECRET", denied.stderr)

        env["ALLOW_DEV_JWT"] = "1"
        allowed = subprocess.run(
            [sys.executable, "-c", "from app.settings import JWT_SECRET; print(JWT_SECRET)"],
            cwd=server_root,
            env=env,
            capture_output=True,
            text=True,
        )
        self.assertEqual(allowed.returncode, 0, allowed.stderr)
        self.assertEqual(allowed.stdout.strip(), DEV_JWT_FALLBACK)

    def test_rejects_iterations_above_cap(self) -> None:
        access = self._register("iter@harker.cn")
        too_high = {**ENVELOPE, "iterations": settings.MAX_KDF_ITERATIONS + 1}
        res = self.client.put(
            "/v1/sync",
            headers={"Authorization": f"Bearer {access}"},
            json={"revision": 0, "envelope": too_high},
        )
        self.assertEqual(res.status_code, 400)
        self.assertIn("迭代", res.json()["detail"])

        at_cap = {**ENVELOPE, "iterations": settings.MAX_KDF_ITERATIONS}
        ok = self.client.put(
            "/v1/sync",
            headers={"Authorization": f"Bearer {access}"},
            json={"revision": 0, "envelope": at_cap},
        )
        self.assertEqual(ok.status_code, 200, ok.text)

    def test_rejects_oversized_ciphertext(self) -> None:
        access = self._register("cipher@harker.cn")
        huge = {**ENVELOPE, "ciphertext": "A" * 80}
        with patch.object(settings, "MAX_CIPHERTEXT_CHARS", 32):
            res = self.client.put(
                "/v1/sync",
                headers={"Authorization": f"Bearer {access}"},
                json={"revision": 0, "envelope": huge},
            )
        self.assertEqual(res.status_code, 413)
        self.assertIn("密文", res.json()["detail"])

    def test_rejects_oversized_sync_body(self) -> None:
        access = self._register("body@harker.cn")
        huge = {**ENVELOPE, "ciphertext": "A" * 800}
        with patch.object(settings, "MAX_SYNC_BODY_BYTES", 256):
            res = self.client.put(
                "/v1/sync",
                headers={"Authorization": f"Bearer {access}"},
                json={"revision": 0, "envelope": huge},
            )
        self.assertEqual(res.status_code, 413)
        self.assertIn("过大", res.json()["detail"])

    def test_accepts_gzip_compression_field(self) -> None:
        access = self._register("gzip@harker.cn")
        env = {**ENVELOPE, "compression": "gzip"}
        res = self.client.put(
            "/v1/sync",
            headers={"Authorization": f"Bearer {access}"},
            json={"revision": 0, "envelope": env},
        )
        self.assertEqual(res.status_code, 200, res.text)
        got = self.client.get("/v1/sync", headers={"Authorization": f"Bearer {access}"})
        self.assertEqual(got.json()["envelope"]["compression"], "gzip")

        bad = self.client.put(
            "/v1/sync",
            headers={"Authorization": f"Bearer {access}"},
            json={"revision": 1, "envelope": {**ENVELOPE, "compression": "lz4"}},
        )
        self.assertEqual(bad.status_code, 400)

    def test_change_password_reseals_and_revokes_refresh(self) -> None:
        created = self.client.post(
            "/v1/auth/register",
            json={"email": "pw@harker.cn", "password": "password1"},
        )
        access = created.json()["access_token"]
        refresh = created.json()["refresh_token"]
        put = self.client.put(
            "/v1/sync",
            headers={"Authorization": f"Bearer {access}"},
            json={"revision": 0, "envelope": ENVELOPE},
        )
        self.assertEqual(put.status_code, 200)

        missing = self.client.post(
            "/v1/auth/password",
            headers={"Authorization": f"Bearer {access}"},
            json={"old_password": "password1", "new_password": "password2", "revision": 1},
        )
        self.assertEqual(missing.status_code, 400)

        changed = self.client.post(
            "/v1/auth/password",
            headers={"Authorization": f"Bearer {access}"},
            json={
                "old_password": "password1",
                "new_password": "password2",
                "revision": 1,
                "envelope": ENVELOPE,
            },
        )
        self.assertEqual(changed.status_code, 200, changed.text)
        self.assertEqual(changed.json()["revision"], 2)
        self.assertTrue(changed.json()["access_token"])
        reused = self.client.post("/v1/auth/refresh", json={"refresh_token": refresh})
        self.assertEqual(reused.status_code, 401)

        old_login = self.client.post(
            "/v1/auth/login",
            json={"email": "pw@harker.cn", "password": "password1"},
        )
        self.assertEqual(old_login.status_code, 401)
        new_login = self.client.post(
            "/v1/auth/login",
            json={"email": "pw@harker.cn", "password": "password2"},
        )
        self.assertEqual(new_login.status_code, 200)

    def test_delete_account_wipes_blob(self) -> None:
        created = self.client.post(
            "/v1/auth/register",
            json={"email": "gone@harker.cn", "password": "password1"},
        )
        access = created.json()["access_token"]
        self.client.put(
            "/v1/sync",
            headers={"Authorization": f"Bearer {access}"},
            json={"revision": 0, "envelope": ENVELOPE},
        )
        wrong = self.client.request(
            "DELETE",
            "/v1/me",
            headers={"Authorization": f"Bearer {access}"},
            json={"password": "password2"},
        )
        self.assertEqual(wrong.status_code, 401)
        gone = self.client.request(
            "DELETE",
            "/v1/me",
            headers={"Authorization": f"Bearer {access}"},
            json={"password": "password1"},
        )
        self.assertEqual(gone.status_code, 200, gone.text)
        me = self.client.get("/v1/me", headers={"Authorization": f"Bearer {access}"})
        self.assertEqual(me.status_code, 401)
        again = self.client.post(
            "/v1/auth/login",
            json={"email": "gone@harker.cn", "password": "password1"},
        )
        self.assertEqual(again.status_code, 401)

    def test_purge_revoked_refresh_tokens(self) -> None:
        from app.auth import purge_refresh_tokens
        from app.db import get_conn

        created = self.client.post(
            "/v1/auth/register",
            json={"email": "purge@harker.cn", "password": "password1"},
        )
        refresh = created.json()["refresh_token"]
        self.client.post("/v1/auth/logout", json={"refresh_token": refresh})
        removed = purge_refresh_tokens()
        self.assertGreaterEqual(removed, 1)
        row = get_conn().execute("SELECT COUNT(*) AS n FROM refresh_tokens").fetchone()
        self.assertEqual(int(row["n"]), 0)

    def test_sqlite_backup_creates_copy(self) -> None:
        from app import db

        self._register("bak@harker.cn")
        with patch.object(db, "BACKUP_INTERVAL_SEC", 0):
            dest = db.maybe_backup_db()
        self.assertIsNotNone(dest)
        self.assertTrue(dest.is_file())


if __name__ == "__main__":
    unittest.main()
