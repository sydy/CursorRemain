use crate::events::{event_timestamp_ms, temp_end_iso};
use serde_json::{json, Value};

pub fn sanitize_kind(raw: Option<&str>) -> &'static str {
    let key = raw.unwrap_or("").trim().to_ascii_lowercase().replace('-', "_");
    if matches!(key.as_str(), "temporary" | "temp" | "short") { "temporary" } else { "long_term" }
}

pub fn compute_end_iso(start: Option<&str>, days: i64, hours: i64) -> Option<String> {
    temp_end_iso(start, days, hours)
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
