"""用量文案（无 GUI 依赖，菜单栏进程可安全导入）。"""

from __future__ import annotations

from datetime import datetime, timezone

from cursor_api import (
    UsageSnapshot,
    format_membership_type,
    format_spend_range,
    format_token_count,
)


def format_summary_text(
    usage: UsageSnapshot | None,
    error_message: str | None,
    updated_at: str | None,
    account_label: str | None = None,
) -> str:
    if error_message:
        return f"状态: {error_message} | 更新 {updated_at or '—'}"
    if usage is None:
        return "状态: 等待刷新…"
    auto = "—" if usage.auto_percent_used is None else f"{usage.auto_percent_used:.1f}%"
    api = "—" if usage.api_percent_used is None else f"{usage.api_percent_used:.1f}%"
    est = format_estimated_days(usage)
    tokens = ""
    if usage.total_tokens:
        tokens = f"消耗 {format_token_count(usage.total_tokens)} Token | "
    spend = ""
    if usage.shows_amount():
        spend = f"金额 {format_spend_range(usage.used_cents, usage.limit_cents)} | "
    plan = format_plan_caption(usage.membership_type, account_label)
    if usage.is_unlimited:
        plan = f"{plan} · 不限量"
    grok = ""
    if usage.shows_grok_bot() and usage.grok_bot_remaining_percent is not None:
        grok = f" | Grok Bot 剩余 {usage.grok_bot_remaining_percent:.1f}%"
    return (
        f"剩余 {usage.remaining_percent:.1f}% | {plan} | "
        f"{spend}{tokens}First-party {auto} | API {api}{grok} | 预计可用 {est} | 更新 {updated_at or '—'}"
    )


def format_estimated_days(usage: UsageSnapshot) -> str:
    est = usage.estimated_usable_days
    if est is None:
        if usage.used_percent < 0.2:
            return "用量过低，暂无法估算"
        if usage.days_elapsed is not None and usage.days_elapsed < 0.04:
            return "周期刚开始，统计中"
        return "暂无法估算"

    if est <= 0:
        text = "已耗尽"
    elif est < 1:
        text = f"约 {max(1, int(est * 24))} 小时"
    else:
        text = f"约 {est:.1f} 天".replace(".0 天", " 天")

    reset_left = usage.days_remaining
    if reset_left is not None and est > 0:
        if est >= reset_left:
            text += "  ·  可撑过本周期"
        else:
            text += "  ·  可能提前耗尽"
    return text


def status_pill_text(remaining: float | None, *, error: bool = False) -> str:
    """组合 4 左侧状态胶囊。"""
    if error:
        return "异常"
    if remaining is None:
        return "等待刷新"
    pct = float(remaining)
    if pct <= 0:
        return "已耗尽"
    if pct < 20:
        return "额度紧张"
    if pct < 50:
        return "略偏低"
    return "状态良好"


def format_plan_caption(membership: str | None, account_label: str | None = None) -> str:
    raw = (membership or "").strip()
    if not raw:
        name = "—"
    else:
        name = format_membership_type(raw)
        if "套餐" not in name:
            name = f"{name} 套餐"
    label = (account_label or "").strip()
    known = {name.lower(), raw.lower(), format_membership_type(raw).lower()}
    if label and label.lower() not in known:
        if name == "—":
            return label
        return f"{label} · {name}"
    return name


def format_estimate_caption(usage: UsageSnapshot) -> str:
    text = format_estimated_days(usage)
    if "可撑过本周期" in text:
        return "预计能撑到重置"
    if "提前耗尽" in text:
        return "预计可能提前耗尽"
    if text == "已耗尽":
        return "额度已耗尽"
    return text


def format_reset_date(iso_value: str, include_time: bool = False) -> str:
    try:
        text = iso_value.replace("Z", "+00:00")
        dt = datetime.fromisoformat(text)
        if dt.tzinfo is not None:
            dt = dt.astimezone()
        text = f"{dt.month}月{dt.day}日"
        if include_time and (dt.hour or dt.minute):
            text += f" {dt.hour:02d}:{dt.minute:02d}"
        return text
    except ValueError:
        return iso_value


def format_cycle_remaining(end_iso: str | None, days_remaining: int | None, now: datetime | None = None) -> str:
    if not end_iso:
        return f"还剩 {days_remaining} 天" if days_remaining is not None else ""
    try:
        text = end_iso.replace("Z", "+00:00")
        end = datetime.fromisoformat(text)
        if end.tzinfo is None:
            end = end.replace(tzinfo=timezone.utc)
        clock = now or datetime.now(timezone.utc)
        if clock.tzinfo is None:
            clock = clock.replace(tzinfo=timezone.utc)
        delta = end - clock.astimezone(end.tzinfo)
    except ValueError:
        return f"还剩 {days_remaining} 天" if days_remaining is not None else ""
    seconds = delta.total_seconds()
    if seconds <= 0:
        return "已到期"
    hours = int(seconds // 3600)
    if hours < 24:
        if hours < 1:
            minutes = max(1, int(seconds // 60))
            return f"还剩 {minutes} 分钟"
        return f"还剩 {hours} 小时"
    days = days_remaining if days_remaining is not None else int(seconds // 86400)
    return f"还剩 {days} 天"


def cycle_end_label(usage: UsageSnapshot) -> str:
    return "到期" if usage.billing_cycle_end_overridden else "重置"


def build_status_lines(
    usage: UsageSnapshot | None,
    error_message: str | None,
    updated_at: str | None = None,
    account_label: str | None = None,
) -> list[tuple[str, str]]:
    """状态明细行（Windows 飞出层 / macOS 原生面板共用）。"""
    if error_message:
        return [("状态", error_message)]
    if usage is None:
        return [("状态", "等待刷新…")]

    rows: list[tuple[str, str]] = []
    if usage.is_unlimited:
        rows.append(("剩余", "不限量"))
    elif usage.shows_amount():
        rows.append(
            (
                "剩余",
                f"{usage.remaining_percent:.1f}%（{format_spend_range(usage.used_cents, usage.limit_cents)}）",
            )
        )
    else:
        rows.append(("剩余", f"{usage.remaining_percent:.1f}%（已用 {usage.used_percent:.1f}%）"))
    label = (account_label or "").strip()
    memb = format_membership_type(usage.membership_type) if usage.membership_type else ""
    if label and label.lower() != memb.lower():
        rows.append(("账号", label))
    plan = memb or "—"
    if usage.is_unlimited:
        plan = f"{plan} · 不限量"
    rows.append(("计划", plan))
    if usage.shows_amount():
        rows.append(("金额", format_spend_range(usage.used_cents, usage.limit_cents)))
    if (
        usage.pooled_used_cents is not None
        and usage.pooled_limit_cents is not None
        and usage.pooled_limit_cents > 0
        and (
            usage.used_cents != usage.pooled_used_cents
            or usage.limit_cents != usage.pooled_limit_cents
        )
    ):
        rows.append(("团队额度", format_spend_range(usage.pooled_used_cents, usage.pooled_limit_cents)))
    if (
        usage.on_demand_used_cents is not None
        and usage.on_demand_limit_cents is not None
        and usage.on_demand_limit_cents > 0
        and (
            usage.used_cents != usage.on_demand_used_cents
            or usage.limit_cents != usage.on_demand_limit_cents
        )
    ):
        rows.append(
            ("按需用量", format_spend_range(usage.on_demand_used_cents, usage.on_demand_limit_cents))
        )
    if usage.total_tokens:
        rows.append(("消耗 Token", format_token_count(usage.total_tokens)))
    if usage.auto_percent_used is not None or usage.api_percent_used is not None:
        auto = "—" if usage.auto_percent_used is None else f"{usage.auto_percent_used:.1f}%"
        api = "—" if usage.api_percent_used is None else f"{usage.api_percent_used:.1f}%"
        rows.append(("明细", f"First-party {auto} · API {api}"))
    if usage.shows_grok_bot() and usage.grok_bot_percent_used is not None:
        grok = f"剩余 {usage.grok_bot_remaining_percent:.1f}%（本周已用 {usage.grok_bot_percent_used:.1f}%）"
        if usage.grok_bot_reset_at:
            reset_text = format_reset_date(usage.grok_bot_reset_at)
            remaining = format_cycle_remaining(usage.grok_bot_reset_at, usage.grok_bot_days_remaining)
            if remaining:
                grok += f" · {reset_text}（{remaining}）"
            else:
                grok += f" · {reset_text} 重置"
        rows.append(("Grok Bot", grok))

    if usage.billing_cycle_end:
        end_text = format_reset_date(
            usage.billing_cycle_end, include_time=usage.billing_cycle_end_overridden
        )
        remaining = format_cycle_remaining(usage.billing_cycle_end, usage.days_remaining)
        label = cycle_end_label(usage)
        if remaining:
            rows.append((label, f"{end_text}（{remaining}）"))
        else:
            rows.append((label, end_text))
        rows.append(("预计可用", format_estimated_days(usage)))
    elif usage.estimated_usable_days is not None:
        rows.append(("预计可用", format_estimated_days(usage)))

    rows.append(("更新", updated_at or datetime.now().strftime("%H:%M:%S")))
    return rows


def short_error(text: str | None, max_len: int = 40) -> str:
    value = (text or "同步失败").strip() or "同步失败"
    if len(value) <= max_len:
        return value
    return value[: max_len - 1] + "…"


def format_compare_sync(ok: int, failures: list[str], stamp: str) -> str:
    if not failures:
        return f"已同步 {ok} 个账号  ·  {stamp}"
    detail = "；".join(failures[:3])
    if len(failures) > 3:
        detail += f" 等{len(failures)}个"
    return f"已同步 {ok} 个账号，{len(failures)} 个失败（{detail}）  ·  {stamp}"


def format_report_spend_kpi(
    total_cny: float,
    plan_cny: float,
    on_demand_cny: float,
    usd_cny_rate: float,
    uses_actual: bool,
    window_plan_cny: float = 0.0,
) -> str:
    from usage_report import format_cny

    if plan_cny <= 0 and on_demand_cny <= 0 and total_cny <= 0:
        return ""
    rate = f"{usd_cny_rate:.2f}"
    if uses_actual:
        text = f"    已分摊 {format_cny(total_cny)}（月成本 {format_cny(plan_cny)}"
        if window_plan_cny > 0 and abs(window_plan_cny - plan_cny) > 0.005:
            text += f"，本窗口折算 {format_cny(window_plan_cny)}"
        return text + f"，按需已计入）· 汇率 {rate}"
    return (
        f"    已分摊 {format_cny(total_cny)}（月费 {format_cny(plan_cny)} + 按需 {format_cny(on_demand_cny)}）· 汇率 {rate}"
    )


def format_report_sync_result(
    count: int,
    fetched: int,
    stamp: str,
    *,
    truncated: bool = False,
    total_available: int = 0,
    note: str = "",
    has_token: bool = True,
) -> str:
    if not has_token:
        return "未配置 Token，请先在设置里导入账号"
    if note == "team_personal":
        return "未能拉取个人明细（团队账号）。请先刷新用量，或把范围切到「全员」。"
    extra = f"（服务端约 {total_available} 条，已截到最近 {count} 条）" if truncated else ""
    if fetched > 0:
        return f"已同步 {count} 条（新增 {fetched}）{extra}  ·  {stamp}"
    if count > 0:
        return f"已是最新  ·  {count} 条{extra}  ·  {stamp}"
    return f"还没有本周期明细。点「同步」拉取，或先去设置添加账号。  ·  {stamp}"


def format_flyout_error(error_message: str | None) -> str:
    from cursor_api import is_auth_error_message

    text = (error_message or "").strip()
    if not text:
        return "等待刷新…"
    if is_auth_error_message(text):
        if "未配置" in text:
            return "未配置 Token，点下方「粘贴 Token」导入"
        return "登录已过期，点下方「粘贴 Token」更新"
    return text


def compare_hint() -> str:
    return (
        "账号一行，First-party / API / Grok Bot 各占一行。日均持有 = 折合月费÷30。"
        "填了实际成本时，实付按该成本在窗口内折算分摊，按需不再按官网标价另加。"
        "绿色数字只比较已填成本的账号，取最低 ¥/百万 Token。"
    )


def format_compare_hint(mixed_windows: bool) -> str:
    if not mixed_windows:
        return compare_hint()
    return compare_hint() + " 当前表里计费窗口不一致，绿色仅供参考。"


def format_report_cache_status(count: int, account_label: str | None = None) -> str:
    prefix = f"当前：{account_label.strip()} · " if (account_label or "").strip() else ""
    if count > 0:
        return f"{prefix}本地 {count} 条，正在刷新…"
    return f"{prefix}本地还没有明细，正在同步…"


def format_report_sync_error(error: str | None) -> str:
    from cursor_api import is_auth_error_message

    text = (error or "").strip()
    if not text:
        return "同步失败"
    if is_auth_error_message(text):
        return "未配置 Token，请先在设置里导入账号" if "未配置" in text else "登录已过期，请到设置重新粘贴 Token"
    return "同步失败：" + short_error(text, 80)


def format_cloud_decrypt_note(decrypt_error: bool, sync_secret_failed: bool, cloud_access_failed: bool) -> str:
    if sync_secret_failed or cloud_access_failed:
        return "本机解不开云同步密钥。请退出后用当前密码重新登录，或导入备份。"
    if decrypt_error:
        return "本机有账号 Token 解不开。请重新粘贴 Token。"
    return ""


def format_report_sync_progress(page: int) -> str:
    if page <= 1:
        return "正在同步本周期明细…"
    return f"正在同步本周期明细…第 {page} 页"


def format_report_filter_empty(total: int) -> str:
    if total <= 0:
        return ""
    return f"当前筛选无结果（本地共 {total} 条，可清空筛选）"


def format_compare_sync_progress(index: int, total: int, name: str, page: int = 0) -> str:
    label = (name or "").strip() or "账号"
    text = f"正在同步 {label}（{max(1, index)}/{max(1, total)}）…"
    if page > 1:
        text += f"第 {page} 页"
    return text


def format_cloud_sync_notify(ok: bool, message: str) -> str:
    from account_sync import is_trim_note

    if ok:
        return ""
    text = (message or "").strip()
    if not text or is_trim_note(text):
        return ""
    return text


def flyout_settings_title(error_message: str | None) -> str:
    from cursor_api import is_auth_error_message

    return "粘贴 Token" if is_auth_error_message(error_message) else "设置"


def prioritize_active(ids: list[str], active_id: str) -> list[str]:
    return sorted(ids, key=lambda item: 0 if item == active_id else 1)


def format_sync_status(last_at: str, last_error: str) -> str:
    from account_sync import format_local, is_trim_note

    error = (last_error or "").strip()
    at = (last_at or "").strip()
    if error and is_trim_note(error) and at:
        return "上次同步 " + format_local(at) + "；" + error
    if error:
        return error
    if at:
        return "上次同步 " + format_local(at)
    return ""
