"""Cursor 官网登录辅助：邮箱规范化、自动填表脚本、从 Cookie 取 Token。

真正打开登录页由桌面壳用 WebView 完成；这里只放可单测的共享逻辑。
"""

from __future__ import annotations

import json
import re
from typing import Any

from cursor_api import normalize_workos_token

LOGIN_URL = "https://cursor.com/login"
COOKIE_NAME = "WorkosCursorSessionToken"
LOGIN_TIMEOUT_SECONDS = 180

_EMAIL_RE = re.compile(r"^[^@\s]+@[^@\s]+\.[^@\s]+$")


def looks_like_email(value: Any) -> bool:
    text = str(value or "").strip()
    at = text.find("@")
    return at > 0 and at < len(text) - 1 and " " not in text


def sanitize_login_email(raw: Any) -> str:
    text = str(raw or "").strip().lower()
    if not looks_like_email(text):
        return ""
    if not _EMAIL_RE.match(text):
        return ""
    return text


_EMAIL_TOKEN = r"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}"
_EMAIL_LABELS = r"(?:账号|帐号|账户|邮箱|用户名)"
_LABELED_BOTH = re.compile(
    rf"^{_EMAIL_LABELS}\s*[：:]\s*({_EMAIL_TOKEN})\s*密码\s*[：:]\s*(.+)$"
)
_LABELED_EMAIL = re.compile(rf"^{_EMAIL_LABELS}\s*[：:]\s*({_EMAIL_TOKEN})\s*$")
_LABELED_PASSWORD = re.compile(r"^密码\s*[：:]\s*(.+)$")
_EMAIL_AT_START = re.compile(rf"^({_EMAIL_TOKEN})(.*)$")


def _paste_item(
    kind: str,
    token: str = "",
    email: str = "",
    password: str = "",
    message: str = "",
) -> dict[str, str]:
    return {
        "kind": kind,
        "token": token,
        "email": email,
        "password": password,
        "message": message,
    }


def looks_like_paste_token(line: Any) -> bool:
    text = str(line or "").strip()
    if not text:
        return False
    lower = text.lower()
    if "workoscursorsessiontoken=" in lower:
        return True
    if "%3a%3a" in lower or "::" in text:
        return True
    parts = text.split(".")
    return len(parts) == 3 and all(parts)


def _parse_labeled_both(line: str) -> tuple[str, str] | None:
    match = _LABELED_BOTH.match(line)
    if not match:
        return None
    email = sanitize_login_email(match.group(1))
    password = match.group(2).strip()
    if not email or not password:
        return None
    return email, password


def _parse_labeled_email(line: str) -> str:
    match = _LABELED_EMAIL.match(line)
    if not match:
        return ""
    return sanitize_login_email(match.group(1))


def _parse_labeled_password(line: str) -> str | None:
    match = _LABELED_PASSWORD.match(line)
    if not match:
        return None
    return match.group(1).strip()


def _parse_email_separator(line: str) -> tuple[str, str] | None:
    match = _EMAIL_AT_START.match(line)
    if not match:
        return None
    email = sanitize_login_email(match.group(1))
    if not email:
        return None
    rest = match.group(2)
    if not rest:
        return None
    stripped = rest.lstrip()
    if stripped.startswith("----"):
        password = stripped[4:].strip()
    elif stripped[:1] in (":", "："):
        password = stripped[1:].strip()
    elif rest[0] in " \t":
        password = rest.strip()
    else:
        return None
    if not password:
        return None
    return email, password


def parse_account_paste(text: Any) -> list[dict[str, str]]:
    raw = str(text or "").replace("\r\n", "\n").replace("\r", "\n")
    items: list[dict[str, str]] = []
    pending_email = ""

    def flush_pending() -> None:
        nonlocal pending_email
        if pending_email:
            items.append(_paste_item("error", email=pending_email, message="只有账号没有密码"))
            pending_email = ""

    for line in raw.split("\n"):
        line = line.strip()
        if not line:
            continue

        both = _parse_labeled_both(line)
        if both:
            flush_pending()
            items.append(_paste_item("credentials", email=both[0], password=both[1]))
            continue

        email_only = _parse_labeled_email(line)
        if email_only:
            flush_pending()
            pending_email = email_only
            continue

        password_only = _parse_labeled_password(line)
        if password_only is not None:
            if pending_email and password_only:
                items.append(_paste_item("credentials", email=pending_email, password=password_only))
                pending_email = ""
            elif pending_email:
                flush_pending()
            else:
                items.append(_paste_item("error", message="只有密码没有账号"))
            continue

        separated = _parse_email_separator(line)
        if separated:
            flush_pending()
            items.append(_paste_item("credentials", email=separated[0], password=separated[1]))
            continue

        if looks_like_paste_token(line):
            flush_pending()
            items.append(_paste_item("token", token=line))
            continue

        flush_pending()
        items.append(_paste_item("error", message="无法识别"))

    flush_pending()
    return items


def is_single_token_paste(text: Any) -> bool:
    items = parse_account_paste(text)
    return len(items) == 1 and items[0]["kind"] == "token"


def default_account_label(email: str, existing_label: str = "") -> str:
    current = str(existing_label or "").strip()
    if current:
        return current
    return sanitize_login_email(email)


def token_from_cookies(cookies: Any) -> str:
    rows = cookies if isinstance(cookies, list) else []
    for item in rows:
        if not isinstance(item, dict):
            continue
        name = str(item.get("name") or item.get("Name") or "").strip()
        if name.lower() != COOKIE_NAME.lower():
            continue
        value = str(item.get("value") or item.get("Value") or "").strip()
        if not value:
            return ""
        try:
            return normalize_workos_token(value)
        except Exception:
            return value
    return ""


def autofill_script(email: str, password: str) -> str:
    payload = json.dumps(
        {"email": sanitize_login_email(email), "password": str(password or "")},
        ensure_ascii=False,
    )
    return (
        "(function(){"
        f"const c={payload};"
        "function setNative(el,val){"
        "if(!el)return;"
        "const proto=el.tagName==='TEXTAREA'?window.HTMLTextAreaElement.prototype:window.HTMLInputElement.prototype;"
        "const desc=Object.getOwnPropertyDescriptor(proto,'value');"
        "if(desc&&desc.set)desc.set.call(el,val);else el.value=val;"
        "el.dispatchEvent(new Event('input',{bubbles:true}));"
        "el.dispatchEvent(new Event('change',{bubbles:true}));"
        "}"
        "if(document.querySelector('iframe[src*=\"challenges.cloudflare\"],iframe[src*=\"turnstile\"],input[autocomplete=\"one-time-code\"]'))"
        "return 'need-user';"
        "const emailEl=document.querySelector('input[type=\"email\"],input[name=\"email\"],input[autocomplete=\"username\"],input[autocomplete=\"email\"]');"
        "if(emailEl&&c.email)setNative(emailEl,c.email);"
        "const passEl=document.querySelector('input[type=\"password\"]');"
        "if(passEl&&c.password)setNative(passEl,c.password);"
        "const buttons=[...document.querySelectorAll('button,[type=submit]')];"
        "const go=buttons.find(b=>/continue|sign in|log in|登录|继续/i.test((b.innerText||b.value||'')));"
        "if(go&&!window.__cttClicked){window.__cttClicked=Date.now();go.click();return 'clicked';}"
        "return passEl&&c.password?'filled':'waiting';"
        "})();"
    )
