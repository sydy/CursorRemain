"""明文明细：账号、删除记录、设置和用量各自一行。"""

from __future__ import annotations

import json

from fastapi import HTTPException

from .auth import now_iso
from .db import get_conn, lock
from .settings import MAX_STORE_BODY_BYTES


def empty_snapshot() -> dict:
    return {
        "version": 1,
        "updated_at": "",
        "device_id": "",
        "active_account_id": "",
        "active_account_updated_at": "",
        "accounts": [],
        "deleted": [],
        "settings": None,
        "usage": None,
    }


def read_snapshot(user_id: str) -> dict:
    with lock():
        conn = get_conn()
        meta = conn.execute("SELECT * FROM sync_store WHERE user_id = ?", (user_id,)).fetchone()
        if meta is None:
            return {"revision": 0, "updated_at": "", "snapshot": empty_snapshot()}
        accounts = []
        for row in conn.execute(
            "SELECT payload FROM sync_store_accounts WHERE user_id = ? ORDER BY account_id",
            (user_id,),
        ):
            item = _load_json(row["payload"])
            if isinstance(item, dict):
                accounts.append(item)
        deleted = [
            {"id": row["account_id"], "deleted_at": row["deleted_at"]}
            for row in conn.execute(
                "SELECT account_id, deleted_at FROM sync_store_deleted WHERE user_id = ? ORDER BY account_id",
                (user_id,),
            )
        ]
        usage = []
        for row in conn.execute(
            "SELECT payload FROM sync_store_usage WHERE user_id = ? ORDER BY account_id",
            (user_id,),
        ):
            item = _load_json(row["payload"])
            if isinstance(item, dict):
                usage.append(item)
        settings = _load_json(meta["settings_json"]) if meta["settings_json"] else None
        snapshot = {
            "version": 1,
            "updated_at": meta["updated_at"],
            "device_id": meta["device_id"],
            "active_account_id": meta["active_account_id"],
            "active_account_updated_at": meta["active_account_updated_at"],
            "accounts": accounts,
            "deleted": deleted,
            "settings": settings if isinstance(settings, dict) else None,
            "usage": usage or None,
        }
        return {"revision": int(meta["revision"]), "updated_at": meta["updated_at"], "snapshot": snapshot}


def write_snapshot(user_id: str, revision: int, snapshot: dict) -> dict:
    payload = json.dumps(snapshot, ensure_ascii=False, separators=(",", ":"))
    if len(payload.encode("utf-8")) > MAX_STORE_BODY_BYTES:
        raise HTTPException(status_code=413, detail="同步数据过大")
    accounts = _accounts(snapshot.get("accounts"))
    deleted = _deleted(snapshot.get("deleted"))
    usage = _usage(snapshot.get("usage"))
    settings = snapshot.get("settings") if isinstance(snapshot.get("settings"), dict) else None
    stamp = now_iso()
    with lock():
        conn = get_conn()
        row = conn.execute("SELECT revision FROM sync_store WHERE user_id = ?", (user_id,)).fetchone()
        current = int(row["revision"]) if row else 0
        if int(revision) != current:
            raise HTTPException(status_code=409, detail="同步冲突，请重试")
        next_rev = current + 1
        conn.execute("DELETE FROM sync_store_accounts WHERE user_id = ?", (user_id,))
        conn.execute("DELETE FROM sync_store_deleted WHERE user_id = ?", (user_id,))
        conn.execute("DELETE FROM sync_store_usage WHERE user_id = ?", (user_id,))
        for account_id, body in accounts:
            conn.execute(
                "INSERT INTO sync_store_accounts (user_id, account_id, payload) VALUES (?, ?, ?)",
                (user_id, account_id, json.dumps(body, ensure_ascii=False, separators=(",", ":"))),
            )
        for account_id, deleted_at in deleted:
            conn.execute(
                "INSERT INTO sync_store_deleted (user_id, account_id, deleted_at) VALUES (?, ?, ?)",
                (user_id, account_id, deleted_at),
            )
        for account_id, body in usage:
            conn.execute(
                "INSERT INTO sync_store_usage (user_id, account_id, payload) VALUES (?, ?, ?)",
                (user_id, account_id, json.dumps(body, ensure_ascii=False, separators=(",", ":"))),
            )
        settings_json = json.dumps(settings, ensure_ascii=False, separators=(",", ":")) if settings else ""
        values = (
            next_rev,
            stamp,
            str(snapshot.get("active_account_id") or ""),
            str(snapshot.get("active_account_updated_at") or ""),
            str(snapshot.get("device_id") or ""),
            settings_json,
            user_id,
        )
        if row is None:
            conn.execute(
                "INSERT INTO sync_store (revision, updated_at, active_account_id, active_account_updated_at, device_id, settings_json, user_id) "
                "VALUES (?, ?, ?, ?, ?, ?, ?)",
                values,
            )
        else:
            conn.execute(
                "UPDATE sync_store SET revision = ?, updated_at = ?, active_account_id = ?, active_account_updated_at = ?, "
                "device_id = ?, settings_json = ? WHERE user_id = ?",
                values,
            )
        conn.commit()
    stored = read_snapshot(user_id)
    stored["revision"] = next_rev
    stored["updated_at"] = stamp
    return stored


def _load_json(raw: str):
    try:
        return json.loads(raw)
    except json.JSONDecodeError:
        return None


def _accounts(raw) -> list[tuple[str, dict]]:
    rows = []
    if not isinstance(raw, list):
        return rows
    seen = set()
    for item in raw:
        if not isinstance(item, dict):
            continue
        account_id = str(item.get("id") or "").strip()
        if not account_id or account_id in seen:
            continue
        seen.add(account_id)
        rows.append((account_id, item))
    return rows


def _deleted(raw) -> list[tuple[str, str]]:
    rows = []
    if not isinstance(raw, list):
        return rows
    seen = set()
    for item in raw:
        if not isinstance(item, dict):
            continue
        account_id = str(item.get("id") or "").strip()
        deleted_at = str(item.get("deleted_at") or "").strip()
        if not account_id or not deleted_at or account_id in seen:
            continue
        seen.add(account_id)
        rows.append((account_id, deleted_at))
    return rows


def _usage(raw) -> list[tuple[str, dict]]:
    rows = []
    if not isinstance(raw, list):
        return rows
    seen = set()
    for item in raw:
        if not isinstance(item, dict):
            continue
        account_id = str(item.get("account_id") or "").strip()
        if not account_id or account_id in seen:
            continue
        seen.add(account_id)
        rows.append((account_id, item))
    return rows
