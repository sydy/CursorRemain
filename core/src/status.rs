use crate::jsonutil::{as_f64, as_int, get};
use crate::usage::{format_membership_type, format_spend_range, format_token_count, format_usd_cents, parse_iso};
use serde_json::{json, Value};
use std::time::{SystemTime, UNIX_EPOCH};

const TZ_OFFSET_SECS: f64 = 8.0 * 3600.0;

/// Plan name, appending 套餐 unless the formatted name already contains it,
/// and prefixing a distinct account label.
pub fn format_plan_caption(membership: Option<&str>, account_label: Option<&str>) -> String {
    let raw = membership.unwrap_or("").trim();
    let name = if raw.is_empty() {
        "—".to_string()
    } else {
        let formatted = format_membership_type(Some(raw));
        if formatted.contains("套餐") { formatted } else { format!("{formatted} 套餐") }
    };
    let label = account_label.unwrap_or("").trim();
    let formatted = format_membership_type(Some(raw));
    if !label.is_empty() && !eq_ignore_case(label, &name) && !eq_ignore_case(label, raw) && !eq_ignore_case(label, &formatted) {
        if name == "—" { label.to_string() } else { format!("{label} · {name}") }
    } else {
        name
    }
}

pub fn short_error(text: Option<&str>, max_len: usize) -> String {
    let max_len = max_len.max(1);
    let mut value = text.unwrap_or("").trim().to_string();
    if value.is_empty() { value = "同步失败".to_string(); }
    if value.chars().count() <= max_len { value } else { value.chars().take(max_len - 1).collect::<String>() + "…" }
}

pub fn export_file_slug(label: Option<&str>) -> String {
    let chars: String = label.unwrap_or("").trim().chars().map(|ch| {
        if ch.is_ascii_alphanumeric() || ('\u{4e00}'..='\u{9fff}').contains(&ch) || ch == '-' || ch == '_' { ch } else { '-' }
    }).collect();
    let slug = chars.trim_matches('-');
    slug.chars().take(24).collect()
}

pub fn format_export_filename(prefix: &str, label: Option<&str>, day: &str) -> String {
    let slug = export_file_slug(label);
    if slug.is_empty() { format!("{prefix}-{day}.csv") } else { format!("{prefix}-{slug}-{day}.csv") }
}

pub fn format_compare_account_name(name: Option<&str>, active: bool) -> String {
    let text = name.unwrap_or("").trim();
    let text = if text.is_empty() { "未命名账号" } else { text };
    if active { format!("{text}  · 当前") } else { text.to_string() }
}

pub fn format_token_save_result(ok: i64, fail: i64) -> String {
    if ok <= 0 && fail <= 0 { String::new() }
    else if fail <= 0 { if ok > 1 { format!("已保存 {ok} 个账号") } else { String::new() } }
    else { format!("成功 {ok} / 失败 {fail}") }
}

pub fn format_compare_sync(ok: i64, failures: &[String], stamp: &str) -> String {
    if failures.is_empty() { return format!("已同步 {ok} 个账号  ·  {stamp}"); }
    let mut detail = failures.iter().take(3).cloned().collect::<Vec<_>>().join("；");
    if failures.len() > 3 { detail.push_str(&format!(" 等{}个", failures.len())); }
    format!("已同步 {ok} 个账号，{} 个失败（{detail}）  ·  {stamp}", failures.len())
}

pub fn format_report_sync_result(count: i64, fetched: i64, stamp: &str, truncated: bool, total_available: i64, note: &str, has_token: bool) -> String {
    if !has_token { return "未配置 Token，请先在设置里导入账号".to_string(); }
    if note == "team_personal" { return "未能拉取个人明细（团队账号）。请先刷新用量，或把范围切到「全员」。".to_string(); }
    let extra = if truncated { format!("（服务端约 {total_available} 条，已截到最近 {count} 条）") } else { String::new() };
    if fetched > 0 { format!("已同步 {count} 条（新增 {fetched}）{extra}  ·  {stamp}") }
    else if count > 0 { format!("已是最新  ·  {count} 条{extra}  ·  {stamp}") }
    else { format!("还没有本周期明细。点「同步」拉取，或先去设置添加账号。  ·  {stamp}") }
}

pub fn format_compare_sync_progress(index: i64, total: i64, name: &str, page: i64) -> String {
    let label = if name.trim().is_empty() { "账号" } else { name.trim() };
    let text = format!("正在同步 {label}（{}/{}）…", index.max(1), total.max(1));
    if page > 1 { format!("{text}第 {page} 页") } else { text }
}

pub fn status_pill_text(remaining: Option<f64>, error: bool) -> &'static str {
    if error { return "异常"; }
    let Some(remaining) = remaining else { return "等待刷新" };
    if remaining <= 0.0 { "已耗尽" } else if remaining < 20.0 { "额度紧张" } else if remaining < 50.0 { "略偏低" } else { "状态良好" }
}

pub fn format_estimated_days(usage: Option<&Value>) -> String {
    let Some(usage) = usage.filter(|value| value.is_object()) else { return "暂无法估算".to_string() };
    let Some(est) = num(usage, "estimated_usable_days") else {
        let used = num(usage, "used_percent").unwrap_or(0.0);
        if used < 0.2 { return "用量过低，暂无法估算".to_string(); }
        if num(usage, "days_elapsed").is_some_and(|days| days < 0.04) { return "周期刚开始，统计中".to_string(); }
        return "暂无法估算".to_string();
    };
    let mut text = if est <= 0.0 {
        "已耗尽".to_string()
    } else if est < 1.0 {
        format!("约 {} 小时", ((est * 24.0) as i32).max(1))
    } else {
        format!("约 {est:.1} 天").replace(".0 天", " 天")
    };
    if let Some(reset_left) = num(usage, "days_remaining") {
        if est > 0.0 {
            text.push_str(if est >= reset_left { "  ·  可撑过本周期" } else { "  ·  可能提前耗尽" });
        }
    }
    text
}

pub fn format_estimate_caption(usage: Option<&Value>) -> String {
    let text = format_estimated_days(usage);
    if text.contains("可撑过本周期") { "预计能撑到重置".to_string() }
    else if text.contains("提前耗尽") { "预计可能提前耗尽".to_string() }
    else if text == "已耗尽" { "额度已耗尽".to_string() }
    else { text }
}

/// Status rows as `{key, value}` objects. Date labels use UTC+8, matching the
/// locked enterprise fixture (`8月1日`).
pub fn build_status_lines(usage: Option<&Value>, error: Option<&str>, updated_at: Option<&str>, account_label: Option<&str>, now: Option<&str>) -> Value {
    if let Some(error) = error {
        return json!([{"key": "状态", "value": error}]);
    }
    let Some(usage) = usage.filter(|value| value.is_object()) else {
        return json!([{"key": "状态", "value": "等待刷新…"}]);
    };
    let clock = parse_clock(now);
    let mut rows: Vec<Value> = Vec::new();
    if flag(usage, "is_unlimited") {
        rows.push(row("剩余", "不限量"));
    } else if shows_amount(usage) {
        rows.push(row("剩余", format!("{}%（{}）", fmt1(num(usage, "remaining_percent").unwrap_or(0.0)), format_spend_range(num(usage, "used_cents"), num(usage, "limit_cents")))));
    } else {
        rows.push(row("剩余", format!("{}%（已用 {}%）", fmt1(num(usage, "remaining_percent").unwrap_or(0.0)), fmt1(num(usage, "used_percent").unwrap_or(0.0)))));
    }
    let label = account_label.unwrap_or("").trim();
    let membership = text(usage, "membership_type");
    let memb = if membership.is_empty() { String::new() } else { format_membership_type(Some(membership)) };
    if !label.is_empty() && !eq_ignore_case(label, &memb) {
        rows.push(row("账号", label));
    }
    let mut plan = if memb.is_empty() { "—".to_string() } else { memb };
    if flag(usage, "is_unlimited") { plan.push_str(" · 不限量"); }
    rows.push(row("计划", plan));
    if flag(usage, "is_x_subscription") {
        if let Some(cents) = num(usage, "prepaid_remaining_cents") {
            rows.push(row("预付余额", format_usd_cents(Some(cents))));
        }
        if let Some(reset) = opt_text(usage, "usage_reset_at") {
            let reset_text = format_reset_date(reset, false);
            let left = format_cycle_remaining(Some(reset), int_field(usage, "days_remaining"), clock);
            rows.push(row("重置", if left.is_empty() { reset_text } else { format!("{reset_text}（{left}）") }));
            rows.push(row("预计可用", format_estimated_days(Some(usage))));
        } else if num(usage, "estimated_usable_days").is_some() {
            rows.push(row("预计可用", format_estimated_days(Some(usage))));
        }
        rows.push(row("更新", format_update_clock(updated_at, clock)));
        return Value::Array(rows);
    }
    if shows_amount(usage) {
        rows.push(row("金额", format_spend_range(num(usage, "used_cents"), num(usage, "limit_cents"))));
    }
    if let (Some(pooled_used), Some(pooled_limit)) = (num(usage, "pooled_used_cents"), num(usage, "pooled_limit_cents")) {
        if pooled_limit > 0.0 && (num(usage, "used_cents") != Some(pooled_used) || num(usage, "limit_cents") != Some(pooled_limit)) {
            rows.push(row("团队额度", format_spend_range(Some(pooled_used), Some(pooled_limit))));
        }
    }
    if let (Some(od_used), Some(od_limit)) = (num(usage, "on_demand_used_cents"), num(usage, "on_demand_limit_cents")) {
        if od_limit > 0.0 && (num(usage, "used_cents") != Some(od_used) || num(usage, "limit_cents") != Some(od_limit)) {
            rows.push(row("按需用量", format_spend_range(Some(od_used), Some(od_limit))));
        }
    }
    if num(usage, "total_tokens").is_some_and(|n| n > 0.0) {
        rows.push(row("消耗 Token", format_token_count(num(usage, "total_tokens"))));
    }
    if num(usage, "auto_percent_used").is_some() || num(usage, "api_percent_used").is_some() {
        let auto = percent_or_dash(num(usage, "auto_percent_used"));
        let api = percent_or_dash(num(usage, "api_percent_used"));
        rows.push(row("明细", format!("First-party {auto} · API {api}")));
    }
    if flag(usage, "shows_grok_bot") {
        if let Some(grok_used) = num(usage, "grok_bot_percent_used") {
            let mut grok = format!("剩余 {}%（本周已用 {}%）", fmt1(num(usage, "grok_bot_remaining_percent").unwrap_or(0.0)), fmt1(grok_used));
            if let Some(reset) = opt_text(usage, "grok_bot_reset_at") {
                let reset_text = format_reset_date(reset, false);
                let remaining = format_cycle_remaining(Some(reset), int_field(usage, "grok_bot_days_remaining"), clock);
                if remaining.is_empty() { grok.push_str(&format!(" · {reset_text} 重置")); } else { grok.push_str(&format!(" · {reset_text}（{remaining}）")); }
            }
            rows.push(row("Grok Bot", grok));
        }
    }
    if let Some(end) = opt_text(usage, "billing_cycle_end") {
        let end_text = format_reset_date(end, flag(usage, "billing_cycle_end_overridden"));
        let remaining = format_cycle_remaining(Some(end), int_field(usage, "days_remaining"), clock);
        let end_label = if flag(usage, "billing_cycle_end_overridden") { "到期" } else { "重置" };
        rows.push(row(end_label, if remaining.is_empty() { end_text } else { format!("{end_text}（{remaining}）") }));
        rows.push(row("预计可用", format_estimated_days(Some(usage))));
    } else if num(usage, "estimated_usable_days").is_some() {
        rows.push(row("预计可用", format_estimated_days(Some(usage))));
    }
    rows.push(row("更新", format_update_clock(updated_at, clock)));
    Value::Array(rows)
}

fn format_reset_date(iso: &str, include_time: bool) -> String {
    let Some(stamp) = parse_iso(Some(iso)) else { return iso.to_string() };
    let (month, day, hour, minute) = local_parts(stamp);
    let mut label = format!("{month}月{day}日");
    if include_time && (hour != 0 || minute != 0) {
        label.push_str(&format!(" {hour:02}:{minute:02}"));
    }
    label
}

fn format_cycle_remaining(end_iso: Option<&str>, days_remaining: Option<i64>, now: f64) -> String {
    let Some(end) = end_iso.and_then(|iso| parse_iso(Some(iso))) else {
        return days_remaining.map(|days| format!("还剩 {days} 天")).unwrap_or_default();
    };
    let seconds = end - now;
    if seconds <= 0.0 { return "已到期".to_string(); }
    let hours = (seconds / 3600.0) as i64;
    if hours < 24 {
        if hours < 1 {
            let minutes = ((seconds / 60.0) as i64).max(1);
            return format!("还剩 {minutes} 分钟");
        }
        return format!("还剩 {hours} 小时");
    }
    let days = days_remaining.unwrap_or((seconds / 86_400.0) as i64);
    format!("还剩 {days} 天")
}

fn format_update_clock(value: Option<&str>, now: f64) -> String {
    let text = value.unwrap_or("").trim();
    if is_full_stamp(text) {
        if let Some(stamp) = parse_iso(Some(text)) {
            let (_, _, hour, minute, second) = local_clock(stamp);
            return format!("{hour:02}:{minute:02}:{second:02}");
        }
    }
    if !text.is_empty() { return text.to_string(); }
    let (_, _, hour, minute, second) = local_clock(now);
    format!("{hour:02}:{minute:02}:{second:02}")
}

fn shows_amount(usage: &Value) -> bool {
    if let Some(value) = usage.get("shows_amount").and_then(Value::as_bool) { return value; }
    num(usage, "used_cents").is_some() && num(usage, "limit_cents").is_some_and(|n| n > 0.0)
        && (text(usage, "billing_mode") == "amount" || flag(usage, "is_team_account"))
}

fn percent_or_dash(value: Option<f64>) -> String {
    value.map(|n| format!("{}%", fmt1(n))).unwrap_or_else(|| "—".to_string())
}

fn fmt1(value: f64) -> String { format!("{value:.1}") }

fn row(key: &str, value: impl Into<String>) -> Value { json!({"key": key, "value": value.into()}) }

fn num(value: &Value, key: &str) -> Option<f64> { as_f64(get(value, key)) }

fn int_field(value: &Value, key: &str) -> Option<i64> { as_int(get(value, key)) }

fn text<'a>(value: &'a Value, key: &str) -> &'a str { value.get(key).and_then(Value::as_str).unwrap_or("").trim() }

fn opt_text<'a>(value: &'a Value, key: &str) -> Option<&'a str> {
    let text = text(value, key);
    if text.is_empty() { None } else { Some(text) }
}

fn flag(value: &Value, key: &str) -> bool { value.get(key).and_then(Value::as_bool).unwrap_or(false) }

fn eq_ignore_case(left: &str, right: &str) -> bool { left.eq_ignore_ascii_case(right) }

fn is_full_stamp(value: &str) -> bool { value.contains('T') && parse_iso(Some(value)).is_some() }

fn parse_clock(now: Option<&str>) -> f64 {
    if let Some(parsed) = now.and_then(|text| parse_iso(Some(text))) { return parsed; }
    SystemTime::now().duration_since(UNIX_EPOCH).map(|d| d.as_secs_f64()).unwrap_or(0.0)
}

fn local_parts(unix: f64) -> (u32, u32, u32, u32) {
    let (month, day, hour, minute, _) = local_clock(unix);
    (month, day, hour, minute)
}

fn local_clock(unix: f64) -> (u32, u32, u32, u32, u32) {
    let secs = (unix + TZ_OFFSET_SECS).floor() as i64;
    let days = secs.div_euclid(86_400);
    let clock = secs.rem_euclid(86_400) as u32;
    let (_, month, day) = civil_from_days(days);
    (month, day, clock / 3600, clock % 3600 / 60, clock % 60)
}

fn civil_from_days(days: i64) -> (i32, u32, u32) {
    let z = days + 719_468;
    let era = if z >= 0 { z } else { z - 146_096 } / 146_097;
    let doe = (z - era * 146_097) as u64;
    let yoe = (doe - doe / 1460 + doe / 36524 - doe / 146_096) / 365;
    let y = yoe as i64 + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let day = doy - (153 * mp + 2) / 5 + 1;
    let month = if mp < 10 { mp + 3 } else { mp - 9 };
    let year = if month <= 2 { y + 1 } else { y };
    (year as i32, month as u32, day as u32)
}
