use crate::events::{event_timestamp_ms, temp_end_iso};
use serde_json::{json, Value};

pub fn sanitize_kind(raw: Option<&str>) -> &'static str {
    let key = raw.unwrap_or("").trim().to_ascii_lowercase().replace('-', "_");
    if matches!(key.as_str(), "temporary" | "temp" | "short") { "temporary" } else { "long_term" }
}

pub fn compute_end_iso(start: Option<&str>, days: i64, hours: i64) -> Option<String> {
    temp_end_iso(start, days, hours)
}

fn json_num(val: &Value) -> i64 {
    if let Some(i) = val.as_i64() { return i; }
    if let Some(f) = val.as_f64() { return f as i64; }
    if let Some(s) = val.as_str() { return s.trim().parse::<i64>().unwrap_or(0); }
    0
}

pub fn clamp_days(value: &Value) -> i64 {
    json_num(value).clamp(0, 999)
}

pub fn clamp_hours(value: &Value) -> i64 {
    json_num(value).clamp(0, 23)
}

pub fn validity_equals(
    a_kind: Option<&str>,
    a_start: Option<&str>,
    a_days: &Value,
    a_hours: &Value,
    b_kind: Option<&str>,
    b_start: Option<&str>,
    b_days: &Value,
    b_hours: &Value,
) -> bool {
    sanitize_kind(a_kind) == sanitize_kind(b_kind)
        && a_start.unwrap_or("").trim() == b_start.unwrap_or("").trim()
        && clamp_days(a_days) == clamp_days(b_days)
        && clamp_hours(a_hours) == clamp_hours(b_hours)
}

pub fn apply_end_override(account: &Value, api_end: Option<&str>, now: Option<&str>, days_remaining: i64) -> Value {
    let temporary = sanitize_kind(account["account_kind"].as_str()) == "temporary";
    let end = if temporary { compute_end_iso(account["temp_start_at"].as_str(), account["temp_valid_days"].as_i64().unwrap_or(0), account["temp_valid_hours"].as_i64().unwrap_or(0)) } else { None };
    if let Some(end) = end {
        let now_ms = now.and_then(event_timestamp_ms);
        let end_ms = event_timestamp_ms(&end);
        let days = match (now_ms, end_ms) {
            (Some(now), Some(end)) => ((end - now).max(0) as f64 / 86_400_000.0).floor() as i64,
            _ => days_remaining,
        };
        json!({"billing_cycle_end": end, "days_remaining": days, "overridden": true, "api_billing_cycle_end": api_end})
    } else {
        json!({"billing_cycle_end": api_end, "days_remaining": days_remaining, "overridden": false, "api_billing_cycle_end": Value::Null})
    }
}
