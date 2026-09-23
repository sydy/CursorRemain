from __future__ import annotations

import hashlib
import logging
import re
import secrets
import sqlite3
import threading
import uuid
from datetime import datetime, timedelta, timezone

import jwt
from argon2 import PasswordHasher
from argon2.exceptions import VerifyMismatchError
from fastapi import HTTPException, Request

from . import settings
from .db import get_conn, lock

EMAIL_RE = re.compile(r"^[^@\s]+@[^@\s]+\.[^@\s]+$")
HASHER = PasswordHasher()
log = logging.getLogger("sync")


def now() -> datetime:
    return datetime.now(timezone.utc)


def now_iso(stamp: datetime | None = None) -> str:
    current = stamp or now()
    return current.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%f")[:-3] + "Z"


def normalize_email(email: str) -> str:
    return (email or "").strip().lower()


def validate_email(email: str) -> str:
    value = normalize_email(email)
    if not EMAIL_RE.match(value):
        raise HTTPException(status_code=400, detail="请填写有效邮箱")
    return value


def validate_password(password: str) -> str:
    secret = password or ""
    if len(secret) < settings.PASSWORD_MIN:
        raise HTTPException(status_code=400, detail=f"密码至少 {settings.PASSWORD_MIN} 位")
    if len(secret) > settings.PASSWORD_MAX:
        raise HTTPException(status_code=400, detail="密码过长")
    return secret


def hash_password(password: str) -> str:
    return HASHER.hash(password)


def verify_password(password_hash: str, password: str) -> bool:
    try:
        HASHER.verify(password_hash, password)
        return True
    except (VerifyMismatchError, ValueError):
        return False


def client_ip(request: Request) -> str:
    if settings.TRUST_PROXY:
        forwarded = (request.headers.get("x-forwarded-for") or "").split(",")[0].strip()
        if forwarded:
            return forwarded
    if request.client and request.client.host:
        return request.client.host
    return "unknown"


class RateLimiter:
    def __init__(self) -> None:
        self._hits: dict[str, list[float]] = {}
        self._lock = threading.Lock()

    def check(self, key: str, limit: int) -> None:
        window = now().timestamp()
        cutoff = window - 60
        with self._lock:
            if len(self._hits) > 4096:
                stale = [name for name, hits in self._hits.items() if not hits or hits[-1] <= cutoff]
                for name in stale:
                    del self._hits[name]
            bucket = [t for t in self._hits.get(key, []) if t > cutoff]
            if len(bucket) >= limit:
                self._hits[key] = bucket
                raise HTTPException(status_code=429, detail="请求过于频繁，请稍后再试")
            bucket.append(window)
            self._hits[key] = bucket

    def reset(self) -> None:
        with self._lock:
            self._hits.clear()


LIMITER = RateLimiter()


def create_access_token(user_id: str, email: str, token_version: int = 0) -> str:
    payload = {
        "sub": user_id,
        "email": email,
        "type": "access",
        "tv": int(token_version),
        "exp": now() + timedelta(minutes=settings.ACCESS_MINUTES),
        "iat": now(),
    }
    return jwt.encode(payload, settings.JWT_SECRET, algorithm="HS256")


def decode_access(token: str) -> dict:
    try:
        payload = jwt.decode(token, settings.JWT_SECRET, algorithms=["HS256"])
    except jwt.ExpiredSignatureError as exc:
        raise HTTPException(status_code=401, detail="登录已过期，请重新登录") from exc
    except jwt.InvalidTokenError as exc:
        raise HTTPException(status_code=401, detail="请先登录云同步") from exc
    if payload.get("type") != "access" or not payload.get("sub"):
        raise HTTPException(status_code=401, detail="请先登录云同步")
    return payload


def hash_refresh(raw: str) -> str:
    return hashlib.sha256(raw.encode("utf-8")).hexdigest()


def issue_refresh(user_id: str) -> str:
    raw = secrets.token_urlsafe(48)
    token_id = str(uuid.uuid4())
    expires = now() + timedelta(days=settings.REFRESH_DAYS)
    with lock():
        conn = get_conn()
        conn.execute(
            "INSERT INTO refresh_tokens (id, user_id, token_hash, expires_at, revoked) VALUES (?, ?, ?, ?, 0)",
            (token_id, user_id, hash_refresh(raw), now_iso(expires)),
        )
        conn.commit()
    return raw


def maybe_purge_refresh() -> None:
    try:
        purge_refresh_tokens()
    except Exception:
        log.exception("清理过期 refresh token 失败")


def revoke_refresh(raw: str) -> None:
    digest = hash_refresh(raw)
    with lock():
        conn = get_conn()
        conn.execute("UPDATE refresh_tokens SET revoked = 1 WHERE token_hash = ?", (digest,))
        conn.commit()


def rotate_refresh(raw: str) -> tuple[str, str]:
    digest = hash_refresh(raw)
    with lock():
        conn = get_conn()
        row = conn.execute(
            "SELECT id, user_id, expires_at, revoked FROM refresh_tokens WHERE token_hash = ?",
            (digest,),
        ).fetchone()
        if row is None or int(row["revoked"]) == 1:
            raise HTTPException(status_code=401, detail="登录已过期，请重新登录")
        try:
            exp = datetime.fromisoformat(str(row["expires_at"]).replace("Z", "+00:00"))
        except ValueError as exc:
            raise HTTPException(status_code=401, detail="登录已过期，请重新登录") from exc
        if exp < now():
            conn.execute("UPDATE refresh_tokens SET revoked = 1 WHERE id = ?", (row["id"],))
            conn.commit()
            raise HTTPException(status_code=401, detail="登录已过期，请重新登录")
        conn.execute("UPDATE refresh_tokens SET revoked = 1 WHERE id = ?", (row["id"],))
        conn.commit()
        user_id = str(row["user_id"])
    user = get_user(user_id)
    if user is None:
        raise HTTPException(status_code=401, detail="登录已过期，请重新登录")
    return create_access_token(user["id"], user["email"], int(user.get("token_version") or 0)), issue_refresh(user["id"])


def get_user(user_id: str) -> dict | None:
    with lock():
        row = get_conn().execute(
            "SELECT id, email, password_hash, created_at, token_version FROM users WHERE id = ?",
            (user_id,),
        ).fetchone()
    return dict(row) if row else None


def get_user_by_email(email: str) -> dict | None:
    with lock():
        row = get_conn().execute(
            "SELECT id, email, password_hash, created_at, token_version FROM users WHERE email = ?",
            (email,),
        ).fetchone()
    return dict(row) if row else None


def create_user(email: str, password: str) -> dict:
    with lock():
        if get_conn().execute("SELECT 1 FROM users WHERE email = ?", (email,)).fetchone():
            raise HTTPException(status_code=409, detail="该邮箱已注册")
    user = {
        "id": str(uuid.uuid4()),
        "email": email,
        "password_hash": hash_password(password),
        "created_at": now_iso(),
        "token_version": 0,
    }
    try:
        with lock():
            conn = get_conn()
            conn.execute(
                "INSERT INTO users (id, email, password_hash, created_at, token_version) VALUES (?, ?, ?, ?, 0)",
                (user["id"], user["email"], user["password_hash"], user["created_at"]),
            )
            conn.commit()
    except sqlite3.IntegrityError as exc:
        raise HTTPException(status_code=409, detail="该邮箱已注册") from exc
    return user


def purge_refresh_tokens() -> int:
    stamp = now_iso()
    with lock():
        conn = get_conn()
        cur = conn.execute(
            "DELETE FROM refresh_tokens WHERE revoked = 1 OR expires_at < ?",
            (stamp,),
        )
        conn.commit()
        return int(cur.rowcount or 0)


def delete_user(user_id: str) -> None:
    with lock():
        conn = get_conn()
        conn.execute("DELETE FROM sync_blobs WHERE user_id = ?", (user_id,))
        conn.execute("DELETE FROM refresh_tokens WHERE user_id = ?", (user_id,))
        conn.execute("DELETE FROM users WHERE id = ?", (user_id,))
        conn.commit()


def bearer_user(request: Request) -> dict:
    header = request.headers.get("authorization") or ""
    if not header.lower().startswith("bearer "):
        raise HTTPException(status_code=401, detail="请先登录云同步")
    payload = decode_access(header[7:].strip())
    user = get_user(str(payload["sub"]))
    if user is None:
        raise HTTPException(status_code=401, detail="请先登录云同步")
    try:
        presented = int(payload.get("tv") or 0)
    except (TypeError, ValueError):
        presented = -1
    if presented != int(user.get("token_version") or 0):
        raise HTTPException(status_code=401, detail="登录已过期，请重新登录")
    return user


def token_payload(user: dict, refresh: str) -> dict:
    return {
        "access_token": create_access_token(user["id"], user["email"], int(user.get("token_version") or 0)),
        "refresh_token": refresh,
        "email": user["email"],
        "expires_in": settings.ACCESS_MINUTES * 60,
    }
