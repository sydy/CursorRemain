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
