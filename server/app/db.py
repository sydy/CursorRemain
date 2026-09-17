from __future__ import annotations

import sqlite3
import threading
import time
from datetime import datetime, timezone
from pathlib import Path

from . import settings

_LOCK = threading.RLock()
_CONN: sqlite3.Connection | None = None


SCHEMA = """
CREATE TABLE IF NOT EXISTS users (
    id TEXT PRIMARY KEY,
    email TEXT NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    created_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS refresh_tokens (
    id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    token_hash TEXT NOT NULL UNIQUE,
    expires_at TEXT NOT NULL,
    revoked INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS sync_blobs (
    user_id TEXT PRIMARY KEY,
    revision INTEGER NOT NULL,
    envelope TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_users_email ON users(email);
CREATE INDEX IF NOT EXISTS idx_refresh_user ON refresh_tokens(user_id);
"""


BACKUP_KEEP = 7
BACKUP_INTERVAL_SEC = 86_400


def backup_dir(db_path: Path | None = None) -> Path:
    path = Path(db_path or settings.DATABASE_PATH)
    return path.parent / "backups"


def maybe_backup_db(conn: sqlite3.Connection | None = None, path: Path | None = None) -> Path | None:
    db_path = Path(path or settings.DATABASE_PATH)
    if not db_path.is_file() or db_path.stat().st_size <= 0:
        return None
    dest_dir = backup_dir(db_path)
    dest_dir.mkdir(parents=True, exist_ok=True)
    existing = sorted(dest_dir.glob("sync-*.db"), key=lambda p: p.stat().st_mtime, reverse=True)
    if existing and (time.time() - existing[0].stat().st_mtime) < BACKUP_INTERVAL_SEC:
        return None
    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    dest = dest_dir / f"sync-{stamp}.db"
    src = conn
    close_src = False
    if src is None:
        src = sqlite3.connect(str(db_path))
        close_src = True
    try:
        dst = sqlite3.connect(str(dest))
        try:
            src.backup(dst)
        finally:
            dst.close()
    finally:
        if close_src:
            src.close()
    keep = existing[: BACKUP_KEEP - 1]
    for old in existing:
        if old in keep:
            continue
        try:
            old.unlink()
        except OSError:
            pass
    return dest


def connect(path: Path | None = None) -> sqlite3.Connection:
    global _CONN
    db_path = Path(path or settings.DATABASE_PATH)
    db_path.parent.mkdir(parents=True, exist_ok=True)
    conn = sqlite3.connect(str(db_path), check_same_thread=False)
    conn.row_factory = sqlite3.Row
    conn.execute("PRAGMA journal_mode=WAL")
    conn.execute("PRAGMA foreign_keys=ON")
    conn.executescript(SCHEMA)
    conn.commit()
    try:
        maybe_backup_db(conn, db_path)
    except Exception:
        pass
    return conn


def get_conn() -> sqlite3.Connection:
    global _CONN
    with _LOCK:
        if _CONN is None:
            _CONN = connect()
        return _CONN


def reset_for_tests(path: Path) -> None:
    global _CONN
    with _LOCK:
        if _CONN is not None:
            _CONN.close()
            _CONN = None
        settings.DATABASE_PATH = path
        _CONN = connect(path)


def lock() -> threading.RLock:
    return _LOCK
