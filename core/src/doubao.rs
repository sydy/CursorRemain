//! 豆包 App 专业版额度。
//!
//! 接口没有公开文档，字段来自 doubao.com 前端资源的静态分析（见
//! `docs/豆包额度接入开发说明.md`）。解析必须容错：字段缺失、类型漂移、
//! 未知窗口都按「忽略」处理，不 panic。

use crate::jsonutil::{as_bool, as_f64, as_string, child, get, has, is_empty};
use crate::numbers::{clamp_percent, round1};
use crate::usage::timestamp_to_iso;
use serde_json::{json, Value};
use sha2::{Digest, Sha256};

/// 登录已过期：HTTP 200，只看业务 code。
pub const LOGIN_INVALID_CODE: i64 = 710012001;
pub const DASHBOARD_URL: &str = "https://www.doubao.com/";

const MEMBER_KEYS: [&str; 15] = [
    "membership_type",
    "membershipType",
    "product_name",
    "productName",
    "plan_name",
    "planName",
    "plan",
    "level",
    "member_type",
    "memberType",
    "tier",
    "subscription_name",
    "subscriptionName",
    "combo_name",
    "name",
];

const USER_ID_KEYS: [&str; 8] = [
    "user_id",
    "userId",
    "uid",
    "uid_tt",
    "user_unique_id",
    "userUniqueId",
    "member_id",
    "memberId",
];

const COOKIE_ID_NAMES: [&str; 6] = ["uid_tt", "uid_tt_ss", "user_id", "user_unique_id", "odin_tt", "tt_uid"];

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum WindowKind {
    Current,
    SevenDay,
    Monthly,
    Package,
    Incentive,
}

/// 每个区段的窗口归属：会员三档、创作额度包、活动赠送额度。
#[derive(Clone, Copy, PartialEq, Eq)]
enum SectionMode {
    Membership,
    Package,
    Incentive,
}

impl WindowKind {
    fn key(self) -> &'static str {
        match self {
            WindowKind::Current => "5h",
            WindowKind::SevenDay => "7d",
            WindowKind::Monthly => "1m",
            WindowKind::Package => "package",
            WindowKind::Incentive => "incentive",
        }
    }

    fn label(self) -> &'static str {
        match self {
            WindowKind::Current => "当前时段",
            WindowKind::SevenDay => "近 7 天",
            WindowKind::Monthly => "每月",
            WindowKind::Package => "创作额度包",
            WindowKind::Incentive => "活动赠送额度",
        }
    }
}

fn window_kind(raw: &Value) -> Option<WindowKind> {
    match as_f64(raw).map(|n| n.round() as i64) {
        Some(1) => Some(WindowKind::Current),
        Some(2) => Some(WindowKind::SevenDay),
        Some(4) => Some(WindowKind::Monthly),
        Some(3) => Some(WindowKind::Package),
        // 0 和未知档位都忽略，不报错。
        _ => None,
    }
}

fn membership_name(payload: &Value) -> String {
    let data = child(payload, "data");
    let member = child(data, "member_info");
    for key in MEMBER_KEYS {
        let raw = if has(member, key) { get(member, key) } else { get(data, key) };
        if let Some(text) = as_string(raw) {
            let trimmed = text.trim();
            if !trimmed.is_empty() {
                return trimmed.to_string();
            }
        }
    }
    // 免费/个人订阅的 member_info 只有布尔位，套餐名在 current_subscription.display。
    subscription_display_name(data)
}

/// `current_subscription.display.product_name` 优先，`short_name` 兜底。
fn subscription_display_name(data: &Value) -> String {
    let subscription = child(data, "current_subscription");
    for container in [child(subscription, "display"), subscription] {
        for key in ["product_name", "productName", "short_name", "shortName"] {
            if let Some(text) = as_string(get(container, key)) {
                let trimmed = text.trim();
                if !trimmed.is_empty() {
                    return trimmed.to_string();
                }
            }
        }
    }
    String::new()
}

/// 从 `www.doubao.com/chat/` 返回的 SSR 页面里取账号的真实用户名。
///
/// 页面把账号信息放在 `window._ROUTER_DATA` 的
/// `loaderData.chat_layout.chat_layout.accountInfo.data`：`name` 是昵称，
/// `screen_name` 是同一份展示名，第三方绑定（抖音）的昵称在 `connects` 里。
/// 都取不到时返回空串，界面回退到套餐名，不编造名字。
pub fn account_name(page: &str) -> String {
    let Some(raw) = router_payload(page) else { return String::new() };
    let Ok(value) = serde_json::from_str::<Value>(raw) else { return String::new() };
    let layout = child(child(child(&value, "loaderData"), "chat_layout"), "chat_layout");
    let info = child(child(layout, "accountInfo"), "data");
    for key in ["name", "screen_name"] {
        if let Some(name) = clean_name(get(info, key)) {
            return name;
        }
    }
    for connect in get(info, "connects").as_array().into_iter().flatten() {
        if let Some(name) = clean_name(get(connect, "platform_screen_name")) {
            return name;
        }
    }
    String::new()
}

fn clean_name(raw: &Value) -> Option<String> {
    let text = as_string(raw)?;
    let trimmed = text.trim();
    if trimmed.is_empty() { None } else { Some(trimmed.to_string()) }
}

/// 同一份 SSR 页面里的头像地址（`accountInfo.data.avatar_url`）。
/// 取不到返回空串，界面回退到首字母头像。
pub fn account_avatar(page: &str) -> String {
    let Some(raw) = router_payload(page) else { return String::new() };
    let Ok(value) = serde_json::from_str::<Value>(raw) else { return String::new() };
    let layout = child(child(child(&value, "loaderData"), "chat_layout"), "chat_layout");
    let info = child(child(layout, "accountInfo"), "data");
    if let Some(url) = clean_avatar(get(info, "avatar_url")) {
        return url;
    }
    for connect in get(info, "connects").as_array().into_iter().flatten() {
        if let Some(url) = clean_avatar(get(connect, "platform_avatar_url")) {
            return url;
        }
    }
    String::new()
}

fn clean_avatar(raw: &Value) -> Option<String> {
    let text = as_string(raw)?;
    let trimmed = text.trim();
    // 只放行 http(s) 绝对地址，避免把任意字符串塞进图片请求。
    if !trimmed.starts_with("https://") && !trimmed.starts_with("http://") {
        return None;
    }
    Some(trimmed.to_string())
}

/// 抠出 `window._ROUTER_DATA = {...}` 这一整段 JSON，去掉行尾分号。
fn router_payload(page: &str) -> Option<&str> {
    let marker = page.find("window._ROUTER_DATA")?;
    let rest = &page[marker..];
    let after_eq = &rest[rest.find('=')? + 1..];
    let start = after_eq.find('{')?;
    let body = &after_eq[start..];
    let end = body.find("</script>")?;
    Some(body[..end].trim().trim_end_matches(';').trim())
}

fn window_rows(section: &Value, mode: SectionMode) -> Vec<Value> {
    let mut rows: Vec<Value> = Vec::new();
    if !section.is_object() || is_empty(section) {
        return rows;
    }
    let Some(groups) = get(section, "window_limit_groups").as_array() else {
        return rows;
    };
    // 活动赠送额度按「一项活动一条窗口」展示，条目数用于界面标注。
    let count = as_f64(get(section, "entitlement_count")).map(|n| n.round() as i64).filter(|n| *n > 0);
    for group in groups {
        let Some(limits) = get(group, "window_limits").as_array() else { continue };
        for limit in limits {
            let kind = match mode {
                SectionMode::Incentive => Some(WindowKind::Incentive),
                SectionMode::Package => Some(WindowKind::Package),
                SectionMode::Membership => window_kind(get(limit, "window_type")),
            };
            let Some(kind) = kind else { continue };
            let used = clamp_percent(as_f64(get(limit, "used_percent")).unwrap_or(0.0));
            let mut row = build_row(kind, limit, used);
            if let Some(count) = count.filter(|_| rows.is_empty()) {
                row["count"] = json!(count);
            }
            merge_row(&mut rows, &mut row);
        }
    }
    rows.sort_by_key(|row| match row["key"].as_str() {
        Some("5h") => 0,
        Some("7d") => 1,
        Some("1m") => 2,
        Some("package") => 3,
        _ => 4,
    });
    rows
}

fn build_row(kind: WindowKind, limit: &Value, used: f64) -> Value {
    let exemption = child(limit, "exemption");
    let exempt_active = as_bool(get(exemption, "active"));
    let unlimited = as_bool(get(limit, "unlimited")) || exempt_active;
    let mut reset_at = timestamp_to_iso(get(limit, "end_time"));
    if exempt_active {
        if let Some(iso) = timestamp_to_iso(get(exemption, "end_time")) {
            reset_at = Some(iso);
        }
    }
    let mut row = json!({
        "key": kind.key(),
        "label": kind.label(),
        "used_percent": round1(used),
        "less_than_one_percent": as_bool(get(limit, "less_than_one_percent")),
        "exhausted": as_bool(get(limit, "exhausted")),
        "unlimited": unlimited,
        "used_amount": as_string(get(limit, "used_amount")),
        "total_amount": as_string(get(limit, "total_amount")),
        "window_type": as_f64(get(limit, "window_type")),
    });
    row["reset_at"] = reset_at.map(Value::String).unwrap_or(Value::Null);
    row
}

fn merge_row(rows: &mut Vec<Value>, row: &mut Value) {
    let key = row["key"].as_str().unwrap_or("").to_string();
    let Some(existing) = rows.iter_mut().find(|item| item["key"].as_str() == Some(key.as_str())) else {
        rows.push(row.clone());
        return;
    };
    let old_used = as_f64(get(existing, "used_percent")).unwrap_or(0.0);
    let new_used = as_f64(get(row, "used_percent")).unwrap_or(0.0);
    if new_used > old_used {
        existing["used_percent"] = json!(round1(new_used));
    }
    for flag in ["less_than_one_percent", "exhausted", "unlimited"] {
        if as_bool(get(row, flag)) {
            existing[flag] = json!(true);
        }
    }
    if existing["reset_at"].is_null() {
        if let Some(reset) = row.get("reset_at").filter(|value| !value.is_null()) {
            existing["reset_at"] = reset.clone();
        }
    }
    if existing["used_amount"].is_null() {
        if let Some(amount) = row.get("used_amount").filter(|value| !value.is_null()) {
            existing["used_amount"] = amount.clone();
        }
    }
    if existing["total_amount"].is_null() {
        if let Some(amount) = row.get("total_amount").filter(|value| !value.is_null()) {
            existing["total_amount"] = amount.clone();
        }
    }
}

fn error_snapshot(code: Option<i64>, auth: bool) -> Value {
    let message = if auth {
        "登录已失效，请重新登录豆包账号".to_string()
    } else {
        match code {
            Some(code) => format!("查询失败（code {code}）"),
            None => "查询失败：接口返回格式异常".to_string(),
        }
    };
    json!({
        "error": message,
        "auth_error": auth,
        "code": code,
        "provider": "doubao",
    })
}

/// 把 `quota/summary` 的响应信封解析成与 `parse_usage_summary` 同形的快照。
pub fn parse_doubao_quota(payload: &Value, now: Option<&str>) -> Value {
    let _ = now;
    if !payload.is_object() {
        return error_snapshot(None, false);
    }
    let code = as_f64(get(payload, "code")).map(|n| n.round() as i64);
    let auth = code == Some(LOGIN_INVALID_CODE);
    if let Some(code) = code {
        if code != 0 {
            return error_snapshot(Some(code), auth);
        }
    } else if !has(payload, "data") {
        return error_snapshot(None, false);
    }
    let data = child(payload, "data");
    if is_empty(data) {
        return error_snapshot(None, false);
    }

    let mut windows = window_rows(child(data, "window_limit_section"), SectionMode::Membership);
    if windows.is_empty() {
        windows = window_rows(child(data, "enterprise_window_limit_section"), SectionMode::Membership);
    }
    windows.extend(window_rows(child(data, "quota_package_section"), SectionMode::Package));
    // 活动赠送额度是免费账号的主要可见额度，会员号也可能同时有。
    windows.extend(window_rows(child(data, "incentive_quota_section"), SectionMode::Incentive));
    windows.sort_by_key(|row| match row["key"].as_str() {
        Some("5h") => 0,
        Some("7d") => 1,
        Some("1m") => 2,
        Some("package") => 3,
        _ => 4,
    });
    if windows.is_empty() {
        return json!({
            "error": "查询失败：账号没有额度窗口（可能未订阅）",
            "auth_error": false,
            "code": 0,
            "provider": "doubao",
        });
    }

    let usage_exhausted = as_bool(get(child(data, "window_limit_section"), "usage_exhausted"));
    let main = main_window(&windows);
    let mut used = main.as_ref().and_then(|row| as_f64(get(row, "used_percent")));
    let mut unlimited_main = main.as_ref().map(|row| as_bool(get(row, "unlimited"))).unwrap_or(false);
    if usage_exhausted {
        // 整体用尽：剩余直接记 0，不再看单档。
        used = Some(100.0);
        unlimited_main = false;
    }
    if unlimited_main {
        used = Some(0.0);
    }
    let used = clamp_percent(used.unwrap_or(0.0));
    let membership = membership_name(payload);
    let main_reset = main.as_ref().and_then(|row| row["reset_at"].as_str().map(str::to_string));

    json!({
        "used_percent": round1(used),
        "remaining_percent": round1(clamp_percent(100.0 - used)),
        "auto_percent_used": Value::Null,
        "api_percent_used": Value::Null,
        "total_percent_used": round1(used),
        "membership_type": membership,
        "billing_cycle_start": Value::Null,
        "billing_cycle_end": Value::Null,
        "days_remaining": Value::Null,
        "days_elapsed": Value::Null,
        "estimated_usable_days": Value::Null,
        "billing_mode": "percent",
        "used_cents": Value::Null,
        "limit_cents": Value::Null,
        "remaining_cents": Value::Null,
        "on_demand_used_cents": Value::Null,
        "on_demand_limit_cents": Value::Null,
        "pooled_used_cents": Value::Null,
        "pooled_limit_cents": Value::Null,
        "limit_type": "",
        "is_unlimited": unlimited_main,
        "is_x_subscription": false,
        "usage_reset_at": main_reset,
        "prepaid_remaining_cents": Value::Null,
        "grok_bot_percent_used": Value::Null,
        "grok_bot_remaining_percent": Value::Null,
        "grok_bot_period_start": Value::Null,
        "grok_bot_reset_at": Value::Null,
        "grok_bot_days_remaining": Value::Null,
        "windows": windows,
        "is_team_account": false,
        "shows_amount": false,
        "shows_grok_bot": false,
        "provider": "doubao",
        "dashboard_url": DASHBOARD_URL,
        "dashboard_button_label": "订阅",
        "dashboard_menu_label": "打开豆包订阅",
        "dashboard_link_label": "查看订阅 →",
    })
}

/// 主百分比：取「近 7 天」「每月」中较大的一档；都没有时退到「当前时段」。
fn main_window(windows: &[Value]) -> Option<Value> {
    let pick = |key: &str| -> Option<Value> {
        windows.iter().find(|row| row["key"].as_str() == Some(key)).cloned()
    };
    let long_term = [pick("7d"), pick("1m")]
        .into_iter()
        .flatten()
        .max_by(|a, b| {
            let left = as_f64(get(a, "used_percent")).unwrap_or(0.0);
            let right = as_f64(get(b, "used_percent")).unwrap_or(0.0);
            left.total_cmp(&right)
        });
    // 没有会员三档时，退到创作额度包 / 活动赠送额度，避免免费账号被判成 0%。
    long_term
        .or_else(|| pick("5h"))
        .or_else(|| pick("package"))
        .or_else(|| pick("incentive"))
}

/// 行内文案：`unlimited` → 暂无限制；`exhausted` → 已用完；
/// `less_than_one_percent` → 已用 <1%；有金额时 → 已用 x/y。
pub fn window_text(window: &Value) -> String {
    if as_bool(get(window, "unlimited")) {
        return "暂无限制".to_string();
    }
    if as_bool(get(window, "exhausted")) {
        return "已用完".to_string();
    }
    if as_bool(get(window, "less_than_one_percent")) {
        return "已用 <1%".to_string();
    }
    let used = as_string(get(window, "used_amount")).filter(|text| !text.trim().is_empty());
    let total = as_string(get(window, "total_amount")).filter(|text| !text.trim().is_empty());
    if let (Some(used), Some(total)) = (used, total) {
        return format!("已用 {used}/{total}");
    }
    let percent = clamp_percent(as_f64(get(window, "used_percent")).unwrap_or(0.0));
    format!("已用 {}", percent_text(percent))
}

/// 整数百分比不带小数点（10 → `10%`），其余保留一位（12.5 → `12.5%`）。
fn percent_text(percent: f64) -> String {
    let value = round1(percent);
    if (value - value.round()).abs() < 0.05 {
        format!("{}%", value.round() as i64)
    } else {
        format!("{value:.1}%")
    }
}

/// 「当前时段」的重置倒计时。没有 `end_time` 时返回空串，界面显示「开始使用后计时」。
pub fn reset_text(window: &Value, now: Option<&str>) -> String {
    let Some(reset) = window["reset_at"].as_str() else { return String::new() };
    let Some(reset_at) = parse_iso_seconds(reset) else { return String::new() };
    let now = parse_iso_seconds(now.unwrap_or("")).unwrap_or_else(|| {
        std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .map(|d| d.as_secs_f64())
            .unwrap_or(0.0)
    });
    let seconds = reset_at - now;
    if seconds <= 0.0 {
        return "即将重置".to_string();
    }
    let total_minutes = (seconds / 60.0).round().max(0.0) as i64;
    let hours = total_minutes / 60;
    let minutes = total_minutes % 60;
    if hours > 0 && minutes > 0 {
        format!("{hours} 小时 {minutes} 分后重置")
    } else if hours > 0 {
        format!("{hours} 小时后重置")
    } else {
        format!("{} 分后重置", minutes.max(1))
    }
}

fn parse_iso_seconds(text: &str) -> Option<f64> {
    crate::usage::parse_iso(Some(text))
}

pub fn is_auth_code(code: Option<i64>) -> bool {
    code == Some(LOGIN_INVALID_CODE)
}

/// 稳定账号标识：优先真实响应里的用户 id，其次 Cookie 里的用户 id。
/// 返回 `{user_id, source, account_id}`；找不到时 `account_id` 为空。
pub fn identity(payload: &Value, cookie: &str) -> Value {
    let data = child(payload, "data");
    let member = child(data, "member_info");
    let mut user_id = String::new();
    let mut source = "";
    for key in USER_ID_KEYS {
        for block in [member, data] {
            if let Some(text) = as_string(get(block, key)) {
                let trimmed = text.trim();
                if !trimmed.is_empty() {
                    user_id = trimmed.to_string();
                    source = "member_info";
                    break;
                }
            }
        }
        if !user_id.is_empty() {
            break;
        }
    }
    if user_id.is_empty() {
        let pairs = cookie_pairs(cookie);
        for name in COOKIE_ID_NAMES {
            if let Some((_, value)) = pairs.iter().find(|(key, _)| key.eq_ignore_ascii_case(name)) {
                if !value.trim().is_empty() {
                    user_id = value.trim().to_string();
                    source = "cookie";
                    break;
                }
            }
        }
    }
    json!({
        "user_id": user_id,
        "source": source,
        "account_id": if user_id.is_empty() { String::new() } else { account_id(&user_id, "user") },
    })
}

/// `doubao_` 前缀 + 摘要：只含字母数字下划线，可直接做历史文件名。
pub fn account_id(source: &str, salt: &str) -> String {
    let trimmed = source.trim();
    if trimmed.is_empty() {
        return String::new();
    }
    if let Some(rest) = trimmed.strip_prefix("doubao_") {
        if rest.chars().all(|ch| ch.is_ascii_alphanumeric() || ch == '_') && !rest.is_empty() {
            return trimmed.to_string();
        }
    }
    let mut hasher = Sha256::new();
    hasher.update(salt.as_bytes());
    hasher.update(b"|");
    hasher.update(trimmed.as_bytes());
    let digest = hasher.finalize();
    let hex: String = digest.iter().take(8).map(|byte| format!("{byte:02x}")).collect();
    format!("doubao_{hex}")
}

pub fn cookie_pairs(cookie: &str) -> Vec<(String, String)> {
    let trimmed = cookie.trim().strip_prefix("Cookie:").map(str::trim).unwrap_or(cookie.trim());
    let trimmed = trimmed.strip_prefix("cookie:").map(str::trim).unwrap_or(trimmed);
    let mut pairs = Vec::new();
    for part in trimmed.split([';', '\n', '\r']) {
        let piece = part.trim();
        if piece.is_empty() {
            continue;
        }
        let Some((name, value)) = piece.split_once('=') else { continue };
        let name = name.trim();
        let value = value.trim();
        if name.is_empty() || name.bytes().any(|b| !b.is_ascii() || b.is_ascii_whitespace()) {
            continue;
        }
        if value.bytes().any(|b| b.is_ascii_control()) {
            continue;
        }
        pairs.push((name.to_string(), value.to_string()));
    }
    pairs
}

/// 手动粘贴 Cookie 时先规整：去掉 `Cookie:` 前缀、换行和多余空白。
pub fn normalize_cookie(input: &str) -> String {
    let pairs = cookie_pairs(input);
    pairs
        .iter()
        .map(|(name, value)| format!("{name}={value}"))
        .collect::<Vec<_>>()
        .join("; ")
}

pub fn sanitize_provider(raw: &str) -> String {
    match raw.trim().to_ascii_lowercase().as_str() {
        "doubao" => "doubao".to_string(),
        _ => "cursor".to_string(),
    }
}
