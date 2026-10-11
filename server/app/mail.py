from __future__ import annotations

import smtplib
import ssl
from email.message import EmailMessage

from . import settings


def send_code(email: str, code: str, purpose: str) -> None:
    if not settings.SMTP_HOST or not settings.SMTP_FROM:
        raise RuntimeError("邮件服务尚未配置")
    subject, action = (
        ("CursorRemain 邮箱验证", "完成邮箱验证")
        if purpose == "verify"
        else ("CursorRemain 密码重置", "重置云同步登录密码")
    )
    message = EmailMessage()
    message["Subject"] = subject
    message["From"] = settings.SMTP_FROM
    message["To"] = email
    message.set_content(
        f"你的 CursorRemain 验证码是：{code}\n\n"
        f"请在 {settings.VERIFY_CODE_MINUTES} 分钟内输入此验证码以{action}。"
        "如果这不是你发起的操作，请忽略此邮件。"
    )
    if settings.SMTP_PORT == 465:
        with smtplib.SMTP_SSL(settings.SMTP_HOST, settings.SMTP_PORT, context=ssl.create_default_context(), timeout=15) as client:
            if settings.SMTP_USER:
                client.login(settings.SMTP_USER, settings.SMTP_PASSWORD)
            client.send_message(message)
        return
    with smtplib.SMTP(settings.SMTP_HOST, settings.SMTP_PORT, timeout=15) as client:
        client.ehlo()
        if settings.SMTP_STARTTLS:
            client.starttls(context=ssl.create_default_context())
            client.ehlo()
        if settings.SMTP_USER:
            client.login(settings.SMTP_USER, settings.SMTP_PASSWORD)
        client.send_message(message)
