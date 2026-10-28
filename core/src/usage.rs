use crate::jsonutil::{as_bool, as_f64, as_int, as_string, child, first_present, get, has, is_empty, opt_bool};
use crate::numbers::{clamp_percent, round1, round2};
use serde_json::{json, Value};
use std::time::{SystemTime, UNIX_EPOCH};

const TEAM_MEMBERSHIPS: [&str; 5] = ["enterprise", "enterprise_trial", "team", "teams", "business"];
const X_MEMBERSHIPS: [&str; 7] = ["plus", "premium", "super", "x_plus", "x_premium", "x_super", "x_ultra"];
const TOKEN_FIELDS: [&str; 8] = [
    "inputTokens", "outputTokens", "cacheWriteTokens", "cacheReadTokens",
    "totalInputTokens", "totalOutputTokens", "totalCacheWriteTokens", "totalCacheReadTokens",
];

pub fn format_membership_type(raw: Option<&str>) -> String {
    let key = raw.unwrap_or("").trim();
    if key.is_empty() {
        return "未知".to_string();
    }
    match key.to_ascii_lowercase().as_str() {
        "pro" => "Pro",
        "pro_plus" | "pro+" => "Pro+",
        "ultra" => "Ultra",
        "free" | "unpaid" => "Free",
        "hobby" => "Hobby",
        "enterprise" | "enterprise_trial" => "Enterprise",
        "team" | "teams" => "Team",
        "business" => "Business",
        "plus" | "x_plus" => "Plus",
        "premium" | "x_premium" => "Premium",
        "super" | "x_super" => "Super",
        "x_ultra" | "x-ultra" => "Ultra",
        _ => return key.to_string(),
    }
    .to_string()
}

pub fn membership_key(raw: Option<&str>) -> String {
    let mut key: String = raw
        .unwrap_or("")
        .trim()
        .chars()
        .filter(|ch| *ch != ' ')
        .collect();
    key = key.to_ascii_lowercase().replace('-', "_");
    key.strip_suffix("套餐").unwrap_or(&key).to_string()
}

pub fn is_team_membership(membership: Option<&str>, limit_type: Option<&str>) -> bool {
    if limit_type.unwrap_or("").trim().eq_ignore_ascii_case("team") {
        return true;
    }
    TEAM_MEMBERSHIPS.contains(&membership.unwrap_or("").trim().to_ascii_lowercase().as_str())
}

pub fn is_x_membership(raw: Option<&str>) -> bool {
    X_MEMBERSHIPS.contains(&membership_key(raw).as_str())
}

pub fn dashboard_url(membership: &str, limit_type: &str, x_subscription: bool) -> &'static str {
    if x_subscription || is_x_membership(Some(membership)) {
        "https://grok.com/dashboard"
    } else if is_team_membership(Some(membership), Some(limit_type)) {
        "https://cursor.com/dashboard/usage"
    } else {
        "https://cursor.com/dashboard/billing"
    }
}

pub fn dashboard_menu_label(membership: &str, limit_type: &str, x_subscription: bool) -> &'static str {
    if x_subscription || is_x_membership(Some(membership)) || is_team_membership(Some(membership), Some(limit_type)) {
        "打开用量"
    } else {
        "打开用量账单"
    }
}

pub fn dashboard_button_label(membership: &str, limit_type: &str, x_subscription: bool) -> &'static str {
    if x_subscription || is_x_membership(Some(membership)) || is_team_membership(Some(membership), Some(limit_type)) {
        "用量"
    } else {
        "账单"
    }
}

pub fn dashboard_link_label(membership: &str, x_subscription: bool, team_account: bool) -> &'static str {
    if x_subscription || is_x_membership(Some(membership)) || team_account {
        "查看用量 →"
    } else {
        "查看用量账单 →"
    }
}

pub fn format_usd_cents(cents: Option<f64>) -> String {
    let Some(cents) = cents else { return "—".to_string() };
    let dollars = (cents / 100.0).max(0.0);
    if (dollars - dollars.round()).abs() < 0.005 {
        format!("${}", dollars.round() as i64)
    } else {
        format!("${dollars:.2}")
    }
}

pub fn format_spend_range(used: Option<f64>, limit: Option<f64>) -> String {
    format!("{} / {}", format_usd_cents(used), format_usd_cents(limit))
}

pub fn format_token_count(count: Option<f64>) -> String {
    let Some(count) = count else { return "—".to_string() };
    let n = count.round().max(0.0) as u64;
    if n >= 100_000_000 {
        format!("{:.1}亿", n as f64 / 100_000_000.0)
    } else if n >= 10_000 {
        format!("{:.1}万", n as f64 / 10_000.0)
    } else {
        n.to_string()
    }
}

pub fn is_grok_bot_model(name: Option<&str>) -> bool {
    let key = name.unwrap_or("").trim().to_ascii_lowercase();
    key.starts_with("grok-bot-") || key.starts_with("sand-")
}

pub fn is_first_party_model(name: Option<&str>) -> bool {
    let key = name.unwrap_or("").trim().to_ascii_lowercase();
    if key.is_empty() || is_grok_bot_model(Some(&key)) {
        return false;
    }
    key == "auto" || key == "default" || key.starts_with("cursor-") || key.starts_with("composer-") || key.starts_with("grok-")
}

pub fn usage_category(name: Option<&str>) -> &'static str {
    if is_grok_bot_model(name) { "grok_bot" } else if is_first_party_model(name) { "first_party" } else { "api" }
}

#[derive(Clone, Debug)]
struct Meter {
    used: f64,
    limit: f64,
    remaining: Option<f64>,
}

pub fn parse_usage_summary(payload: &Value, now: Option<&str>) -> Value {
    let clock = parse_now(now);
    let individual = child(payload, "individualUsage");
    let team = child(payload, "teamUsage");
    let plan = child(individual, "plan");
    let overall = child(individual, "overall");
    let pooled = child(team, "pooled");
    let individual_od = child(individual, "onDemand");
    let team_od = child(team, "onDemand");
    let mut auto = as_f64(get(plan, "autoPercentUsed"));
    let mut api = as_f64(get(plan, "apiPercentUsed"));
    let total = as_f64(get(plan, "totalPercentUsed"));
    if auto.is_none() {
        auto = percent_from_display(as_string(get(payload, "autoModelSelectedDisplayMessage")).as_deref());
    }
    if api.is_none() {
        api = percent_from_display(as_string(get(payload, "namedModelSelectedDisplayMessage")).as_deref());
    }
    let raw_membership = as_string(get(payload, "membershipType")).or_else(|| as_string(get(payload, "plan"))).unwrap_or_default();
    let membership = format_membership_type(Some(&raw_membership));
    let limit_type = as_string(get(payload, "limitType")).unwrap_or_default().trim().to_string();
    let cycle_start_raw = as_string(get(payload, "billingCycleStart")).or_else(|| as_string(get(payload, "startOfMonth")));
    let cycle_end_raw = as_string(get(payload, "billingCycleEnd"));
    let x_plan = looks_like_x_plan(Some(&raw_membership), payload, cycle_start_raw.as_deref(), cycle_end_raw.as_deref());
    let is_unlimited = as_bool(get(payload, "isUnlimited"));
    let plan_meter = spend_meter(plan, false);
    let plan_breakdown = spend_meter(plan, true);
    let overall_meter = spend_meter(overall, false);
    let pooled_meter = spend_meter(pooled, false);
    let mut od_block = if on_demand_enabled(individual_od) { individual_od } else { team_od };
    if !on_demand_enabled(od_block) && on_demand_enabled(team_od) {
        od_block = team_od;
    }
    let od_meter = if on_demand_enabled(od_block) { spend_meter(od_block, false) } else { None };

    let mut billing_mode = "percent";
    let mut used_cents = None;
    let mut limit_cents = None;
    let mut remaining_cents = None;
    let mut used_percent = None;
    if is_unlimited {
        used_percent = Some(0.0);
    } else {
        if is_team_membership(Some(&membership), Some(&limit_type)) && !x_plan {
            for meter in [overall_meter.clone(), pooled_meter.clone(), plan_meter.clone()] {
                if let Some(applied) = percent_from_spend_meter(meter.as_ref()) {
                    used_cents = Some(applied.0);
                    limit_cents = Some(applied.1);
                    remaining_cents = applied.2;
                    used_percent = Some(applied.3);
                    billing_mode = "amount";
                    break;
                }
            }
        }
        if used_percent.is_none() {
            if let Some(total) = total {
                used_percent = Some(total);
                if let Some(meter) = meter_with_limit(plan_meter.as_ref()).or_else(|| meter_with_limit(overall_meter.as_ref())) {
                    used_cents = Some(meter.used);
                    limit_cents = Some(meter.limit);
                    remaining_cents = meter.remaining;
                }
            } else if auto.is_some() || api.is_some() {
                used_percent = [auto, api].into_iter().flatten().reduce(f64::max);
                if let Some(meter) = meter_with_limit(plan_meter.as_ref()).or_else(|| meter_with_limit(overall_meter.as_ref())) {
                    used_cents = Some(meter.used);
                    limit_cents = Some(meter.limit);
                    remaining_cents = meter.remaining;
                }
            } else {
                let mut picked_source = "";
                for (meter, source) in [
                    (overall_meter.clone(), "overall"),
                    (plan_meter.clone(), "plan"),
                    (plan_breakdown.clone(), "plan"),
                    (pooled_meter.clone(), "pooled"),
                    (od_meter.clone(), "on_demand"),
                ] {
                    if let Some(applied) = percent_from_spend_meter(meter.as_ref()) {
                        used_cents = Some(applied.0);
                        limit_cents = Some(applied.1);
                        remaining_cents = applied.2;
                        used_percent = Some(applied.3);
                        picked_source = source;
                        break;
                    }
                }
                if used_percent.is_none() {
                    used_percent = Some(0.0);
                } else if matches!(picked_source, "overall" | "pooled" | "on_demand") || (is_team_membership(Some(&membership), Some(&limit_type)) && !x_plan) {
                    billing_mode = "amount";
                } else {
                    used_cents = None;
                    limit_cents = None;
                    remaining_cents = None;
                    billing_mode = "percent";
                }
            }
        }
    }
    if used_cents.is_none() {
        if let Some(fallback) = meter_with_limit(overall_meter.as_ref()).or_else(|| meter_with_limit(pooled_meter.as_ref())) {
            used_cents = Some(fallback.used);
            limit_cents = Some(fallback.limit);
            remaining_cents = fallback.remaining;
        }
    }
    let used = clamp_percent(used_percent.unwrap_or(0.0));
    let remaining = clamp_percent(100.0 - used);
    if remaining_cents.is_none() {
        if let (Some(used_cents), Some(limit_cents)) = (used_cents, limit_cents) {
            remaining_cents = Some((limit_cents - used_cents).max(0.0));
        }
    }
    let cycle_start = as_string(get(payload, "billingCycleStart")).or_else(|| as_string(get(payload, "startOfMonth")));
    let cycle_end = as_string(get(payload, "billingCycleEnd"));
    let mut snap = json!({
        "used_percent": round1(used),
        "remaining_percent": round1(remaining),
        "auto_percent_used": auto.map(round1),
        "api_percent_used": api.map(round1),
        "total_percent_used": total.map(round1),
        "membership_type": membership,
        "billing_cycle_start": empty_to_null(cycle_start.clone()),
        "billing_cycle_end": empty_to_null(cycle_end.clone()),
        "days_remaining": days_until(cycle_end.as_deref(), clock),
        "days_elapsed": days_since(cycle_start.as_deref(), clock).map(round2),
        "estimated_usable_days": estimate_usable_days(used, remaining, days_since(cycle_start.as_deref(), clock)),
        "billing_mode": billing_mode,
        "used_cents": used_cents,
        "limit_cents": limit_cents,
        "remaining_cents": remaining_cents,
        "on_demand_used_cents": od_meter.as_ref().map(|m| m.used),
        "on_demand_limit_cents": od_meter.as_ref().map(|m| m.limit),
        "pooled_used_cents": pooled_meter.as_ref().map(|m| m.used),
        "pooled_limit_cents": pooled_meter.as_ref().map(|m| m.limit),
        "limit_type": limit_type,
        "is_unlimited": is_unlimited,
        "is_x_subscription": false,
        "usage_reset_at": Value::Null,
        "prepaid_remaining_cents": Value::Null,
        "grok_bot_percent_used": Value::Null,
        "grok_bot_remaining_percent": Value::Null,
        "grok_bot_period_start": Value::Null,
        "grok_bot_reset_at": Value::Null,
        "grok_bot_days_remaining": Value::Null,
    });
    apply_x_subscription(&mut snap, payload, clock);
    let membership = snap["membership_type"].as_str().unwrap_or("").to_string();
    let limit_type = snap["limit_type"].as_str().unwrap_or("").to_string();
    let x = snap["is_x_subscription"].as_bool().unwrap_or(false);
    let team = !x && !is_x_membership(Some(&membership)) && is_team_membership(Some(&membership), Some(&limit_type));
    let shows_amount = snap["used_cents"].is_number() && snap["limit_cents"].as_f64().unwrap_or(0.0) > 0.0 && (snap["billing_mode"] == "amount" || team);
    let shows_grok = snap["grok_bot_percent_used"].is_number();
    snap["is_team_account"] = json!(team);
    snap["shows_amount"] = json!(shows_amount);
    snap["shows_grok_bot"] = json!(shows_grok);
    snap["dashboard_url"] = json!(dashboard_url(&membership, &limit_type, x));
    snap["dashboard_button_label"] = json!(dashboard_button_label(&membership, &limit_type, x));
    snap["dashboard_menu_label"] = json!(dashboard_menu_label(&membership, &limit_type, x));
    snap["dashboard_link_label"] = json!(dashboard_link_label(&membership, x, team));
    snap
}

pub fn parse_sand_usage_status(payload: &Value, now: Option<&str>) -> Option<Value> {
    if !payload.is_object() || is_empty(payload) || is_unified_weekly(payload) {
        return None;
    }
    if opt_bool(payload, &["usesPooledEnterpriseAllowance", "uses_pooled_enterprise_allowance"]) == Some(true) {
        return None;
    }
    if opt_bool(payload, &["includedLimitZero", "included_limit_zero"]) == Some(true) {
        return None;
    }
    if opt_bool(payload, &["hasNonZeroIncludedLimit", "has_non_zero_included_limit"]) != Some(true) {
        return None;
    }
    let percent = as_f64(first_present(payload, &["usagePercent", "usage_percent"]));
    let percent = percent?;
    let used = clamp_percent(percent);
    let start = timestamp_to_iso(first_present(payload, &["currentPeriodStart", "current_period_start"]));
    let reset = timestamp_to_iso(first_present(payload, &["nextResetTimestampUtc", "next_reset_timestamp_utc"]));
    let clock = parse_now(now);
    Some(json!({
        "percent_used": round1(used),
        "remaining_percent": round1(100.0 - used),
        "period_start": start,
        "reset_at": reset,
        "days_remaining": reset.as_ref().and_then(|iso| days_until(Some(iso), clock)),
    }))
}

pub fn apply_sand_usage(snapshot: &mut Value, payload: &Value, now: Option<&str>) {
    let clock = parse_now(now);
    if is_unified_weekly(payload) {
        apply_unified_weekly(snapshot, payload, clock);
        return;
    }
    if snapshot["is_x_subscription"].as_bool().unwrap_or(false) {
        return;
    }
    let Some(parsed) = parse_sand_usage_status(payload, now) else { return };
    snapshot["grok_bot_percent_used"] = parsed["percent_used"].clone();
    snapshot["grok_bot_remaining_percent"] = parsed["remaining_percent"].clone();
    snapshot["grok_bot_period_start"] = parsed["period_start"].clone();
    snapshot["grok_bot_reset_at"] = parsed["reset_at"].clone();
    snapshot["grok_bot_days_remaining"] = parsed["days_remaining"].clone();
    snapshot["shows_grok_bot"] = json!(true);
}

pub fn parse_aggregated_usage(payload: &Value, auto_percent: Option<f64>, api_percent: Option<f64>) -> Value {
    let mut rows = Vec::new();
    if let Some(items) = get(payload, "aggregations").as_array() {
        for item in items {
            let raw_name = as_string(get(item, "modelIntent")).or_else(|| as_string(get(item, "model"))).unwrap_or_default();
            let name = display_model_name(&raw_name);
            if name.is_empty() { continue; }
            let tokens = sum_token_fields(item);
            if tokens <= 0 { continue; }
            rows.push(json!({
                "name": name,
                "tokens": tokens,
                "cents": as_f64(get(item, "totalCents")).unwrap_or(0.0),
                "tier": model_tier(&name, get(item, "tier")),
                "grok": is_grok_bot_model(Some(&name)),
                "cursor": model_tier(&name, get(item, "tier")) == 2,
            }));
        }
    }
    if rows.is_empty() {
        let total = sum_token_fields(payload).max(0);
        return json!({ "models": [], "total": total });
    }
    let mut grok = allocate(rows.iter().filter(|m| m["grok"].as_bool().unwrap_or(false)), None);
    let mut cursor = allocate(rows.iter().filter(|m| m["cursor"].as_bool().unwrap_or(false) && !m["grok"].as_bool().unwrap_or(false)), auto_percent);
    let mut other = allocate(rows.iter().filter(|m| !m["cursor"].as_bool().unwrap_or(false) && !m["grok"].as_bool().unwrap_or(false)), api_percent);
    cursor.sort_by(|a, b| cmp_usage_then_tokens(a, b));
    other.sort_by(|a, b| cmp_usage_then_tokens(a, b));
    grok.sort_by(|a, b| b["tokens"].as_i64().unwrap_or(0).cmp(&a["tokens"].as_i64().unwrap_or(0)));
    let mut models = cursor;
    models.append(&mut other);
    models.append(&mut grok);
    let mut total = models.iter().map(|m| m["tokens"].as_i64().unwrap_or(0)).sum::<i64>();
    let header = sum_token_fields(payload);
    if header > total { total = header; }
    for model in &mut models {
        if let Some(obj) = model.as_object_mut() {
            obj.remove("grok");
            obj.remove("cursor");
        }
    }
    json!({ "models": models, "total": total })
}

fn apply_unified_weekly(snapshot: &mut Value, payload: &Value, clock: f64) {
    let Some(percent) = as_f64(first_present(payload, &["usagePercent", "usage_percent"])) else { return };
    let used = clamp_percent(percent);
    snapshot["is_x_subscription"] = json!(true);
    snapshot["billing_mode"] = json!("percent");
    snapshot["used_percent"] = json!(round1(used));
    snapshot["remaining_percent"] = json!(round1(clamp_percent(100.0 - used)));
    snapshot["total_percent_used"] = snapshot["used_percent"].clone();
    snapshot["auto_percent_used"] = Value::Null;
    snapshot["api_percent_used"] = Value::Null;
    snapshot["grok_bot_percent_used"] = Value::Null;
    snapshot["grok_bot_remaining_percent"] = Value::Null;
    snapshot["grok_bot_period_start"] = Value::Null;
    snapshot["grok_bot_reset_at"] = Value::Null;
    snapshot["grok_bot_days_remaining"] = Value::Null;
    snapshot["shows_grok_bot"] = json!(false);
    let start = timestamp_to_iso(first_present(payload, &["currentPeriodStart", "current_period_start"]));
    let reset = timestamp_to_iso(first_present(payload, &["nextResetTimestampUtc", "next_reset_timestamp_utc"]));
    snapshot["usage_reset_at"] = reset.clone().map(Value::String).unwrap_or(Value::Null);
    if let Some(reset) = reset.as_deref() {
        snapshot["days_remaining"] = json!(days_until(Some(reset), clock));
    }
    if let Some(start) = start.as_deref() {
        snapshot["days_elapsed"] = json!(days_since(Some(start), clock).map(round2));
    }
    let used_percent = snapshot["used_percent"].as_f64().unwrap_or(0.0);
    let remaining = snapshot["remaining_percent"].as_f64().unwrap_or(0.0);
    snapshot["estimated_usable_days"] = json!(estimate_usable_days(used_percent, remaining, snapshot["days_elapsed"].as_f64()));
}

fn is_unified_weekly(payload: &Value) -> bool {
    opt_bool(payload, &["isUnified", "is_unified"]) == Some(true)
}

fn looks_like_x_plan(membership: Option<&str>, payload: &Value, cycle_start: Option<&str>, cycle_end: Option<&str>) -> bool {
    if is_x_membership(membership) { return true; }
    let key = membership_key(membership);
    if !matches!(key.as_str(), "ultra" | "free" | "hobby") { return false; }
    let short_cycle = cycle_span_days(cycle_start, cycle_end).is_some_and(|span| (1.0..=8.0).contains(&span));
    short_cycle || has_explicit_weekly(payload)
}

fn apply_x_subscription(snap: &mut Value, payload: &Value, clock: f64) {
    let membership = snap["membership_type"].as_str().unwrap_or("").to_string();
    let raw = as_string(get(payload, "membershipType")).or_else(|| as_string(get(payload, "plan")));
    let start = snap["billing_cycle_start"].as_str().map(str::to_string);
    let end = snap["billing_cycle_end"].as_str().map(str::to_string);
    if !looks_like_x_plan(Some(&membership), payload, start.as_deref(), end.as_deref()) && !looks_like_x_plan(raw.as_deref(), payload, start.as_deref(), end.as_deref()) {
        return;
    }
    snap["is_x_subscription"] = json!(true);
    snap["billing_mode"] = json!("percent");
    let weekly = weekly_block(payload);
    let percent = as_f64(get(&weekly, "percentUsed")).or_else(|| as_f64(get(&weekly, "usagePercent"))).or_else(|| as_f64(get(payload, "weeklyPercentUsed")));
    if let Some(weekly_used) = percent {
        let used = clamp_percent(weekly_used);
        snap["used_percent"] = json!(round1(used));
        snap["remaining_percent"] = json!(round1(clamp_percent(100.0 - used)));
        snap["total_percent_used"] = snap["used_percent"].clone();
    }
    snap["auto_percent_used"] = Value::Null;
    snap["api_percent_used"] = Value::Null;
    snap["used_cents"] = Value::Null;
    snap["limit_cents"] = Value::Null;
    snap["remaining_cents"] = Value::Null;
    let mut reset = iso_or_none(as_string(get(payload, "usageResetAt")).or_else(|| as_string(get(payload, "usage_reset_at"))))
        .or_else(|| iso_or_none(as_string(get(&weekly, "resetAt")).or_else(|| as_string(get(&weekly, "nextReset")))));
    let mut period = iso_or_none(as_string(get(payload, "usagePeriodStart")).or_else(|| as_string(get(payload, "usage_period_start"))))
        .or_else(|| iso_or_none(as_string(get(&weekly, "periodStart")).or_else(|| as_string(get(&weekly, "currentPeriodStart")))));
    let span = cycle_span_days(start.as_deref(), end.as_deref());
    if reset.is_none() && span.is_some_and(|days| (1.0..=8.0).contains(&days)) {
        reset = end.clone();
        if period.is_none() { period = start.clone(); }
    }
    snap["usage_reset_at"] = reset.clone().map(Value::String).unwrap_or(Value::Null);
    if let Some(reset) = reset.as_deref() {
        snap["days_remaining"] = json!(days_until(Some(reset), clock));
    }
    if let Some(period) = period.as_deref() {
        snap["days_elapsed"] = json!(days_since(Some(period), clock).map(round2));
    }
    if reset.is_some() || period.is_some() {
        let used = snap["used_percent"].as_f64().unwrap_or(0.0);
        let remaining = snap["remaining_percent"].as_f64().unwrap_or(0.0);
        let elapsed = days_since(period.as_deref(), clock);
        snap["estimated_usable_days"] = json!(estimate_usable_days(used, remaining, elapsed));
    }
    let individual = child(payload, "individualUsage");
    let mut prepaid = child(individual, "prepaid");
    if !prepaid.is_object() || is_empty(prepaid) { prepaid = child(individual, "additionalUsage"); }
    snap["prepaid_remaining_cents"] = as_f64(get(prepaid, "remaining")).or_else(|| as_f64(get(prepaid, "remainingCents"))).or_else(|| as_f64(get(payload, "prepaidRemainingCents"))).map(|n| json!(n)).unwrap_or(Value::Null);
}

fn weekly_block(payload: &Value) -> Value {
    let individual = child(payload, "individualUsage");
    let weekly = child(individual, "weekly");
    if weekly.is_object() && !is_empty(weekly) { return weekly.clone(); }
    let top = child(payload, "weeklyUsage");
    if top.is_object() && !is_empty(top) { top.clone() } else { Value::Null }
}

fn has_explicit_weekly(payload: &Value) -> bool {
    for key in ["usageResetAt", "usage_reset_at", "usagePeriodStart", "usage_period_start", "weeklyPercentUsed"] {
        if has(payload, key) && (as_string(get(payload, key)).is_some_and(|s| !s.is_empty()) || as_f64(get(payload, key)).is_some()) {
            return true;
        }
    }
    let cadence = as_string(get(payload, "usageCadence")).or_else(|| as_string(get(payload, "billingInterval"))).or_else(|| as_string(get(payload, "resetInterval"))).unwrap_or_default();
    if cadence.eq_ignore_ascii_case("week") || cadence.eq_ignore_ascii_case("weekly") { return true; }
    let weekly = weekly_block(payload);
    weekly.is_object() && !is_empty(&weekly)
}

fn cycle_span_days(start: Option<&str>, end: Option<&str>) -> Option<f64> {
    Some((parse_iso(end)? - parse_iso(start)?) / 86_400.0)
}

fn iso_or_none(value: Option<String>) -> Option<String> {
    value.filter(|text| !text.is_empty())
}

fn empty_to_null(value: Option<String>) -> Value {
    match value {
        Some(text) if !text.is_empty() => Value::String(text),
        _ => Value::Null,
    }
}

fn on_demand_enabled(block: &Value) -> bool {
    if !block.is_object() || is_empty(block) { return false; }
    if has(block, "enabled") { return as_bool(get(block, "enabled")); }
    spend_meter(block, false).is_some()
}

fn spend_meter(block: &Value, allow_breakdown: bool) -> Option<Meter> {
    if !block.is_object() || is_empty(block) { return None; }
    let mut used = as_f64(get(block, "used"));
    let mut limit = as_f64(get(block, "limit"));
    let remaining = as_f64(get(block, "remaining"));
    if limit.unwrap_or(0.0) <= 0.0 && allow_breakdown {
        limit = as_f64(get(child(block, "breakdown"), "total"));
    }
    if used.is_none() {
        if let (Some(remaining), Some(limit)) = (remaining, limit) {
            used = Some((limit - remaining).max(0.0));
        }
    }
    Some(Meter { used: used?, limit: limit?, remaining })
}

fn meter_with_limit(meter: Option<&Meter>) -> Option<Meter> {
    meter.filter(|m| m.limit > 0.0).cloned()
}

fn percent_from_spend_meter(meter: Option<&Meter>) -> Option<(f64, f64, Option<f64>, f64)> {
    let meter = meter_with_limit(meter)?;
    Some((meter.used, meter.limit, meter.remaining, clamp_percent(meter.used / meter.limit * 100.0)))
}

fn percent_from_display(message: Option<&str>) -> Option<f64> {
    let text = message?;
    let index = text.find('%')?;
    if index == 0 { return None; }
    let before = &text[..index];
    let start = before.rfind(|ch: char| !(ch.is_ascii_digit() || ch == '.')).map(|i| i + 1).unwrap_or(0);
    let raw = before[start..].trim();
    if raw.is_empty() { return None; }
    raw.parse::<f64>().ok().map(clamp_percent)
}

fn sum_token_fields(item: &Value) -> i64 {
    let mut total = 0;
    let mut found = false;
    for key in TOKEN_FIELDS {
        if let Some(n) = as_int(get(item, key)) {
            found = true;
            total += n.max(0);
        }
    }
    if found { return total; }
    as_int(get(item, "totalTokens")).unwrap_or(0).max(0)
}

fn display_model_name(raw: &str) -> String {
    let name = raw.trim();
    if name.is_empty() { String::new() } else if name == "default" { "auto".to_string() } else { name.to_string() }
}

fn model_tier(name: &str, tier: &Value) -> i64 {
    as_int(tier).unwrap_or_else(|| if is_first_party_model(Some(name)) { 2 } else { 1 })
}

fn allocate<'a>(models: impl Iterator<Item = &'a Value>, category_percent: Option<f64>) -> Vec<Value> {
    let models: Vec<&Value> = models.collect();
    if models.is_empty() { return Vec::new(); }
    let cents_sum: f64 = models.iter().map(|m| m["cents"].as_f64().unwrap_or(0.0)).sum();
    let tokens_sum: i64 = models.iter().map(|m| m["tokens"].as_i64().unwrap_or(0)).sum();
    models.into_iter().map(|model| {
        let mut copy = model.clone();
        let share = if cents_sum > 1e-6 {
            model["cents"].as_f64().unwrap_or(0.0) / cents_sum
        } else if tokens_sum > 0 {
            model["tokens"].as_f64().unwrap_or(0.0) / tokens_sum as f64
        } else {
            0.0
        };
        copy["usage_percent"] = category_percent.map(|pct| json!(round1(share * pct))).unwrap_or(Value::Null);
        copy
    }).collect()
}

fn cmp_usage_then_tokens(a: &Value, b: &Value) -> std::cmp::Ordering {
    let usage = b["usage_percent"].as_f64().unwrap_or(0.0).total_cmp(&a["usage_percent"].as_f64().unwrap_or(0.0));
    usage.then(b["tokens"].as_i64().unwrap_or(0).cmp(&a["tokens"].as_i64().unwrap_or(0)))
}

pub fn timestamp_to_iso(value: &Value) -> Option<String> {
    if let Some(raw) = as_string(value).map(|text| text.trim().to_string()) {
        if !raw.is_empty() && (raw.contains('T') || raw.ends_with('Z')) {
            return parse_iso(Some(&raw)).map(|_| raw);
        }
    }
    let num = as_f64(value)?;
    let seconds = if num.abs() > 10_000_000_000.0 { num / 1000.0 } else { num };
    let millis = (seconds * 1000.0).round();
    if !(0.0..253_402_300_800_000.0).contains(&millis) { return None; }
    let whole = millis as i64;
    let secs = whole.div_euclid(1000);
    let ms = whole.rem_euclid(1000) as u32;
    let stamp = UNIX_EPOCH + std::time::Duration::from_secs(secs as u64);
    format_utc(stamp, ms)
}

fn format_utc(stamp: SystemTime, millis: u32) -> Option<String> {
    let seconds = stamp.duration_since(UNIX_EPOCH).ok()?.as_secs();
    let days = seconds.div_euclid(86_400);
    let time = seconds.rem_euclid(86_400);
    let (year, month, day) = civil_from_days(days as i64);
    let hour = time / 3600;
    let minute = (time % 3600) / 60;
    let second = time % 60;
    if millis == 0 {
        Some(format!("{year:04}-{month:02}-{day:02}T{hour:02}:{minute:02}:{second:02}Z"))
    } else {
        Some(format!("{year:04}-{month:02}-{day:02}T{hour:02}:{minute:02}:{second:02}.{millis:03}Z"))
    }
}

fn civil_from_days(days: i64) -> (i32, u32, u32) {
    let z = days + 719_468;
    let era = if z >= 0 { z } else { z - 146_096 } / 146_097;
    let doe = (z - era * 146_097) as u64;
    let yoe = (doe - doe / 1460 + doe / 36524 - doe / 146_096) / 365;
    let y = yoe as i64 + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = doy - (153 * mp + 2) / 5 + 1;
    let m = if mp < 10 { mp + 3 } else { mp - 9 };
    let y = if m <= 2 { y + 1 } else { y };
    (y as i32, m as u32, d as u32)
}

pub(crate) fn parse_iso(iso: Option<&str>) -> Option<f64> {
    let text = iso?.trim();
    if text.is_empty() { return None; }
    let (date, rest) = text.split_once('T')?;
    let mut ymd = date.split('-');
    let year: i32 = ymd.next()?.parse().ok()?;
    let month: u32 = ymd.next()?.parse().ok()?;
    let day: u32 = ymd.next()?.parse().ok()?;
    let rest = rest.strip_suffix('Z').unwrap_or(rest);
    let (time, offset) = if let Some(index) = rest.rfind(['+', '-']) {
        if index > 0 { (&rest[..index], Some(&rest[index..])) } else { (rest, None) }
    } else {
        (rest, None)
    };
    let mut hms = time.split(':');
    let hour: u32 = hms.next()?.parse().ok()?;
    let minute: u32 = hms.next()?.parse().ok()?;
    let seconds = hms.next().unwrap_or("0");
    let (whole, frac) = seconds.split_once('.').unwrap_or((seconds, ""));
    let second: u32 = whole.parse().ok()?;
    let millis = if frac.is_empty() { 0 } else { format!("{frac:0<3}")[..3].parse::<u32>().unwrap_or(0) };
    let days = days_from_civil(year, month, day)?;
    let mut stamp = days as f64 * 86_400.0 + (hour * 3600 + minute * 60 + second) as f64 + millis as f64 / 1000.0;
    if let Some(offset) = offset {
        let sign = if offset.starts_with('-') { -1.0 } else { 1.0 };
        let body = &offset[1..];
        let mut parts = body.split(':');
        let oh: f64 = parts.next()?.parse().ok()?;
        let om: f64 = parts.next().unwrap_or("0").parse().ok()?;
        stamp -= sign * (oh * 3600.0 + om * 60.0);
    }
    Some(stamp)
}

fn days_from_civil(year: i32, month: u32, day: u32) -> Option<i64> {
    if !(1..=12).contains(&month) || !(1..=31).contains(&day) { return None; }
    let y = if month <= 2 { year as i64 - 1 } else { year as i64 };
    let era = if y >= 0 { y } else { y - 399 } / 400;
    let yoe = (y - era * 400) as u64;
    let mp = if month > 2 { month as u64 - 3 } else { month as u64 + 9 };
    let doy = (153 * mp + 2) / 5 + day as u64 - 1;
    let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    Some(era * 146_097 + doe as i64 - 719_468)
}

pub fn days_until(iso: Option<&str>, now: f64) -> Option<i64> {
    let end = parse_iso(iso)?;
    Some(((end - now) / 86_400.0).floor().max(0.0) as i64)
}

fn days_since(iso: Option<&str>, now: f64) -> Option<f64> {
    let start = parse_iso(iso)?;
    let hours = (now - start) / 3600.0;
    Some(if hours < 0.0 { 0.0 } else { hours / 24.0 })
}

pub fn estimate_usable_days(used_percent: f64, remaining_percent: f64, days_elapsed: Option<f64>) -> Option<f64> {
    let days_elapsed = days_elapsed?;
    if remaining_percent <= 0.0 { return Some(0.0); }
    if days_elapsed < 0.04 || used_percent < 0.2 { return None; }
    let burn = used_percent / days_elapsed;
    if burn <= 1e-6 { return None; }
    Some(round1(remaining_percent / burn))
}

fn parse_now(now: Option<&str>) -> f64 {
    if let Some(now) = now {
        if let Some(parsed) = parse_iso(Some(now)) { return parsed; }
    }
    SystemTime::now().duration_since(UNIX_EPOCH).map(|d| d.as_secs_f64()).unwrap_or(0.0)
}
