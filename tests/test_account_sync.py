"""账号多端同步：合并规则、路径解析、加密信封与配置往返。"""

from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def _cases() -> dict:
    return json.loads((ROOT / "fixtures" / "account_sync_cases.json").read_text(encoding="utf-8"))


class ResolvePathTests(unittest.TestCase):
    def test_resolve_path_fixtures(self) -> None:
        from account_sync import resolve_sync_path

        for row in _cases()["resolve_path"]:
            got = resolve_sync_path(row["input"])
            if not row["output_suffix"]:
                self.assertEqual(got, "")
            else:
                self.assertTrue(got.endswith(row["output_suffix"]), f"{got} vs {row['output_suffix']}")


class MergeFixtureTests(unittest.TestCase):
    def test_merge_cases(self) -> None:
        from account_sync import apply_snapshot_to_config, merge_snapshots

        for cse in _cases()["merge"]:
            with self.subTest(cse["name"]):
                if cse.get("apply"):
                    cfg = json.loads(json.dumps(cse["config"]))
                    apply_snapshot_to_config(cfg, cse["snapshot"])
                    acc = cfg["accounts"][0]
                    exp = cse["expected"]
                    self.assertEqual(acc["label"], exp["label"])
                    self.assertEqual(acc["token"], exp["token"])
                    self.assertEqual(acc["membership_type"], exp["membership_type"])
                    self.assertEqual(acc["last_remaining"], exp["last_remaining"])
                    self.assertEqual(acc["alert_notified_levels"], exp["alert_notified_levels"])
                    self.assertTrue(acc["auth_error_notified"])
                    self.assertTrue(acc["low_quota_notified"])
                    continue
                merged = merge_snapshots(cse["local"], cse["remote"])
                exp = cse["expected"]
                self.assertEqual(merged["active_account_id"], exp["active_account_id"])
                ids = [a["id"] for a in merged["accounts"]]
                self.assertEqual(ids, exp["ids"])
                labels = {a["id"]: a["label"] for a in merged["accounts"]}
                tokens = {a["id"]: a["token"] for a in merged["accounts"]}
                self.assertEqual(labels, exp["labels"])
                self.assertEqual(tokens, exp["tokens"])
                if "emails" in exp:
                    emails = {a["id"]: a.get("email") or "" for a in merged["accounts"]}
                    self.assertEqual(emails, exp["emails"])
                if "passwords" in exp:
                    passwords = {a["id"]: a.get("password") or "" for a in merged["accounts"]}
                    self.assertEqual(passwords, exp["passwords"])
                self.assertEqual([d["id"] for d in merged["deleted"]], exp["deleted_ids"])
                if "settings" in exp:
                    self.assertEqual(merged["settings"]["refresh_interval_minutes"], exp["settings"]["refresh_interval_minutes"])
                    self.assertEqual(merged["settings"]["tray_display_mode"], exp["settings"]["tray_display_mode"])
                    self.assertEqual(merged["settings"]["notify_enabled"], exp["settings"]["notify_enabled"])
                    self.assertEqual(merged["settings"]["monthly_plan_usd"], exp["settings"]["monthly_plan_usd"])
                    if "alert_thresholds" in exp["settings"]:
                        self.assertEqual(merged["settings"]["alert_thresholds"], exp["settings"]["alert_thresholds"])
                    if "notify_exhaustion_risk" in exp["settings"]:
                        self.assertEqual(merged["settings"]["notify_exhaustion_risk"], exp["settings"]["notify_exhaustion_risk"])
                    if "usd_cny_rate" in exp["settings"]:
                        self.assertEqual(merged["settings"]["usd_cny_rate"], exp["settings"]["usd_cny_rate"])
                if "remaining" in exp:
                    got = {a["id"]: a.get("last_remaining") for a in merged["accounts"]}
                    self.assertEqual(got, exp["remaining"])
                if "billing_cycle_end" in exp:
                    got = {a["id"]: a.get("billing_cycle_end") for a in merged["accounts"]}
                    self.assertEqual(got, exp["billing_cycle_end"])
                if "usage_history_ts" in exp:
                    by_id = {row["account_id"]: [p["ts"] for p in row["history"]] for row in merged.get("usage") or []}
                    self.assertEqual(by_id, exp["usage_history_ts"])
                if "usage_event_ids" in exp:
                    by_id = {row["account_id"]: sorted(e["id"] for e in row["events"]) for row in merged.get("usage") or []}
                    self.assertEqual(by_id, exp["usage_event_ids"])
                if "usage_team_event_ids" in exp:
                    by_id = {row["account_id"]: sorted(e["id"] for e in row["team_events"]) for row in merged.get("usage") or []}
                    self.assertEqual(by_id, exp["usage_team_event_ids"])


class CryptoFixtureTests(unittest.TestCase):
    def test_known_vector_and_wrong_passphrase(self) -> None:
        from account_sync import decrypt_envelope, encrypt_envelope

        data = _cases()
        crypto = data["crypto"]
        envelope = encrypt_envelope(
            crypto["plaintext"],
            crypto["passphrase"],
            salt=__import__("base64").b64decode(crypto["salt"]),
            nonce=__import__("base64").b64decode(crypto["nonce"]),
            iterations=data["iterations"],
        )
        self.assertEqual(envelope["ciphertext"], crypto["ciphertext"])
        self.assertEqual(envelope["format"], data["format"])
        got = decrypt_envelope(
            {
                "format": data["format"],
                "kdf": data["kdf"],
                "iterations": data["iterations"],
                "salt": crypto["salt"],
                "nonce": crypto["nonce"],
                "ciphertext": crypto["ciphertext"],
            },
            crypto["passphrase"],
        )
        self.assertEqual(got["accounts"][0]["id"], "user_01A")
        self.assertEqual(got["accounts"][0]["token"], crypto["plaintext"]["accounts"][0]["token"])
        with self.assertRaises(ValueError):
            decrypt_envelope(
                {
                    "format": data["format"],
                    "kdf": data["kdf"],
                    "iterations": data["iterations"],
                    "salt": crypto["salt"],
                    "nonce": crypto["nonce"],
                    "ciphertext": crypto["ciphertext"],
                },
                "wrong-pass",
            )

    def test_gzip_envelope_roundtrip(self) -> None:
        from account_sync import SYNC_COMPRESS_MIN_BYTES, decrypt_envelope, encrypt_envelope

        accounts = [
            {
                "id": f"user_{i:02d}",
                "label": f"账号{i}",
                "token": f"tok-{i}-" + ("x" * 80),
                "membership_type": "pro",
                "sync_updated_at": "2026-09-09T00:00:00.000Z",
            }
            for i in range(12)
        ]
        payload = {
            "version": 1,
            "updated_at": "2026-09-09T00:00:00.000Z",
            "device_id": "dev-gzip",
            "active_account_id": "user_00",
            "accounts": accounts,
            "deleted": [],
            "settings": {
                "refresh_interval_minutes": 10,
                "alert_thresholds": [50, 20, 5],
                "notify_enabled": True,
                "notify_exhaustion_risk": True,
                "tray_display_mode": "ring",
                "monthly_plan_usd": 20,
                "usd_cny_rate": 7.5,
            },
        }
        envelope = encrypt_envelope(payload, "gzip-pass-123")
        self.assertGreaterEqual(
            len(__import__("json").dumps(payload, ensure_ascii=False, separators=(",", ":"), sort_keys=True).encode()),
            SYNC_COMPRESS_MIN_BYTES,
        )
        self.assertEqual(envelope.get("compression"), "gzip")
        got = decrypt_envelope(envelope, "gzip-pass-123")
        self.assertEqual(got["accounts"][0]["id"], "user_00")
        self.assertEqual(len(got["accounts"]), 12)
        self.assertEqual(got["settings"]["tray_display_mode"], "ring")

    def test_trim_snapshot_drops_oldest_usage(self) -> None:
        from account_sync import trim_snapshot_for_upload

        snap = {
            "version": 1,
            "updated_at": "2026-09-09T00:00:00.000Z",
            "active_account_id": "user_01A",
            "accounts": [],
            "deleted": [],
            "usage": [
                {
                    "account_id": "user_01A",
                    "history": [
                        {"ts": 1, "remaining": 90, "auto": None, "api": None},
                        {"ts": 2, "remaining": 80, "auto": None, "api": None},
                    ],
                    "events": [{"id": "old", "timestamp_ms": 1000, "model": "opus", "kind": "included", "tokens": 1}],
                    "team_events": [],
                }
            ],
        }
        trimmed = trim_snapshot_for_upload(snap, budget=80)
        self.assertLessEqual(len((trimmed.get("usage") or [{}])[0].get("history") or []) if trimmed.get("usage") else 0, 2)
        self.assertIn("usage", trimmed)
        from account_sync import append_trim_note, format_trim_note, is_trim_note, trim_note, usage_record_count

        self.assertGreater(usage_record_count(snap), usage_record_count(trimmed))
        note = trim_note(snap, budget=80)
        self.assertTrue(is_trim_note(note))
        self.assertIn("条最旧记录", note)
        self.assertEqual(append_trim_note("已上传到云端", note), "已上传到云端；" + note)
        self.assertEqual(format_trim_note(0), "")


class ExportImportTests(unittest.TestCase):
    def test_export_then_import_on_second_config(self) -> None:
        from account_sync import export_to_file, import_from_file
        from accounts import upsert_account

        with tempfile.TemporaryDirectory() as tmp:
            dest = Path(tmp) / "CursorRemain.accounts.sync"
            a = {
                "accounts": [],
                "active_account_id": "",
                "session_token": "",
                "sync_secret": "folder-pass-123",
                "refresh_interval_minutes": 15,
                "tray_display_mode": "number",
                "deleted_accounts": [],
            }
            upsert_account(a, "user_01SYNC%3A%3Ajwt.part.sig", label="工作", activate=True)
            export_to_file(a, str(dest))
            self.assertTrue(dest.is_file())

            b = {
                "accounts": [],
                "active_account_id": "",
                "session_token": "",
                "sync_secret": "folder-pass-123",
                "refresh_interval_minutes": 10,
                "tray_display_mode": "ring",
                "deleted_accounts": [],
            }
            import_from_file(b, str(dest))
            self.assertEqual(b["accounts"][0]["label"], "工作")
            self.assertEqual(b["active_account_id"], a["active_account_id"])
            self.assertEqual(b["refresh_interval_minutes"], 15)
            self.assertEqual(b["tray_display_mode"], "number")

    def test_channel_syncs_with_account(self) -> None:
        from account_sync import apply_snapshot_to_config, snapshot_account, snapshot_from_config
        from accounts import set_account_channel, upsert_account

        cfg: dict = {"accounts": [], "active_account_id": "", "session_token": "", "deleted_accounts": []}
        upsert_account(cfg, "user_01CHAN%3A%3Ajwt.part.sig", label="自费号", activate=True)
        set_account_channel(cfg, cfg["active_account_id"], "self_pay")
        snap = snapshot_from_config(cfg)
        self.assertEqual(snap["accounts"][0]["channel"], "self_pay")
        self.assertEqual(snapshot_account(snap["accounts"][0])["channel"], "self_pay")

        other: dict = {"accounts": [], "active_account_id": "", "session_token": "", "deleted_accounts": []}
        apply_snapshot_to_config(other, snap)
        self.assertEqual(other["accounts"][0]["channel"], "self_pay")

    def test_actual_cny_syncs_with_account(self) -> None:
        from account_sync import apply_snapshot_to_config, snapshot_account, snapshot_from_config
        from accounts import set_account_actual_cny, upsert_account

        cfg: dict = {"accounts": [], "active_account_id": "", "session_token": "", "deleted_accounts": []}
        upsert_account(cfg, "user_01COST%3A%3Ajwt.part.sig", label="企业", activate=True)
        set_account_actual_cny(cfg, cfg["active_account_id"], 88)
        snap = snapshot_from_config(cfg)
        self.assertEqual(snap["accounts"][0]["actual_cny"], 88)
        self.assertEqual(snapshot_account(snap["accounts"][0])["actual_cny"], 88)

        other: dict = {"accounts": [], "active_account_id": "", "session_token": "", "deleted_accounts": []}
        apply_snapshot_to_config(other, snap)
        self.assertEqual(other["accounts"][0]["actual_cny"], 88)
        self.assertEqual(other["actual_cny"], 88)

    def test_usage_history_and_events_roundtrip(self) -> None:
        import config
        from account_sync import apply_snapshot_to_config, snapshot_from_config
        from accounts import apply_snapshot_to_account, upsert_account
        from usage_history import load_points, replace_points
        from usage_report import event_to_dict, load_cached_events, save_cached_events, usage_event_from_dict

        old_dir = config.CONFIG_DIR
        with tempfile.TemporaryDirectory() as tmp:
            config.CONFIG_DIR = Path(tmp)
            try:
                cfg: dict = {"accounts": [], "active_account_id": "", "session_token": "", "deleted_accounts": []}
                upsert_account(cfg, "user_01USE%3A%3Ajwt.part.sig", label="用量", activate=True)
                aid = cfg["active_account_id"]
                apply_snapshot_to_account(cfg["accounts"][0], remaining=33.5, billing_cycle_end="2026-10-01T00:00:00.000Z")
                now_ts = __import__("time").time()
                replace_points([{"ts": now_ts, "remaining": 40, "auto": 8, "api": None}], account_id=aid, directory=Path(tmp))
                ev = usage_event_from_dict(
                    {
                        "id": "ev-sync",
                        "timestamp_ms": int(now_ts * 1000),
                        "model": "opus",
                        "kind": "included",
                        "tokens": 12,
                    }
                )
                self.assertIsNotNone(ev)
                save_cached_events([ev], aid, False, Path(tmp))
                snap = snapshot_from_config(cfg)
                self.assertEqual(snap["accounts"][0]["last_remaining"], 33.5)
                self.assertEqual(snap["usage"][0]["history"][0]["remaining"], 40)
                self.assertEqual(snap["usage"][0]["events"][0]["id"], "ev-sync")

                other_dir = Path(tmp) / "other"
                other_dir.mkdir()
                config.CONFIG_DIR = other_dir
                other: dict = {"accounts": [], "active_account_id": "", "session_token": "", "deleted_accounts": []}
                apply_snapshot_to_config(other, snap)
                self.assertEqual(other["accounts"][0]["last_remaining"], 33.5)
                self.assertEqual(other["accounts"][0]["billing_cycle_end"], "2026-10-01T00:00:00.000Z")
                hist = load_points(days=10_000, account_id=aid, directory=other_dir)
                self.assertEqual(hist[0]["remaining"], 40)
                loaded = load_cached_events(aid, False, other_dir)
                self.assertEqual(loaded[0].id, "ev-sync")
                self.assertEqual(event_to_dict(loaded[0])["tokens"], 12)
            finally:
                config.CONFIG_DIR = old_dir


class ConfigRoundtripTests(unittest.TestCase):
    def test_sync_fields_survive_save(self) -> None:
        import config

        old_dir = config.CONFIG_DIR
        old_path = config.CONFIG_PATH
        with tempfile.TemporaryDirectory() as tmp:
            config.CONFIG_DIR = Path(tmp)
            config.CONFIG_PATH = Path(tmp) / "config.json"
            try:
                cfg = dict(config.DEFAULT_CONFIG)
                cfg["sync_enabled"] = True
                cfg["cloud_email"] = "User@Harker.cn"
                cfg["cloud_access_token"] = "access"
                cfg["cloud_refresh_token"] = "refresh"
                cfg["sync_secret"] = "secret"
                cfg["deleted_accounts"] = [{"id": "user_gone", "deleted_at": "2026-09-01T00:00:00.000Z"}]
                config.save_config(cfg)
                loaded = config.load_config()
                self.assertTrue(loaded["sync_enabled"])
                self.assertEqual(loaded["cloud_email"], "user@harker.cn")
                self.assertEqual(loaded["sync_secret"], "secret")
                self.assertNotIn("sync_path", loaded)
                self.assertEqual(loaded["deleted_accounts"][0]["id"], "user_gone")
            finally:
                config.CONFIG_DIR = old_dir
                config.CONFIG_PATH = old_path


class LocalTimeTests(unittest.TestCase):
    def test_format_local_uses_system_timezone(self) -> None:
        from account_sync import format_local, parse_iso

        iso = "2026-09-14T12:37:36.998Z"
        got = format_local(iso)
        expect = parse_iso(iso).astimezone().strftime("%Y-%m-%d %H:%M:%S")
        self.assertEqual(got, expect)
        self.assertNotIn("Z", got)
        self.assertNotIn("T", got)
        self.assertEqual(format_local(""), "")
        self.assertEqual(format_local("not-a-date"), "not-a-date")


if __name__ == "__main__":
    unittest.main()
