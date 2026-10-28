use crate::events::{self, merge as merge_events};
use crate::jsonutil::{as_f64, as_int, get, has};
use crate::login::sanitize_email;
use crate::validity::sanitize_kind;
use serde_json::{json, Map, Value};
use std::collections::BTreeMap;

const SETTINGS_KEYS: [&str; 7] = [
    "refresh_interval_minutes",
    "alert_thresholds",
    "notify_enabled",
    "notify_exhaustion_risk",
    "tray_display_mode",
    "monthly_plan_usd",
    "usd_cny_rate",
];

/// Parse a decrypted account-sync snapshot. Crypto and files stay on each shell.
pub fn parse_snapshot(raw: &Value) -> Value {
    let mut accounts = Vec::new();
    if let Some(rows) = raw.get("accounts").and_then(Value::as_array) {
        for item in rows {
            if !item.is_object() {
                continue;
            }
            let id = text(item, "id");
            if id.is_empty() {
                continue;
            }
            accounts.push(account_from(item, &id));
        }
    }
    let mut deleted = Vec::new();
    if let Some(rows) = raw.get("deleted").and_then(Value::as_array) {
        for item in rows {
            deleted.push(json!({
                "id": text(item, "id"),
                "deleted_at": text(item, "deleted_at"),
            }));
        }
    }
    let settings = raw.get("settings").filter(|v| v.is_object()).map(snapshot_settings);
    let usage = raw.get("usage").filter(|v| v.is_array()).map(|rows| {
        rows.as_array()
            .into_iter()
            .flatten()
            .filter_map(parse_usage)
            .collect::<Vec<_>>()
    });
    json!({
        "version": 1,
        "updated_at": text(raw, "updated_at"),
        "device_id": text(raw, "device_id"),
        "active_account_id": text(raw, "active_account_id"),
        "active_account_updated_at": text(raw, "active_account_updated_at"),
        "accounts": accounts,
        "deleted": sanitize_deleted(&deleted),
        "settings": settings,
        "usage": usage,
    })
}

pub fn merge_snapshots(local: &Value, remote: &Value) -> Value {
    let local = parse_snapshot(local);
    let remote = parse_snapshot(remote);
    let mut tombstones: BTreeMap<String, String> = BTreeMap::new();
    for row in local["deleted"].as_array().into_iter().flatten().chain(remote["deleted"].as_array().into_iter().flatten()) {
        let id = row["id"].as_str().unwrap_or("").to_string();
        let at = row["deleted_at"].as_str().unwrap_or("").to_string();
        if id.is_empty() {
            continue;
        }
        match tombstones.get(&id) {
            Some(prev) if compare_iso(&at, prev) < 0 => {}
            _ => {
                tombstones.insert(id, at);
            }
        }
    }

    let mut chosen: BTreeMap<String, Value> = BTreeMap::new();
    for src in local["accounts"].as_array().into_iter().flatten().chain(remote["accounts"].as_array().into_iter().flatten()) {
        let acc = snapshot_account(src);
        let id = acc["id"].as_str().unwrap_or("");
        let token = acc["token"].as_str().unwrap_or("");
        if id.is_empty() || token.is_empty() {
            continue;
        }
        if let Some(tomb) = tombstones.get(id) {
            if compare_iso(tomb, acc["sync_updated_at"].as_str().unwrap_or("")) >= 0 {
                continue;
            }
        }
        if let Some(prev) = chosen.get(id) {
            let merged = combine_accounts(prev, &acc);
            chosen.insert(id.to_string(), merged);
        } else {
            chosen.insert(id.to_string(), acc);
        }
    }
    tombstones.retain(|id, _| !chosen.contains_key(id));

    let local_active_at = fallback_stamp(&local, "active_account_updated_at", "updated_at");
    let remote_active_at = fallback_stamp(&remote, "active_account_updated_at", "updated_at");
    let remote_newer = compare_iso(&remote_active_at, &local_active_at) > 0;
    let mut active = if remote_newer { text(&remote, "active_account_id") } else { text(&local, "active_account_id") };
    let active_at = if remote_newer { remote_active_at } else { local_active_at };
    let settings = merge_settings(local.get("settings"), remote.get("settings"), local["updated_at"].as_str(), remote["updated_at"].as_str());
    if !chosen.contains_key(&active) {
        active = chosen.keys().next().cloned().unwrap_or_default();
    }
    let accounts: Vec<Value> = chosen.into_values().collect();
    let keep: std::collections::BTreeSet<String> = accounts.iter().filter_map(|a| a["id"].as_str().map(str::to_string)).collect();
    let deleted: Vec<Value> = tombstones.into_iter().map(|(id, deleted_at)| json!({"id": id, "deleted_at": deleted_at})).collect();
    json!({
        "version": 1,
        "updated_at": newer_iso(local["updated_at"].as_str(), remote["updated_at"].as_str()),
        "device_id": if text(&local, "device_id").is_empty() { text(&remote, "device_id") } else { text(&local, "device_id") },
        "active_account_id": active,
        "active_account_updated_at": active_at,
        "accounts": accounts,
        "deleted": deleted,
        "settings": settings,
        "usage": merge_usage(local.get("usage"), remote.get("usage"), &keep),
    })
}

fn account_from(item: &Value, id: &str) -> Value {
    let last_remaining = item.get("last_remaining").filter(|v| v.is_number()).and_then(Value::as_f64);
    json!({
        "id": id,
        "label": text(item, "label"),
        "email": sanitize_email(item.get("email").and_then(Value::as_str)),
        "password": str_field(item, "password"),
        "token": text(item, "token"),
        "membership_type": text(item, "membership_type"),
        "account_kind": sanitize_kind(item.get("account_kind").and_then(Value::as_str)),
        "temp_start_at": text(item, "temp_start_at"),
        "temp_valid_days": clamp_int(int_val(item, "temp_valid_days"), 0, 999),
        "temp_valid_hours": clamp_int(int_val(item, "temp_valid_hours"), 0, 23),
        "actual_cny": clamp_actual(double_val(item, "actual_cny")),
        "channel": events::sanitize_channel(item.get("channel").and_then(Value::as_str)),
        "sync_updated_at": text(item, "sync_updated_at"),
        "last_remaining": last_remaining,
        "last_error": str_field(item, "last_error"),
        "usage_updated_at": text(item, "usage_updated_at"),
        "billing_cycle_start": text(item, "billing_cycle_start"),
        "billing_cycle_end": text(item, "billing_cycle_end"),
    })
}

fn snapshot_account(account: &Value) -> Value {
    account_from(account, &text(account, "id"))
}

fn combine_accounts(left: &Value, right: &Value) -> Value {
    let ident_cmp = compare_iso(left["sync_updated_at"].as_str().unwrap_or(""), right["sync_updated_at"].as_str().unwrap_or(""));
    let ident = if ident_cmp > 0 {
        left
    } else if ident_cmp < 0 {
        right
    } else if !left["token"].as_str().unwrap_or("").is_empty() || right["token"].as_str().unwrap_or("").is_empty() {
        left
    } else {
        right
    };
    let usage_cmp = compare_iso(left["usage_updated_at"].as_str().unwrap_or(""), right["usage_updated_at"].as_str().unwrap_or(""));
    let usage = if usage_cmp > 0 {
        left
    } else if usage_cmp < 0 {
        right
    } else if left.get("last_remaining").is_some_and(|v| !v.is_null()) || right.get("last_remaining").is_none_or(Value::is_null) {
        left
    } else {
        right
    };
    let mut merged = snapshot_account(ident);
    if merged["email"].as_str().unwrap_or("").is_empty() {
        let email = if left["email"].as_str().unwrap_or("").is_empty() { right["email"].as_str().unwrap_or("") } else { left["email"].as_str().unwrap_or("") };
        merged["email"] = json!(sanitize_email(Some(email)));
    }
    if merged["password"].as_str().unwrap_or("").is_empty() {
        let password = if left["password"].as_str().unwrap_or("").is_empty() { right["password"].as_str().unwrap_or("") } else { left["password"].as_str().unwrap_or("") };
        merged["password"] = json!(password);
    }
    merged["last_remaining"] = usage.get("last_remaining").cloned().unwrap_or(Value::Null);
    merged["last_error"] = usage.get("last_error").cloned().unwrap_or(json!(""));
    merged["usage_updated_at"] = usage.get("usage_updated_at").cloned().unwrap_or(json!(""));
    merged["billing_cycle_start"] = usage.get("billing_cycle_start").cloned().unwrap_or(json!(""));
    merged["billing_cycle_end"] = usage.get("billing_cycle_end").cloned().unwrap_or(json!(""));
    merged
}

fn parse_usage(item: &Value) -> Option<Value> {
    let account_id = text(item, "account_id");
    if account_id.is_empty() {
        return None;
    }
    let mut history = Vec::new();
    if let Some(points) = item.get("history").and_then(Value::as_array) {
        for point in points {
            let Some(ts) = point.get("ts").filter(|v| v.is_number()).and_then(Value::as_f64) else { continue };
            let Some(remaining) = point.get("remaining").filter(|v| v.is_number()).and_then(Value::as_f64) else { continue };
            history.push(json!({
                "ts": ts,
                "remaining": remaining,
                "auto": num_opt(point, "auto"),
                "api": num_opt(point, "api"),
            }));
        }
    }
    let events = parse_events(item.get("events"));
    let team = parse_events(item.get("team_events"));
    Some(json!({
        "account_id": account_id,
        "history": history,
        "events": merge_events(&events, &[]),
        "team_events": merge_events(&team, &[]),
    }))
}

fn parse_events(raw: Option<&Value>) -> Vec<Value> {
    let Some(rows) = raw.and_then(Value::as_array) else { return Vec::new() };
    rows.iter().filter_map(stored_event).collect()
}

fn stored_event(item: &Value) -> Option<Value> {
    if !item.is_object() {
        return None;
    }
    let ts = item.get("timestamp_ms").and_then(as_int).or_else(|| item.get("timestampMs").and_then(as_int)).or_else(|| {
        let stamp = item.get("timestamp").and_then(Value::as_str).unwrap_or("");
        if stamp.contains('T') { events::event_timestamp_ms(stamp) } else { item.get("timestamp").and_then(as_int) }
    })?;
    let id = item.get("id").and_then(Value::as_str).unwrap_or("").trim().to_string();
    Some(json!({
        "id": id,
        "timestamp_ms": ts,
        "model": item.get("model").and_then(Value::as_str).unwrap_or(""),
        "kind": item.get("kind").and_then(Value::as_str).unwrap_or("其他"),
        "user_email": item.get("user_email").and_then(Value::as_str).unwrap_or(""),
        "owning_user": item.get("owning_user").and_then(Value::as_str).unwrap_or(""),
        "tokens": item.get("tokens").and_then(as_int).unwrap_or(0).max(0),
        "input_tokens": item.get("input_tokens").and_then(as_int).unwrap_or(0).max(0),
        "output_tokens": item.get("output_tokens").and_then(as_int).unwrap_or(0).max(0),
        "cache_write_tokens": item.get("cache_write_tokens").and_then(as_int).unwrap_or(0).max(0),
        "cache_read_tokens": item.get("cache_read_tokens").and_then(as_int).unwrap_or(0).max(0),
        "charged_cents": item.get("charged_cents").and_then(as_f64).or_else(|| item.get("chargedCents").and_then(as_f64)),
        "total_cents": item.get("total_cents").and_then(as_f64).or_else(|| item.get("totalCents").and_then(as_f64)),
        "is_headless": item.get("is_headless").and_then(Value::as_bool).unwrap_or(false),
        "is_chargeable": item.get("is_chargeable").and_then(Value::as_bool).unwrap_or(false),
    }))
}

fn merge_history(left: &[Value], right: &[Value]) -> Vec<Value> {
    let mut best: BTreeMap<i64, Value> = BTreeMap::new();
    for src in left.iter().chain(right) {
        let Some(ts) = src.get("ts").and_then(Value::as_f64) else { continue };
        let key = (ts * 1000.0).round() as i64;
        let replace = match best.get(&key) {
            None => true,
            Some(prev) => (src.get("auto").is_some_and(|v| !v.is_null()) || src.get("api").is_some_and(|v| !v.is_null()))
                && prev.get("auto").is_none_or(Value::is_null)
                && prev.get("api").is_none_or(Value::is_null),
        };
        if replace {
            best.insert(key, src.clone());
        }
    }
    best.into_values().collect()
}

fn merge_usage(local: Option<&Value>, remote: Option<&Value>, keep: &std::collections::BTreeSet<String>) -> Option<Vec<Value>> {
    if local.is_none() && remote.is_none() {
        return None;
    }
    let mut rows: BTreeMap<String, Value> = BTreeMap::new();
    for src in local.and_then(Value::as_array).into_iter().flatten().chain(remote.and_then(Value::as_array).into_iter().flatten()) {
        let Some(row) = parse_usage(src) else { continue };
        let id = row["account_id"].as_str().unwrap_or("").to_string();
        if id.is_empty() || (!keep.is_empty() && !keep.contains(&id)) {
            continue;
        }
        if let Some(prev) = rows.get(&id) {
            let events = events_of(prev, "events");
            let incoming = events_of(&row, "events");
            let team = events_of(prev, "team_events");
            let team_in = events_of(&row, "team_events");
            rows.insert(id.clone(), json!({
                "account_id": id,
                "history": merge_history(prev["history"].as_array().map(Vec::as_slice).unwrap_or(&[]), row["history"].as_array().map(Vec::as_slice).unwrap_or(&[])),
                "events": merge_events(&events, &incoming),
                "team_events": merge_events(&team, &team_in),
            }));
        } else {
            rows.insert(id, row);
        }
    }
    Some(rows.into_values().collect())
}

fn events_of(row: &Value, key: &str) -> Vec<Value> {
    row.get(key).and_then(Value::as_array).cloned().unwrap_or_default()
}

fn snapshot_settings(settings: &Value) -> Value {
    let mode = text(settings, "tray_display_mode").to_ascii_lowercase();
    let mode = if matches!(mode.as_str(), "ring" | "number" | "dot") { mode } else { "ring".to_string() };
    let minutes = int_val(settings, "refresh_interval_minutes").unwrap_or(10).max(1);
    let rate_raw = double_val(settings, "usd_cny_rate");
    let mut fields = Map::new();
    if let Some(obj) = settings.get("field_updated_at").and_then(Value::as_object) {
        for (key, value) in obj {
            fields.insert(key.clone(), json!(value.as_str().unwrap_or("")));
        }
    }
    let row = json!({
        "refresh_interval_minutes": minutes,
        "alert_thresholds": parse_thresholds(settings.get("alert_thresholds").unwrap_or(&Value::Null)),
        "notify_enabled": settings.get("notify_enabled").and_then(Value::as_bool).unwrap_or(true),
        "notify_exhaustion_risk": settings.get("notify_exhaustion_risk").and_then(Value::as_bool).unwrap_or(true),
        "tray_display_mode": mode,
        "monthly_plan_usd": clamp_monthly(double_val(settings, "monthly_plan_usd")),
        "usd_cny_rate": clamp_rate(if rate_raw == 0.0 { 7.5 } else { rate_raw }),
        "field_updated_at": Value::Object(fields),
    });
    copy_settings(&row)
}

fn copy_settings(settings: &Value) -> Value {
    let mode = text(settings, "tray_display_mode");
    let mode = if matches!(mode.as_str(), "ring" | "number" | "dot") { mode } else { "ring".to_string() };
    let parsed = parse_thresholds(&settings["alert_thresholds"]);
    let thresholds = if parsed.is_empty() { vec![50, 20, 5] } else { parsed };
    json!({
        "refresh_interval_minutes": settings["refresh_interval_minutes"].as_i64().unwrap_or(10).max(1),
        "alert_thresholds": thresholds,
        "notify_enabled": settings["notify_enabled"].as_bool().unwrap_or(true),
        "notify_exhaustion_risk": settings["notify_exhaustion_risk"].as_bool().unwrap_or(true),
        "tray_display_mode": mode,
        "monthly_plan_usd": clamp_monthly(settings["monthly_plan_usd"].as_f64().unwrap_or(0.0)),
        "usd_cny_rate": clamp_rate(settings["usd_cny_rate"].as_f64().unwrap_or(7.5)),
        "field_updated_at": sanitize_field_updated_at(settings.get("field_updated_at")),
    })
}

fn merge_settings(local: Option<&Value>, remote: Option<&Value>, local_fallback: Option<&str>, remote_fallback: Option<&str>) -> Option<Value> {
    let (Some(local), Some(remote)) = (local.filter(|v| !v.is_null()), remote.filter(|v| !v.is_null())) else {
        return local.filter(|v| !v.is_null()).or(remote.filter(|v| !v.is_null())).map(snapshot_settings);
    };
    let left = snapshot_settings(local);
    let right = snapshot_settings(remote);
    let mut merged = copy_settings(&left);
    let mut stamps = Map::new();
    for key in SETTINGS_KEYS {
        let l_at = field_stamp(&left, key).unwrap_or_else(|| local_fallback.unwrap_or("").to_string());
        let r_at = field_stamp(&right, key).unwrap_or_else(|| remote_fallback.unwrap_or("").to_string());
        if compare_iso(&r_at, &l_at) > 0 {
            set_settings_field(&mut merged, key, &right);
            if !r_at.trim().is_empty() {
                stamps.insert(key.to_string(), json!(r_at));
            }
        } else if !l_at.trim().is_empty() {
            stamps.insert(key.to_string(), json!(l_at));
        }
    }
    merged["field_updated_at"] = sanitize_field_updated_at(Some(&Value::Object(stamps)));
    Some(copy_settings(&merged))
}

fn set_settings_field(row: &mut Value, key: &str, src: &Value) {
    if let Some(obj) = row.as_object_mut() {
        obj.insert(key.to_string(), src.get(key).cloned().unwrap_or(Value::Null));
    }
}

fn field_stamp(row: &Value, key: &str) -> Option<String> {
    row.get("field_updated_at").and_then(|map| map.get(key)).and_then(Value::as_str).map(str::to_string)
}

fn sanitize_field_updated_at(raw: Option<&Value>) -> Value {
    let mut out = Map::new();
    let Some(obj) = raw.and_then(Value::as_object) else { return Value::Object(out) };
    for key in SETTINGS_KEYS {
        if let Some(stamp) = obj.get(key).and_then(Value::as_str) {
            let stamp = stamp.trim();
            if !stamp.is_empty() && parse_iso_ms(stamp).is_some() {
                out.insert(key.to_string(), json!(stamp));
            }
        }
    }
    Value::Object(out)
}

fn sanitize_deleted(raw: &[Value]) -> Vec<Value> {
    let mut best: BTreeMap<String, String> = BTreeMap::new();
    for item in raw {
        let id = item["id"].as_str().unwrap_or("").trim().to_string();
        let deleted_at = item["deleted_at"].as_str().unwrap_or("").trim().to_string();
        if id.is_empty() || deleted_at.is_empty() {
            continue;
        }
        match best.get(&id) {
            Some(prev) if compare_iso(&deleted_at, prev) < 0 => {}
            _ => {
                best.insert(id, deleted_at);
            }
        }
    }
    best.into_iter().map(|(id, deleted_at)| json!({"id": id, "deleted_at": deleted_at})).collect()
}

fn parse_thresholds(value: &Value) -> Vec<i64> {
    let mut nums = Vec::new();
    match value {
        Value::String(text) => {
            for part in text.replace('，', ",").split(',') {
                let part = part.trim();
                if part.is_empty() {
                    continue;
                }
                if let Ok(n) = part.parse::<f64>() {
                    nums.push(n as i64);
                }
            }
        }
        Value::Array(rows) => {
            for item in rows {
                if let Some(n) = item.as_i64() {
                    nums.push(n);
                } else if let Some(n) = item.as_f64() {
                    nums.push(n as i64);
                }
            }
        }
        Value::Null => return vec![50, 20, 5],
        _ => return vec![50, 20, 5],
    }
    let mut cleaned: Vec<i64> = nums.into_iter().filter(|n| (1..=100).contains(n)).collect();
    cleaned.sort_by(|a, b| b.cmp(a));
    cleaned.dedup();
    if cleaned.is_empty() { vec![50, 20, 5] } else { cleaned }
}

fn fallback_stamp(snap: &Value, primary: &str, fallback: &str) -> String {
    let primary = text(snap, primary);
    if primary.trim().is_empty() { text(snap, fallback) } else { primary }
}

fn newer_iso(left: Option<&str>, right: Option<&str>) -> String {
    let left = left.unwrap_or("");
    let right = right.unwrap_or("");
    if compare_iso(left, right) >= 0 { left.to_string() } else { right.to_string() }
}

fn compare_iso(left: &str, right: &str) -> i32 {
    match (parse_iso_ms(left), parse_iso_ms(right)) {
        (None, None) => 0,
        (None, Some(_)) => -1,
        (Some(_), None) => 1,
        (Some(a), Some(b)) => a.cmp(&b) as i32,
    }
}

/// Enough of DateTimeOffset.TryParse for snapshot stamps: date, optional time, Z or numeric offset.
fn parse_iso_ms(value: &str) -> Option<i64> {
    let text = value.trim();
    if text.is_empty() {
        return None;
    }
    let (date, rest) = if let Some((date, rest)) = text.split_once('T').or_else(|| text.split_once(' ')) {
        (date, rest)
    } else {
        (text, "")
    };
    let mut ymd = date.split('-');
    let year: i32 = ymd.next()?.parse().ok()?;
    let month: u32 = ymd.next()?.parse().ok()?;
    let day: u32 = ymd.next()?.parse().ok()?;
    if ymd.next().is_some() || !(1..=12).contains(&month) || day == 0 || day > days_in_month(year, month) {
        return None;
    }
    let rest = rest.trim();
    let (time, offset_minutes) = split_offset(rest)?;
    let (hour, minute, second, millis) = if time.is_empty() {
        (0, 0, 0, 0)
    } else {
        parse_hms(time)?
    };
    if hour > 23 || minute > 59 || second > 60 {
        return None;
    }
    let days = days_from_civil(year, month, day)?;
    let stamp = days * 86_400_000 + hour as i64 * 3_600_000 + minute as i64 * 60_000 + second as i64 * 1000 + millis;
    Some(stamp - offset_minutes as i64 * 60_000)
}

fn split_offset(rest: &str) -> Option<(&str, i32)> {
    if rest.is_empty() {
        return Some(("", 0));
    }
    let rest = rest.strip_suffix('Z').or_else(|| rest.strip_suffix('z')).unwrap_or(rest);
    if let Some(index) = rest.rfind(['+', '-']) {
        if index == 0 {
            return None;
        }
        let sign = if rest.as_bytes()[index] == b'-' { -1 } else { 1 };
        let body = &rest[index + 1..];
        let (oh, om) = if let Some((h, m)) = body.split_once(':') {
            (h.parse::<i32>().ok()?, m.parse::<i32>().ok()?)
        } else if body.len() == 4 && body.chars().all(|c| c.is_ascii_digit()) {
            (body[..2].parse().ok()?, body[2..].parse().ok()?)
        } else {
            (body.parse::<i32>().ok()?, 0)
        };
        if !(0..=14).contains(&oh) || !(0..=59).contains(&om) {
            return None;
        }
        return Some((&rest[..index], sign * (oh * 60 + om)));
    }
    Some((rest, 0))
}

fn parse_hms(time: &str) -> Option<(u32, u32, u32, i64)> {
    let mut parts = time.split(':');
    let hour: u32 = parts.next()?.parse().ok()?;
    let minute: u32 = parts.next().unwrap_or("0").parse().ok()?;
    let seconds = parts.next().unwrap_or("0");
    if parts.next().is_some() {
        return None;
    }
    let (whole, frac) = seconds.split_once('.').unwrap_or((seconds, ""));
    let second: u32 = whole.parse().ok()?;
    let millis = if frac.is_empty() {
        0
    } else {
        format!("{frac:0<3}").chars().take(3).collect::<String>().parse::<i64>().unwrap_or(0)
    };
    Some((hour, minute, second, millis))
}

fn days_in_month(year: i32, month: u32) -> u32 {
    match month {
        1 | 3 | 5 | 7 | 8 | 10 | 12 => 31,
        4 | 6 | 9 | 11 => 30,
        2 => {
            if year % 400 == 0 || (year % 4 == 0 && year % 100 != 0) { 29 } else { 28 }
        }
        _ => 0,
    }
}

fn days_from_civil(year: i32, month: u32, day: u32) -> Option<i64> {
    if !(1..=12).contains(&month) || day == 0 || day > days_in_month(year, month) {
        return None;
    }
    let y = if month <= 2 { year as i64 - 1 } else { year as i64 };
    let era = if y >= 0 { y } else { y - 399 } / 400;
    let yoe = (y - era * 400) as u64;
    let mp = if month > 2 { month as u64 - 3 } else { month as u64 + 9 };
    let doy = (153 * mp + 2) / 5 + day as u64 - 1;
    let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    Some(era * 146_097 + doe as i64 - 719_468)
}

fn text(value: &Value, key: &str) -> String {
    get(value, key).as_str().unwrap_or("").trim().to_string()
}

fn str_field(value: &Value, key: &str) -> String {
    if !has(value, key) {
        return String::new();
    }
    value.get(key).and_then(Value::as_str).unwrap_or("").to_string()
}

fn int_val(value: &Value, key: &str) -> Option<i64> {
    let raw = value.get(key)?;
    if let Some(n) = raw.as_i64() {
        return Some(n);
    }
    if let Some(n) = raw.as_f64() {
        return Some(n as i64);
    }
    raw.as_str().and_then(|text| text.trim().parse::<i64>().ok())
}

fn double_val(value: &Value, key: &str) -> f64 {
    let Some(raw) = value.get(key) else { return 0.0 };
    if let Some(n) = raw.as_f64() {
        return n;
    }
    raw.as_str()
        .and_then(|text| text.trim().replace('，', ".").parse::<f64>().ok())
        .unwrap_or(0.0)
}

fn num_opt(value: &Value, key: &str) -> Value {
    match value.get(key) {
        Some(Value::Number(n)) => Value::Number(n.clone()),
        _ => Value::Null,
    }
}

fn clamp_int(value: Option<i64>, lo: i64, hi: i64) -> i64 {
    value.unwrap_or(0).clamp(lo, hi)
}

fn clamp_monthly(usd: f64) -> f64 {
    if !usd.is_finite() || usd < 0.0 { 0.0 } else { usd.min(10_000.0) }
}

fn clamp_actual(amount: f64) -> f64 {
    if !amount.is_finite() || amount < 0.0 { 0.0 } else { amount.min(1_000_000.0) }
}

fn clamp_rate(rate: f64) -> f64 {
    if !rate.is_finite() { 7.5 } else { rate.clamp(0.01, 100.0) }
}
