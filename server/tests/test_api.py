from __future__ import annotations

import base64
import json
import os
import sqlite3
import subprocess
import sys
import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path
from unittest.mock import patch

# Ensure server package directory is on sys.path even when pytest runs from workspace root
SERVER_DIR = Path(__file__).resolve().parent.parent
if str(SERVER_DIR) not in sys.path:
    sys.path.insert(0, str(SERVER_DIR))

os.environ.setdefault("JWT_SECRET", "test-secret-for-sync-please-use-32b+")

from fastapi import HTTPException
from fastapi.testclient import TestClient

from app import settings
from app.auth import LIMITER, RateLimiter
from app.db import close_for_tests, reset_for_tests
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
        self.sent_codes: dict[tuple[str, str], str] = {}
        self.mail_patch = patch("app.main.send_code", side_effect=self._capture_mail)
        self.mail_patch.start()
        self.client = TestClient(app)

    def tearDown(self) -> None:
        self.mail_patch.stop()
        close_for_tests()
        try:
            self.tmp.cleanup()
        except OSError:
            pass

    def _capture_mail(self, email: str, code: str, purpose: str) -> None:
        self.sent_codes[(email, purpose)] = code

    def _verify(self, email: str) -> None:
        result = self.client.post(
            "/v1/auth/verify-email",
            json={"email": email, "code": self.sent_codes[(email.lower(), "verify")]},
        )
        self.assertEqual(result.status_code, 200, result.text)

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
        self.assertTrue(created.json()["ok"])
        blocked = self.client.post(
            "/v1/auth/login",
            json={"email": "user@harker.cn", "password": "password1"},
        )
        self.assertEqual(blocked.status_code, 403)
        self._verify("user@harker.cn")

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
        access = self._register("b@harker.cn")
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
        self._verify(email)
        logged = self.client.post("/v1/auth/login", json={"email": email, "password": "password1"})
        self.assertEqual(logged.status_code, 200, logged.text)
        self.last_tokens = logged.json()
        return self.last_tokens["access_token"]

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
            encoding="utf-8",
            errors="replace",
        )
        self.assertNotEqual(denied.returncode, 0, denied.stderr)
        self.assertIn("JWT_SECRET", denied.stderr or "")

        env["ALLOW_DEV_JWT"] = "1"
        allowed = subprocess.run(
            [sys.executable, "-c", "from app.settings import JWT_SECRET; print(JWT_SECRET)"],
            cwd=server_root,
            env=env,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
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
        access = self._register("pw@harker.cn")
        refresh = self.last_tokens["refresh_token"]
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
        access = self._register("gone@harker.cn")
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

        self._register("purge@harker.cn")
        refresh = self.last_tokens["refresh_token"]
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

    def test_server_store_and_optional_envelope(self) -> None:
        access = self._register("store@harker.cn")
        headers = {"Authorization": f"Bearer {access}"}
        self.assertEqual(self.last_tokens["sync_storage"], "server")
        me = self.client.get("/v1/me", headers=headers)
        self.assertEqual(me.json()["sync_storage"], "server")

        empty = self.client.get("/v1/store", headers=headers)
        self.assertEqual(empty.status_code, 200, empty.text)
        self.assertEqual(empty.json()["revision"], 0)
        self.assertEqual(empty.json()["snapshot"]["accounts"], [])

        snap = {
            "version": 1,
            "device_id": "dev",
            "active_account_id": "acc",
            "accounts": [{"id": "acc", "label": "明细", "token": "secret-token"}],
            "deleted": [{"id": "old", "deleted_at": "2026-01-01T00:00:00Z"}],
            "settings": {"refresh_interval_minutes": 12},
            "usage": [{"account_id": "acc", "events": [{"at": "2026-01-02T00:00:00Z"}]}],
        }
        put = self.client.put("/v1/store", headers=headers, json={"revision": 0, "snapshot": snap})
        self.assertEqual(put.status_code, 200, put.text)
        self.assertEqual(put.json()["revision"], 1)
        got = self.client.get("/v1/store", headers=headers)
        stored = got.json()["snapshot"]
        self.assertEqual(stored["accounts"][0]["token"], "secret-token")
        self.assertEqual(stored["accounts"][0]["label"], "明细")
        self.assertEqual(stored["deleted"][0]["id"], "old")
        self.assertEqual(stored["settings"]["refresh_interval_minutes"], 12)
        self.assertEqual(stored["usage"][0]["account_id"], "acc")

        conflict = self.client.put("/v1/store", headers=headers, json={"revision": 0, "snapshot": snap})
        self.assertEqual(conflict.status_code, 409)

        bad = self.client.put("/v1/me/storage", headers=headers, json={"mode": "folder"})
        self.assertEqual(bad.status_code, 400)
        switched = self.client.put("/v1/me/storage", headers=headers, json={"mode": "envelope"})
        self.assertEqual(switched.status_code, 200, switched.text)
        self.assertEqual(switched.json()["sync_storage"], "envelope")
        again = self.client.post("/v1/auth/login", json={"email": "store@harker.cn", "password": "password1"})
        self.assertEqual(again.json()["sync_storage"], "envelope")
        blob = self.client.get("/v1/sync", headers={"Authorization": f"Bearer {again.json()['access_token']}"})
        self.assertIsNone(blob.json()["envelope"])
        rows = self.client.get("/v1/store", headers=headers)
        self.assertEqual(rows.json()["snapshot"]["accounts"][0]["label"], "明细")


    # ---- 落库加密 ----

    def _sql(self, statement: str, params: tuple = ()) -> None:
        conn = sqlite3.connect(str(settings.DATABASE_PATH))
        try:
            conn.execute(statement, params)
            conn.commit()
        finally:
            conn.close()

    def _rows(self, table: str, user_id: str) -> list[sqlite3.Row]:
        conn = sqlite3.connect(str(settings.DATABASE_PATH))
        conn.row_factory = sqlite3.Row
        try:
            return conn.execute(f"SELECT * FROM {table} WHERE user_id = ?", (user_id,)).fetchall()
        finally:
            conn.close()

    def _db_bytes(self) -> bytes:
        path = Path(settings.DATABASE_PATH)
        data = path.read_bytes()
        wal = path.with_name(path.name + "-wal")
        if wal.exists():
            data += wal.read_bytes()
        return data

    def _store_user(self, email: str) -> tuple[str, dict, str]:
        access = self._register(email)
        headers = {"Authorization": f"Bearer {access}"}
        user_id = self.client.get("/v1/me", headers=headers).json()["id"]
        return access, headers, user_id

    def _snapshot(self) -> dict:
        return {
            "version": 1,
            "device_id": "dev",
            "active_account_id": "acc",
            "accounts": [{"id": "acc", "label": "明细", "token": "secret-token"}],
            "settings": {"refresh_interval_minutes": 12},
            "usage": [{"account_id": "acc", "events": [{"at": "2026-01-02T00:00:00Z"}]}],
        }

    def test_store_payload_is_encrypted_at_rest(self) -> None:
        _, headers, user_id = self._store_user("cipher@harker.cn")
        put = self.client.put("/v1/store", headers=headers, json={"revision": 0, "snapshot": self._snapshot()})
        self.assertEqual(put.status_code, 200, put.text)

        accounts = self._rows("sync_store_accounts", user_id)
        self.assertEqual(len(accounts), 1)
        self.assertTrue(accounts[0]["payload"].startswith("enc1:"))
        self.assertNotIn("secret-token", accounts[0]["payload"])

        usage = self._rows("sync_store_usage", user_id)
        self.assertTrue(usage[0]["payload"].startswith("enc1:"))
        self.assertNotIn("2026-01-02", usage[0]["payload"])

        meta = self._rows("sync_store", user_id)
        self.assertTrue(meta[0]["settings_json"].startswith("enc1:"))
        self.assertNotIn("refresh_interval_minutes", meta[0]["settings_json"])

        self.assertNotIn(b"secret-token", self._db_bytes())

        got = self.client.get("/v1/store", headers=headers).json()["snapshot"]
        self.assertEqual(got["accounts"][0]["token"], "secret-token")
        self.assertEqual(got["settings"]["refresh_interval_minutes"], 12)
        self.assertEqual(got["usage"][0]["account_id"], "acc")

    def test_legacy_plaintext_rows_readable_then_encrypted(self) -> None:
        _, headers, user_id = self._store_user("legacy@harker.cn")
        self._sql(
            "INSERT INTO sync_store (user_id, revision, updated_at, settings_json) VALUES (?, 0, ?, ?)",
            (user_id, "2026-01-01T00:00:00Z", json.dumps({"refresh_interval_minutes": 5})),
        )
        self._sql(
            "INSERT INTO sync_store_accounts (user_id, account_id, payload) VALUES (?, ?, ?)",
            (user_id, "acc", json.dumps({"id": "acc", "label": "旧", "token": "legacy-token"})),
        )

        got = self.client.get("/v1/store", headers=headers)
        self.assertEqual(got.status_code, 200, got.text)
        self.assertEqual(got.json()["snapshot"]["accounts"][0]["token"], "legacy-token")
        self.assertEqual(got.json()["snapshot"]["settings"]["refresh_interval_minutes"], 5)

        again = self.client.put("/v1/store", headers=headers, json={"revision": 0, "snapshot": got.json()["snapshot"]})
        self.assertEqual(again.status_code, 200, again.text)
        row = self._rows("sync_store_accounts", user_id)[0]
        self.assertTrue(row["payload"].startswith("enc1:"))
        self.assertNotIn("legacy-token", row["payload"])
        self.assertEqual(
            self.client.get("/v1/store", headers=headers).json()["snapshot"]["accounts"][0]["token"],
            "legacy-token",
        )

    def test_tampered_ciphertext_surfaces_error(self) -> None:
        _, headers, user_id = self._store_user("tamper@harker.cn")
        put = self.client.put("/v1/store", headers=headers, json={"revision": 0, "snapshot": self._snapshot()})
        self.assertEqual(put.status_code, 200, put.text)
        bad = "enc1:" + base64.urlsafe_b64encode(b"\x00" * 12 + b"garbage").decode("ascii")
        self._sql("UPDATE sync_store_accounts SET payload = ? WHERE user_id = ?", (bad, user_id))
        res = self.client.get("/v1/store", headers=headers)
        self.assertEqual(res.status_code, 500)
        self.assertIn("损坏", res.text)


class RateLimiterTests(unittest.TestCase):
    def test_sweep_drops_stale_and_empty_buckets(self) -> None:
        limiter = RateLimiter()
        base = 1_700_000_000.0
        limiter._hits = {"fresh": [base + 90], "stale": [base - 30], "empty": []}
        with patch("app.auth.now") as fake:
            fake.return_value = datetime.fromtimestamp(base + 120, timezone.utc)
            limiter._sweep()
        self.assertEqual(set(limiter._hits), {"fresh"})

    def test_key_table_stays_bounded_and_still_limits(self) -> None:
        limiter = RateLimiter()
        limiter.MAX_KEYS = 2
        limiter._hits = {"a": [1.0], "b": [1.0]}
        limiter.check("c", 5)
        self.assertIn("c", limiter._hits)
        self.assertLessEqual(len(limiter._hits), 2)
        limiter.reset()
        with self.assertRaises(HTTPException):
            limiter.check("c", 1)
            limiter.check("c", 1)
        limiter.reset()


if __name__ == "__main__":
    unittest.main()
