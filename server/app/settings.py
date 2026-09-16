from __future__ import annotations

import os
from pathlib import Path

DEV_JWT_FALLBACK = "dev-only-change-me"
JWT_SECRET_MIN_LEN = 32


def _int(name: str, default: int) -> int:
    raw = os.environ.get(name, "").strip()
    if not raw:
        return default
    try:
        return int(raw)
    except ValueError:
        return default


def _flag(name: str) -> bool:
    return os.environ.get(name, "").strip().lower() in {"1", "true", "yes", "on"}


def resolve_jwt_secret(raw: str | None = None, *, allow_dev: bool | None = None) -> str:
    secret = (os.environ.get("JWT_SECRET", "") if raw is None else raw).strip()
    if allow_dev is None:
        allow_dev = _flag("ALLOW_DEV_JWT")
    if not secret:
        if allow_dev:
            return DEV_JWT_FALLBACK
        raise RuntimeError(
            "JWT_SECRET 不能为空，长度至少 32。生产环境禁止使用开发默认值；本地/单测可设 ALLOW_DEV_JWT=1。"
        )
    if secret == DEV_JWT_FALLBACK:
        if allow_dev:
            return secret
        raise RuntimeError(
            "JWT_SECRET 不能使用开发默认值 dev-only-change-me。请换成至少 32 位的随机串，或仅在本地设置 ALLOW_DEV_JWT=1。"
        )
    if len(secret) < JWT_SECRET_MIN_LEN:
        raise RuntimeError(f"JWT_SECRET 长度至少 {JWT_SECRET_MIN_LEN} 个字符。")
    return secret


JWT_SECRET = resolve_jwt_secret()
ACCESS_MINUTES = max(5, _int("ACCESS_MINUTES", 15))
REFRESH_DAYS = max(1, _int("REFRESH_DAYS", 30))
DATABASE_PATH = Path(os.environ.get("DATABASE_PATH", "data/sync.db")).expanduser()
TRUST_PROXY = _flag("TRUST_PROXY")
AUTH_RATE_PER_MIN = max(3, _int("AUTH_RATE_PER_MIN", 20))
SYNC_RATE_PER_MIN = max(10, _int("SYNC_RATE_PER_MIN", 60))
PASSWORD_MIN = 8
PASSWORD_MAX = 128
MAX_SYNC_BODY_BYTES = max(1024, _int("MAX_SYNC_BODY_BYTES", 1_048_576))
MAX_CIPHERTEXT_CHARS = max(256, _int("MAX_CIPHERTEXT_CHARS", 512_000))
MIN_KDF_ITERATIONS = 1000
MAX_KDF_ITERATIONS = max(MIN_KDF_ITERATIONS, _int("MAX_KDF_ITERATIONS", 600_000))
