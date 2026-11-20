"""SQLite 落库字段级加密（AES-256-GCM）。

密钥优先取 `DATA_ENCRYPTION_KEY`（Base64 编码的 32 字节），未配置时从
`JWT_SECRET` 经 HKDF 派生，保证既有部署升级后无需额外配置即可加密。
历史明文行不带前缀，读取时原样返回，写入时统一加密。
"""

from __future__ import annotations

import base64
import os
from functools import lru_cache

from cryptography.exceptions import InvalidTag
from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.ciphers.aead import AESGCM
from cryptography.hazmat.primitives.kdf.hkdf import HKDF

from . import settings

PREFIX = "enc1:"
_AAD = b"cursorremain-sync-store"
_NONCE_BYTES = 12


class DecryptError(Exception):
    """落库数据无法解密（密钥变更或内容被篡改）。"""


@lru_cache(maxsize=1)
def _key() -> bytes:
    raw = settings.DATA_ENCRYPTION_KEY
    if raw:
        try:
            data = base64.b64decode(raw, validate=True)
        except ValueError as exc:
            raise RuntimeError("DATA_ENCRYPTION_KEY 必须是 Base64 编码的 32 字节密钥") from exc
        if len(data) != 32:
            raise RuntimeError("DATA_ENCRYPTION_KEY 解码后必须是 32 字节")
        return data
    return HKDF(
        algorithm=hashes.SHA256(),
        length=32,
        salt=b"cursorremain-store-key-v1",
        info=b"aes-256-gcm",
    ).derive(settings.JWT_SECRET.encode("utf-8"))


def seal(text: str) -> str:
    """把明文包成带前缀的密文，供 SQLite 落库。"""
    nonce = os.urandom(_NONCE_BYTES)
    ciphertext = AESGCM(_key()).encrypt(nonce, text.encode("utf-8"), _AAD)
    return PREFIX + base64.urlsafe_b64encode(nonce + ciphertext).decode("ascii")


def unseal(text: str) -> str:
    """解密；不带前缀的历史明文按原样返回。"""
    if not text.startswith(PREFIX):
        return text
    try:
        raw = base64.urlsafe_b64decode(text[len(PREFIX) :].encode("ascii"))
    except ValueError as exc:
        raise DecryptError("密文编码损坏") from exc
    if len(raw) <= _NONCE_BYTES:
        raise DecryptError("密文过短")
    nonce, ciphertext = raw[:_NONCE_BYTES], raw[_NONCE_BYTES:]
    try:
        return AESGCM(_key()).decrypt(nonce, ciphertext, _AAD).decode("utf-8")
    except (InvalidTag, ValueError) as exc:
        raise DecryptError("密文校验失败") from exc


def is_encrypted(text: str) -> bool:
    return text.startswith(PREFIX)
