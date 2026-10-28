use crate::jsonutil::{as_bool, as_f64, as_int, as_string, get, has};
use crate::usage::{format_usd_cents, usage_category};
use serde_json::{json, Map, Value};
use std::collections::BTreeMap;

const MS_HOUR: i64 = 3_600_000;
const MS_DAY: i64 = 86_400_000;
const TZ_OFFSET_MS: i64 = 8 * MS_HOUR;
const TZ_LABEL: &str = "本地时间";

pub fn classify_kind(kind: Option<&str>, costs: Option<&str>, chargeable: bool) -> &'static str {
    let blob = format!("{} {}", kind.unwrap_or(""), costs.unwrap_or("")).trim().to_ascii_lowercase();
    if blob.contains("free") { return "free"; }
    if blob.contains("included") { return "included"; }
    if blob.contains("usage_based") || blob.contains("usage-based") || blob.contains("ondemand") || blob.contains("on_demand") || blob.contains("on-demand") {
        return "on_demand";
    }
    if chargeable { "on_demand" } else { "included" }
}

pub fn kind_label(kind: Option<&str>) -> &'static str {
    match kind.unwrap_or("").trim().to_ascii_lowercase().as_str() {
        "included" => "套餐内",
        "free" => "免费",
        "on_demand" => "按需",
        _ => "其他",
    }
}

pub fn category_label(category: Option<&str>) -> &'static str {
    match category.unwrap_or("").trim().to_ascii_lowercase().as_str() {
        "first_party" => "First-party",
        "grok_bot" => "Grok Bot",
        _ => "API",
    }
}

pub fn classify_category(model: Option<&str>) -> &'static str {
    usage_category(model)
}

pub fn parse_page(payload: &Value) -> Value {
    let rows = if get(payload, "usageEventsDisplay").as_array().is_some_and(|rows| !rows.is_empty()) {
        get(payload, "usageEventsDisplay").as_array()
    } else {
        get(payload, "usageEvents").as_array()
    };
    let events: Vec<Value> = rows.into_iter().flatten().filter_map(parse_event).collect();
    let mut total = as_int(get(payload, "totalUsageEventsCount"));
    if total.is_none() {
        let paging = get(payload, "pagination");
        total = as_int(get(paging, "numEvents")).or_else(|| as_int(get(paging, "totalNumEvents"))).or_else(|| as_int(get(paging, "total")));
    }
    let total = total.filter(|n| *n >= events.len() as i64).unwrap_or(events.len() as i64);
    json!({ "total_count": total, "events": events })
}

pub fn parse_event(item: &Value) -> Option<Value> {
    if !item.is_object() { return None; }
    let ts = as_int(get(item, "timestamp")).or_else(|| as_int(get(item, "timestampMs"))).or_else(|| as_int(get(item, "createdAt")))?;
    if ts <= 0 { return None; }
    let token_usage = if get(item, "tokenUsage").is_object() { get(item, "tokenUsage") } else { &Value::Null };
    let model = display_model(&as_string(get(item, "model")).or_else(|| as_string(get(item, "modelIntent"))).unwrap_or_default());
    let kind_raw = as_string(get(item, "kind")).or_else(|| as_string(get(item, "type"))).unwrap_or_default();
    let costs_raw = as_string(get(item, "usageBasedCosts")).or_else(|| as_string(get(item, "cost"))).unwrap_or_default();
    let chargeable = as_bool(get(item, "isChargeable"));
    let kind = classify_kind(Some(&kind_raw), Some(&costs_raw), chargeable);
    let input = token_int(token_usage, item, "inputTokens");
    let output = token_int(token_usage, item, "outputTokens");
    let cache_write = token_int(token_usage, item, "cacheWriteTokens");
    let cache_read = token_int(token_usage, item, "cacheReadTokens");
    let mut tokens = sum_tokens(token_usage);
    if tokens <= 0 { tokens = sum_tokens(item); }
    if tokens <= 0 { tokens = input + output + cache_write + cache_read; }
    let charged = as_f64(get(item, "chargedCents")).or_else(|| parse_money_cents(get(item, "usageBasedCosts")));
    let total_cents = as_f64(get(token_usage, "totalCents")).or_else(|| as_f64(get(item, "totalCents")));
    let email = as_string(get(item, "email")).or_else(|| as_string(get(item, "userEmail"))).or_else(|| as_string(get(item, "user")))
        .or_else(|| if get(item, "user").is_object() { as_string(get(get(item, "user"), "email")) } else { None })
        .unwrap_or_default().trim().to_string();
    let owning = as_string(get(item, "owningUser")).or_else(|| as_string(get(item, "userId"))).unwrap_or_default().trim().to_string();
    let given = as_string(get(item, "id")).or_else(|| as_string(get(item, "eventId"))).unwrap_or_default().trim().to_string();
    let id = if given.is_empty() { format!("{ts}|{owning}|{model}|{input}|{output}|{cache_write}|{cache_read}|{kind_raw}") } else { given };
    Some(json!({
        "id": id,
        "timestamp_ms": ts,
        "model": model,
        "kind": kind,
        "user_email": email,
        "owning_user": owning,
        "tokens": tokens.max(0),
        "input_tokens": input,
        "output_tokens": output,
        "cache_write_tokens": cache_write,
        "cache_read_tokens": cache_read,
        "charged_cents": charged,
        "total_cents": total_cents,
        "is_headless": as_bool(get(item, "isHeadless")) || as_bool(get(item, "isCloudAgent")),
        "is_chargeable": chargeable,
    }))
}

pub fn parse_money_cents(value: &Value) -> Option<f64> {
    if as_string(value).is_none() {
        if let Some(n) = as_f64(value) { return Some(n); }
    }
    let text = as_string(value).unwrap_or_default().trim().to_string();
    if text.is_empty() { return None; }
    let lower = text.to_ascii_lowercase();
    if matches!(lower.as_str(), "included" | "free" | "n/a" | "—" | "-" | "none") { return None; }
    if !lower.contains("us$") && !text.contains('$') { return None; }
    let cleaned = text.replace("US$", "").replace("us$", "").replace('$', "").replace(',', "").replace("Included", "").replace("Free", "");
    cleaned.trim().parse::<f64>().ok().map(|dollars| dollars * 100.0)
}

pub fn merge(existing: &[Value], incoming: &[Value]) -> Vec<Value> {
    let mut by_id: Vec<(String, Value)> = Vec::new();
    for ev in existing.iter().chain(incoming) {
        let id = ev["id"].as_str().unwrap_or("");
        if id.is_empty() { continue; }
        if let Some(slot) = by_id.iter_mut().find(|(key, _)| key == id) { slot.1 = ev.clone(); }
        else { by_id.push((id.to_string(), ev.clone())); }
    }
    by_id.sort_by(|a, b| b.1["timestamp_ms"].as_i64().unwrap_or(0).cmp(&a.1["timestamp_ms"].as_i64().unwrap_or(0)));
    by_id.into_iter().map(|(_, ev)| ev).collect()
}

pub fn prune(events: &[Value], min_timestamp: i64) -> Vec<Value> {
    let mut kept: Vec<Value> = events.iter().filter(|ev| ev["timestamp_ms"].as_i64().unwrap_or(0) >= min_timestamp).cloned().collect();
    kept.sort_by(|a, b| b["timestamp_ms"].as_i64().unwrap_or(0).cmp(&a["timestamp_ms"].as_i64().unwrap_or(0)));
    kept
}

pub fn cost_cents(event: &Value) -> f64 {
    event["charged_cents"].as_f64().or_else(|| event["total_cents"].as_f64()).unwrap_or(0.0).max(0.0)
}

pub fn format_cost(event: &Value) -> String {
    let cents = cost_cents(event);
    match event["kind"].as_str().unwrap_or("") {
        "free" => "免费".to_string(),
        "included" if cents > 0.0 => format!("{} 套餐内", format_usd_cents(Some(cents))),
        "included" => "套餐内".to_string(),
        _ if cents > 0.0 => format_usd_cents(Some(cents)),
        _ => "—".to_string(),
    }
}

fn token_int(token_usage: &Value, item: &Value, key: &str) -> i64 {
    as_int(get(token_usage, key)).or_else(|| as_int(get(item, key))).unwrap_or(0).max(0)
}

fn sum_tokens(item: &Value) -> i64 {
    const KEYS: [&str; 8] = ["inputTokens", "outputTokens", "cacheWriteTokens", "cacheReadTokens", "totalInputTokens", "totalOutputTokens", "totalCacheWriteTokens", "totalCacheReadTokens"];
    let mut total = 0;
    let mut found = false;
    for key in KEYS {
        if has(item, key) {
            if let Some(n) = as_int(get(item, key)) { found = true; total += n.max(0); }
        }
    }
    if found { return total; }
    as_int(get(item, "totalTokens")).unwrap_or(0).max(0)
}

pub fn default_monthly_plan_usd(membership: Option<&str>) -> f64 {
    let mut key = membership.unwrap_or("").trim().to_ascii_lowercase().replace(' ', "");
    if let Some(stripped) = key.strip_suffix("套餐") { key = stripped.to_string(); }
    match key.as_str() {
        "pro" => 20.0,
        "pro+" | "pro_plus" | "proplus" => 60.0,
        "plus" => 8.0,
        "premium" => 30.0,
        "super" => 100.0,
        "ultra" => 200.0,
        _ => 0.0,
    }
}

pub fn format_cny(yuan: Option<f64>) -> String {
    let Some(yuan) = yuan else { return "—".to_string(); };
    format!("¥{:.2}", yuan.max(0.0))
}

pub fn format_discount(cny: f64, cents: f64, rate: f64, kind: Option<&str>) -> String {
    if kind.unwrap_or("").trim().eq_ignore_ascii_case("free") { return "—".to_string(); }
    if ![cny, cents, rate].iter().all(|n| n.is_finite()) { return "—".to_string(); }
    let list = cents / 100.0 * rate;
    if list <= 1e-9 || cny <= 0.0 { return "—".to_string(); }
    format!("{:.2}折", (cny / list * 10.0).clamp(0.0, 99.99))
}

pub fn sanitize_channel(raw: Option<&str>) -> &'static str {
    let key = raw.unwrap_or("").trim().to_ascii_lowercase().replace(['-', ' '], "");
    match key.as_str() {
        "self_pay" | "self" | "selfpay" | "自费" => "self_pay",
        "third_party" | "third" | "thirdparty" | "第三方" => "third_party",
        _ => "",
    }
}

pub fn channel_label(channel: Option<&str>) -> &'static str {
    match sanitize_channel(channel) { "self_pay" => "自费", "third_party" => "第三方", _ => "未标" }
}

pub fn build_report(events: &[Value], filter: &Value, spend: Option<&Value>, allocation: Option<&Value>) -> Value {
    let kind = text(filter, "kind").to_ascii_lowercase();
    let category = text(filter, "category").to_ascii_lowercase();
    let model = text(filter, "model");
    let owning = text(filter, "owning_user");
    let (start_date, end_date, _) = normalize_range(filter["start_date"].as_str(), filter["end_date"].as_str());
    let start_ms = date_start_ms(&start_date);
    let end_ms = date_end_ms(&end_date);
    let mut selected = Vec::new();
    for (i, ev) in events.iter().enumerate() {
        if !kind.is_empty() && ev["kind"].as_str() != Some(kind.as_str()) { continue; }
        if !category.is_empty() && classify_category(ev["model"].as_str()) != category { continue; }
        if !model.is_empty() && ev["model"].as_str() != Some(model) { continue; }
        if let Some(headless) = filter["headless"].as_bool() {
            if ev["is_headless"].as_bool().unwrap_or(false) != headless { continue; }
        }
        if !owning.is_empty() && ev["owning_user"].as_str() != Some(owning) { continue; }
        let ts = ev["timestamp_ms"].as_i64().unwrap_or(0);
        if start_ms.is_some_and(|s| ts < s) || end_ms.is_some_and(|e| ts >= e) { continue; }
        let mut copy = ev.clone();
        if copy["id"].as_str().unwrap_or("").is_empty() { copy["id"] = json!(format!("#{i}")); }
        selected.push(copy);
    }
    selected.sort_by(|a, b| b["timestamp_ms"].as_i64().unwrap_or(0).cmp(&a["timestamp_ms"].as_i64().unwrap_or(0)));
    let cny = cny_by_id(events, spend, allocation);
    for (i, ev) in selected.iter_mut().enumerate() {
        let key = ev["id"].as_str().filter(|s| !s.is_empty()).unwrap_or("");
        let fallback = format!("#{i}");
        ev["allocated_cny"] = json!(cny.by_id.get(if key.is_empty() { fallback.as_str() } else { key }).copied().unwrap_or(0.0));
    }
    let mut daily: BTreeMap<String, (i64, f64, i64, f64)> = BTreeMap::new();
    let mut models: Vec<(String, i64, f64, i64, i64, f64)> = Vec::new();
    let (mut included, mut free, mut on_demand, mut other, mut headless) = (0, 0, 0, 0, 0);
    let (mut first_party, mut api, mut grok_bot) = (0, 0, 0);
    let (mut total_tokens, mut total_cents, mut total_cny, mut has_cost) = (0, 0.0, 0.0, false);
    for ev in &selected {
        let cents = cost_cents(ev);
        let tokens = ev["tokens"].as_i64().unwrap_or(0);
        let allocated = ev["allocated_cny"].as_f64().unwrap_or(0.0);
        total_tokens += tokens;
        total_cents += cents;
        total_cny += allocated;
        has_cost |= cents > 0.0;
        match ev["kind"].as_str().unwrap_or("") {
            "included" => included += 1,
            "free" => free += 1,
            "on_demand" => on_demand += 1,
            _ => other += 1,
        }
        match classify_category(ev["model"].as_str()) {
            "grok_bot" => grok_bot += 1,
            "first_party" => first_party += 1,
            _ => api += 1,
        }
        if ev["is_headless"].as_bool().unwrap_or(false) { headless += 1; }
        let day = event_date(ev["timestamp_ms"].as_i64().unwrap_or(0));
        let row = daily.entry(day).or_insert((0, 0.0, 0, 0.0));
        row.0 += tokens; row.1 += cents; row.2 += 1; row.3 += allocated;
        let name = ev["model"].as_str().filter(|s| !s.is_empty()).unwrap_or("—").to_string();
        let headless_n = i64::from(ev["is_headless"].as_bool().unwrap_or(false));
        if let Some(row) = models.iter_mut().find(|row| row.0 == name) { row.1 += tokens; row.2 += cents; row.3 += 1; row.4 += headless_n; row.5 += allocated; }
        else { models.push((name, tokens, cents, 1, headless_n, allocated)); }
    }
    models.sort_by(|a, b| b.1.cmp(&a.1).then(b.2.total_cmp(&a.2)).then(b.3.cmp(&a.3)));
    json!({
        "event_count": selected.len(),
        "total_tokens": total_tokens,
        "total_cents": total_cents,
        "has_cost": has_cost,
        "included_count": included,
        "free_count": free,
        "on_demand_count": on_demand,
        "other_count": other,
        "headless_count": headless,
        "first_party_count": first_party,
        "api_count": api,
        "grok_bot_count": grok_bot,
        "daily": daily.into_iter().map(|(date, (tokens, cents, count, cny))| if spend.is_some() { json!({"date": date, "tokens": tokens, "cents": cents, "count": count, "cny": cny}) } else { json!({"date": date, "tokens": tokens, "cents": cents, "count": count}) }).collect::<Vec<_>>(),
        "models": models.into_iter().map(|(name, tokens, cents, count, headless, cny)| if spend.is_some() { json!({"name": name, "tokens": tokens, "cents": cents, "count": count, "headless_count": headless, "cny": cny}) } else { json!({"name": name, "tokens": tokens, "cents": cents, "count": count, "headless_count": headless}) }).collect::<Vec<_>>(),
        "events": selected,
        "total_cny": total_cny,
        "plan_cny": cny.plan,
        "on_demand_cny": cny.on_demand,
        "usd_cny_rate": cny.rate,
        "monthly_plan_usd": cny.monthly,
        "actual_cny": cny.actual,
        "uses_actual_cny": cny.uses_actual,
        "window_plan_cny": cny.window_plan,
    })
}

struct CnySplit { by_id: BTreeMap<String, f64>, plan: f64, on_demand: f64, monthly: f64, rate: f64, actual: f64, uses_actual: bool, window_plan: f64 }

fn cny_by_id(events: &[Value], spend: Option<&Value>, allocation: Option<&Value>) -> CnySplit {
    let Some(spend) = spend else { return CnySplit { by_id: BTreeMap::new(), plan: 0.0, on_demand: 0.0, monthly: 0.0, rate: 0.0, actual: 0.0, uses_actual: false, window_plan: 0.0 }; };
    let rate = clamp_rate(spend["usd_cny_rate"].as_f64().unwrap_or(7.5));
    let monthly = resolve_monthly(spend["monthly_plan_usd"].as_f64().unwrap_or(0.0), spend["membership_type"].as_str());
    let actual = clamp_actual(spend["actual_cny"].as_f64().unwrap_or(0.0));
    let uses_actual = actual > 0.0;
    let plan = if uses_actual { actual } else { monthly * rate };
    let (start_ms, end_ms, window_plan) = allocation.filter(|row| row.is_object()).map(|row| {
        let (start, end, _) = compare_window(row, row["now_ms"].as_i64());
        let mut window = if plan > 0.0 { plan / 30.0 } else { 0.0 } * window_days(start, end);
        if uses_actual { window = window.min(actual); }
        (Some(start), Some(end), window)
    }).unwrap_or((None, None, plan));
    let in_window = |ev: &Value| start_ms.is_none_or(|s| ev["timestamp_ms"].as_i64().unwrap_or(0) >= s) && end_ms.is_none_or(|e| ev["timestamp_ms"].as_i64().unwrap_or(0) <= e);
    let pool: Vec<&Value> = events.iter().filter(|ev| in_window(ev) && cost_pool(ev["kind"].as_str(), uses_actual)).collect();
    let included_sum: f64 = pool.iter().map(|ev| cost_cents(ev)).sum();
    let included_count = pool.len();
    let mut by_id = BTreeMap::new();
    let mut on_demand = 0.0;
    for (i, ev) in events.iter().enumerate() {
        let amount = if in_window(ev) { allocate_event(ev, included_sum, included_count, window_plan, rate, uses_actual) } else { 0.0 };
        let key = ev["id"].as_str().filter(|s| !s.is_empty()).map(str::to_string).unwrap_or_else(|| format!("#{i}"));
        by_id.insert(key, amount);
        if ev["kind"].as_str() == Some("on_demand") { on_demand += amount; }
    }
    CnySplit { by_id, plan, on_demand, monthly, rate, actual, uses_actual, window_plan }
}

fn allocate_event(ev: &Value, included_sum: f64, included_count: usize, plan: f64, rate: f64, uses_actual: bool) -> f64 {
    match ev["kind"].as_str().unwrap_or("") {
        "free" => 0.0,
        "on_demand" if !uses_actual => cost_cents(ev) / 100.0 * rate,
        _ => {
            let cents = cost_cents(ev);
            if included_sum > 1e-9 { plan * (cents / included_sum) }
            else if included_count > 0 && plan > 0.0 { plan / included_count as f64 }
            else { 0.0 }
        }
    }
}

fn cost_pool(kind: Option<&str>, uses_actual: bool) -> bool {
    let key = kind.unwrap_or("").trim().to_ascii_lowercase();
    key != "free" && (uses_actual || (key != "on_demand" && key != "free"))
}

fn clamp_rate(rate: f64) -> f64 { if rate.is_finite() { rate.clamp(0.01, 100.0) } else { 7.5 } }
fn clamp_monthly(usd: f64) -> f64 { if usd.is_finite() && usd > 0.0 { usd.min(10_000.0) } else { 0.0 } }
fn clamp_actual(amount: f64) -> f64 { if amount.is_finite() && amount > 0.0 { amount.min(1_000_000.0) } else { 0.0 } }
fn resolve_monthly(stored: f64, membership: Option<&str>) -> f64 { let stored = clamp_monthly(stored); if stored > 0.0 { stored } else { default_monthly_plan_usd(membership) } }

pub fn build_account_compare(items: &[Value], now_ms: Option<i64>) -> Value {
    let rows: Vec<Value> = items.iter().map(|item| compare_row(item, now_ms)).collect();
    let mut groups = Vec::new();
    for channel in ["self_pay", "third_party", ""] {
        let bucket: Vec<&Value> = rows.iter().filter(|row| row["channel"].as_str() == Some(channel)).collect();
        if bucket.is_empty() { continue; }
        groups.push(json!({
            "channel": channel,
            "daily_holding_cny": bucket.iter().map(|row| row["daily_holding_cny"].as_f64().unwrap_or(0.0)).sum::<f64>(),
            "total_cny": bucket.iter().map(|row| row["total_cny"].as_f64().unwrap_or(0.0)).sum::<f64>(),
            "event_count": bucket.iter().map(|row| row["event_count"].as_i64().unwrap_or(0)).sum::<i64>(),
            "total_tokens": bucket.iter().map(|row| row["total_tokens"].as_i64().unwrap_or(0)).sum::<i64>(),
        }));
    }
    json!({"rows": rows, "groups": groups, "holding_days": 30.0})
}

fn compare_row(item: &Value, now_ms: Option<i64>) -> Value {
    let (start, end, source) = compare_window(item, now_ms);
    let days = window_days(start, end);
    let events: Vec<&Value> = item["events"].as_array().into_iter().flatten().filter(|ev| timestamp_ms(ev).is_some_and(|ts| ts >= start && ts <= end)).collect();
    let spend = &item["spend"];
    let rate = clamp_rate(spend["usd_cny_rate"].as_f64().unwrap_or(7.5));
    let monthly = resolve_monthly(spend["monthly_plan_usd"].as_f64().unwrap_or(0.0), spend["membership_type"].as_str());
    let actual = clamp_actual(spend["actual_cny"].as_f64().unwrap_or(0.0));
    let uses_actual = actual > 0.0;
    let plan = if uses_actual { actual } else { monthly * rate };
    let daily = if plan > 0.0 { plan / 30.0 } else { 0.0 };
    let mut window_plan = daily * days;
    if uses_actual { window_plan = window_plan.min(actual); }
    let pool: Vec<&&Value> = events.iter().filter(|ev| cost_pool(ev["kind"].as_str(), uses_actual)).collect();
    let included_sum: f64 = pool.iter().map(|ev| cost_cents(ev)).sum();
    let mut cats = BTreeMap::from([("first_party".to_string(), (0, 0, 0.0)), ("api".to_string(), (0, 0, 0.0)), ("grok_bot".to_string(), (0, 0, 0.0))]);
    let (mut on_demand, mut total_cny, mut total_tokens) = (0.0, 0.0, 0);
    for ev in &events {
        let amount = allocate_event(ev, included_sum, pool.len(), window_plan, rate, uses_actual);
        total_cny += amount;
        total_tokens += ev["tokens"].as_i64().unwrap_or(0);
        if ev["kind"].as_str() == Some("on_demand") { on_demand += amount; }
        let bucket = classify_category(ev["model"].as_str());
        let row = cats.entry(bucket.to_string()).or_insert((0, 0, 0.0));
        row.0 += 1; row.1 += ev["tokens"].as_i64().unwrap_or(0); row.2 += amount;
    }
    let cat = |name: &str| { let row = cats.get(name).copied().unwrap_or((0, 0, 0.0)); json!({"count": row.0, "tokens": row.1, "cny": row.2}) };
    let denom = if total_tokens > 0 { total_tokens as f64 / 1_000_000.0 } else { 0.0 };
    json!({
        "account_id": item["account_id"].as_str().unwrap_or(""),
        "channel": sanitize_channel(item["channel"].as_str()),
        "window_source": source,
        "window_days": days,
        "plan_cny": plan,
        "daily_holding_cny": daily,
        "window_plan_cny": window_plan,
        "on_demand_cny": on_demand,
        "total_cny": total_cny,
        "event_count": events.len(),
        "total_tokens": total_tokens,
        "cny_per_million": if denom > 1e-12 { total_cny.max(0.0) / denom } else { 0.0 },
        "cny_per_request": if events.is_empty() { 0.0 } else { total_cny.max(0.0) / events.len() as f64 },
        "first_party": cat("first_party"),
        "api": cat("api"),
        "grok_bot": cat("grok_bot"),
    })
}

fn compare_window(item: &Value, now_ms: Option<i64>) -> (i64, i64, &'static str) {
    let now = now_ms.unwrap_or(0);
    let kind = item["account_kind"].as_str().unwrap_or("").trim().to_ascii_lowercase().replace('-', "_");
    if matches!(kind.as_str(), "temporary" | "temp" | "short") {
        if let Some(start) = iso_to_ms(item["temp_start_at"].as_str()) {
            let end = temp_end_ms(item["temp_start_at"].as_str(), item["temp_valid_days"].as_i64().unwrap_or(0), item["temp_valid_hours"].as_i64().unwrap_or(0)).unwrap_or(now);
            return (start, end.min(now), "validity");
        }
    }
    if let Some(start) = iso_to_ms(item["billing_cycle_start"].as_str()) {
        let end = iso_to_ms(item["billing_cycle_end"].as_str()).unwrap_or(now);
        return (start, end.min(now), "cycle");
    }
    (now - 30 * MS_DAY, now, "fallback")
}

pub fn temp_end_iso(start: Option<&str>, days: i64, hours: i64) -> Option<String> {
    let ms = temp_end_ms(start, days, hours)?;
    let secs = ms / 1000;
    let (year, month, day) = civil_from_days(secs.div_euclid(86_400));
    let clock = secs.rem_euclid(86_400);
    let millis = ms.rem_euclid(1000);
    Some(if millis == 0 {
        format!("{:04}-{:02}-{:02}T{:02}:{:02}:{:02}.000Z", year, month, day, clock / 3600, clock % 3600 / 60, clock % 60)
    } else {
        format!("{:04}-{:02}-{:02}T{:02}:{:02}:{:02}.{:03}Z", year, month, day, clock / 3600, clock % 3600 / 60, clock % 60, millis)
    })
}

fn temp_end_ms(start: Option<&str>, days: i64, hours: i64) -> Option<i64> {
    let start = iso_to_ms(start)?;
    let days = days.clamp(0, 999);
    let hours = hours.clamp(0, 23);
    if days == 0 && hours == 0 { return None; }
    Some(start + days * MS_DAY + hours * MS_HOUR)
}

fn window_days(start: i64, end: i64) -> f64 { ((end - start).max(0) as f64 / MS_DAY as f64).max(1.0 / 24.0) }

pub fn event_timestamp_ms(text: &str) -> Option<i64> { iso_to_ms(Some(text)) }

fn timestamp_ms(ev: &Value) -> Option<i64> {
    ev["timestamp_ms"].as_i64().or_else(|| iso_to_ms(ev["timestamp"].as_str())).or_else(|| ev["timestamp"].as_i64())
}

fn iso_to_ms(value: Option<&str>) -> Option<i64> {
    let text = value?.trim();
    if text.is_empty() { return None; }
    let (date, time) = text.split_once('T')?;
    let mut date_parts = date.split('-');
    let (year, month, day): (i32, u32, u32) = (date_parts.next()?.parse().ok()?, date_parts.next()?.parse().ok()?, date_parts.next()?.parse().ok()?);
    let time = time.trim_end_matches('Z');
    let mut clock = time.split([':', '.']);
    let (hour, minute, second): (u32, u32, u32) = (clock.next()?.parse().ok()?, clock.next()?.parse().ok()?, clock.next().unwrap_or("0").parse().unwrap_or(0));
    let millis: i64 = clock.next().unwrap_or("0").chars().take(3).collect::<String>().parse().unwrap_or(0);
    Some(civil_to_unix(year, month, day, hour) * 1000 + minute as i64 * 60_000 + second as i64 * 1000 + millis)
}

pub fn build_chart(events: &[Value], hourly: bool, hidden: &[String]) -> Value {
    let models = chart_models(events);
    let visible: Vec<String> = models.iter().filter(|name| !hidden.iter().any(|item| item == *name)).cloned().collect();
    if events.is_empty() { return json!({"hourly": hourly, "caption": chart_caption(hourly, &[]), "models": models, "buckets": []}); }
    let keys = if hourly { hour_keys(events) } else { day_keys(events) };
    let mut cells: Vec<((String, String), (i64, f64, i64))> = Vec::new();
    for ev in events {
        let key = if hourly { event_hour(ev["timestamp_ms"].as_i64().unwrap_or(0)) } else { event_date(ev["timestamp_ms"].as_i64().unwrap_or(0)) };
        if key.as_str() < keys[0].as_str() || key.as_str() > keys.last().map(String::as_str).unwrap_or("") { continue; }
        let name = ev["model"].as_str().filter(|s| !s.is_empty()).unwrap_or("—").to_string();
        if !visible.iter().any(|item| item == &name) { continue; }
        if let Some(cell) = cells.iter_mut().find(|((k, n), _)| k == &key && n == &name) { cell.1.0 += ev["tokens"].as_i64().unwrap_or(0); cell.1.1 += cost_cents(ev); cell.1.2 += 1; }
        else { cells.push(((key, name), (ev["tokens"].as_i64().unwrap_or(0), cost_cents(ev), 1))); }
    }
    let multi_day = hourly && keys[0].get(..10) != keys.last().and_then(|k| k.get(..10));
    let buckets = keys.iter().map(|key| {
        let mut slices = Vec::new();
        let (mut tokens, mut cents, mut count) = (0, 0.0, 0);
        for name in &visible {
            if let Some((_, cell)) = cells.iter().find(|((k, n), _)| k == key && n == name) {
                if cell.0 > 0 || cell.1 > 0.0 || cell.2 > 0 { slices.push(json!({"model": name, "tokens": cell.0, "cents": cell.1, "count": cell.2})); tokens += cell.0; cents += cell.1; count += cell.2; }
            }
        }
        json!({"key": key, "label": bucket_label(key, hourly, multi_day), "tokens": tokens, "cents": cents, "count": count, "slices": slices})
    }).collect::<Vec<_>>();
    json!({"hourly": hourly, "caption": chart_caption(hourly, &keys), "models": models, "buckets": buckets})
}

pub fn chart_model_label(name: Option<&str>) -> String {
    let text = name.unwrap_or("").trim();
    text.strip_prefix("cursor-").unwrap_or(text).to_string()
}

pub const CSV_HEADER: &str = "日期(本地时间),用户,类型,模型,Token,费用,实付,折扣,云端Agent";

pub fn usage_events_csv(events: &[Value], newline: &str) -> String {
    let newline = if newline == "\n" { "\n" } else { "\r\n" };
    let mut out = String::from("\u{feff}");
    out.push_str(CSV_HEADER);
    out.push_str(newline);
    for event in events {
        let amount = event.get("allocated_cny").and_then(Value::as_f64).filter(|n| *n > 0.0);
        let cells = [
            event["timestamp_ms"].as_i64().filter(|n| *n > 0).map(format_time).unwrap_or_default(),
            event["user_email"].as_str().unwrap_or("").to_string(),
            kind_label(event["kind"].as_str()).to_string(),
            event["model"].as_str().unwrap_or("").to_string(),
            event["tokens"].as_i64().unwrap_or(0).to_string(),
            format_cost(event),
            amount.map(|n| format_cny(Some(n))).unwrap_or_else(|| "—".to_string()),
            format_discount(amount.unwrap_or(0.0), cost_cents(event), 0.0, event["kind"].as_str()),
            if event["is_headless"].as_bool().unwrap_or(false) { "是" } else { "否" }.to_string(),
        ];
        out.push_str(&cells.map(|cell| csv_cell(&cell)).join(","));
        out.push_str(newline);
    }
    out
}

fn csv_cell(value: &str) -> String {
    if value.contains([',', '"', '\n', '\r']) { format!("\"{}\"", value.replace('"', "\"\"")) } else { value.to_string() }
}

pub fn resolve_sync_window(start: Option<&str>, end: Option<&str>, end_overridden: bool, api_end: Option<&str>, now_ms: i64, watermark_ms: i64) -> Value {
    let cycle_start = event_timestamp_ms(start.unwrap_or("")).unwrap_or(now_ms - 30 * 86_400_000);
    let end_iso = if end_overridden { api_end.unwrap_or("") } else { end.unwrap_or("") };
    let mut cycle_end = event_timestamp_ms(end_iso).unwrap_or(now_ms).min(now_ms);
    let mut start_ms = cycle_start;
    let stop_at = if watermark_ms > 0 {
        start_ms = cycle_start.max(watermark_ms - 60_000);
        Some(watermark_ms)
    } else {
        None
    };
    if cycle_end < start_ms { cycle_end = now_ms; }
    json!({"start_ms": start_ms, "end_ms": cycle_end, "stop_at_ms": stop_at})
}

pub fn normalize_report_range(start: Option<&str>, end: Option<&str>) -> Value {
    let start = sanitize_report_date(start);
    let end = sanitize_report_date(end);
    if start.is_empty() || end.is_empty() || start <= end { json!({"start": start, "end": end, "swapped": false}) }
    else { json!({"start": end, "end": start, "swapped": true}) }
}

fn sanitize_report_date(raw: Option<&str>) -> String {
    let text = raw.unwrap_or("").trim();
    if text.len() >= 10 && text.as_bytes().get(4) == Some(&b'-') && text.as_bytes().get(7) == Some(&b'-') { text[..10].to_string() } else { String::new() }
}

pub fn format_time(timestamp_ms: i64) -> String { display(timestamp_ms).format("%Y-%m-%d %H:%M") }
pub fn event_date(timestamp_ms: i64) -> String { display(timestamp_ms).format("%Y-%m-%d") }
pub fn event_hour(timestamp_ms: i64) -> String { display(floor_hour(timestamp_ms)).format("%Y-%m-%d %H:00") }

fn chart_models(events: &[Value]) -> Vec<String> {
    let mut totals: Vec<(String, i64, f64, i64)> = Vec::new();
    for ev in events {
        let name = ev["model"].as_str().filter(|s| !s.is_empty()).unwrap_or("—").to_string();
        if let Some(row) = totals.iter_mut().find(|row| row.0 == name) { row.1 += ev["tokens"].as_i64().unwrap_or(0); row.2 += cost_cents(ev); row.3 += 1; }
        else { totals.push((name, ev["tokens"].as_i64().unwrap_or(0), cost_cents(ev), 1)); }
    }
    totals.sort_by(|a, b| b.1.cmp(&a.1).then(b.2.total_cmp(&a.2)).then(b.3.cmp(&a.3)).then(a.0.cmp(&b.0)));
    totals.into_iter().map(|row| row.0).collect()
}

fn hour_keys(events: &[Value]) -> Vec<String> {
    let last = floor_hour(events.iter().map(|ev| ev["timestamp_ms"].as_i64().unwrap_or(0)).max().unwrap_or(0));
    let mut first = floor_hour(events.iter().map(|ev| ev["timestamp_ms"].as_i64().unwrap_or(0)).min().unwrap_or(0));
    let span = (last - first) / MS_HOUR + 1;
    if span > 48 { first = last - 47 * MS_HOUR; }
    (0..=(last - first) / MS_HOUR).map(|i| event_hour(first + i * MS_HOUR)).collect()
}

fn day_keys(events: &[Value]) -> Vec<String> {
    let last = floor_day(events.iter().map(|ev| ev["timestamp_ms"].as_i64().unwrap_or(0)).max().unwrap_or(0));
    let first = floor_day(events.iter().map(|ev| ev["timestamp_ms"].as_i64().unwrap_or(0)).min().unwrap_or(0));
    let mut keys = Vec::new();
    let mut day = first;
    while day <= last { keys.push(event_date(day)); day += MS_DAY; }
    keys
}

fn chart_caption(hourly: bool, keys: &[String]) -> String {
    let kind = if hourly { "按小时 Token" } else { "按日 Token" };
    if keys.is_empty() { return format!("{kind}（{TZ_LABEL}）"); }
    let (first, last) = (&keys[0], keys.last().unwrap());
    if first == last { return format!("{kind}（{TZ_LABEL} · {first}）"); }
    if hourly && first.get(..10) == last.get(..10) { return format!("{kind}（{TZ_LABEL} · {} {}–{}）", &first[..10], &first[11..], &last[11..]); }
    format!("{kind}（{TZ_LABEL} · {first} 至 {last}）")
}

fn bucket_label(key: &str, hourly: bool, multi_day: bool) -> String {
    if !hourly { return if key.len() >= 10 { key[5..].to_string() } else { key.to_string() }; }
    let hour = if key.len() >= 13 { &key[11..13] } else { key };
    if multi_day { format!("{} {hour}", &key[5..10]) } else { hour.to_string() }
}

fn normalize_range(start: Option<&str>, end: Option<&str>) -> (String, String, bool) {
    let (s, e) = (sanitize_date(start), sanitize_date(end));
    if s.is_empty() || e.is_empty() || s <= e { (s, e, false) } else { (e, s, true) }
}

fn sanitize_date(raw: Option<&str>) -> String {
    let text = raw.unwrap_or("").trim();
    let mut parts = text.split('-');
    match (parts.next(), parts.next(), parts.next(), parts.next()) {
        (Some(y), Some(m), Some(d), None) if y.len() == 4 && m.len() == 2 && d.len() == 2 => text.to_string(),
        _ => String::new(),
    }
}

fn date_start_ms(date: &str) -> Option<i64> { date_parts(date).map(|(y, m, d)| civil_to_unix(y, m, d, 0) * 1000 - TZ_OFFSET_MS) }
fn date_end_ms(date: &str) -> Option<i64> { date_start_ms(date).map(|ms| ms + MS_DAY) }
fn date_parts(date: &str) -> Option<(i32, u32, u32)> {
    if date.is_empty() { return None; }
    let mut parts = date.split('-');
    Some((parts.next()?.parse().ok()?, parts.next()?.parse().ok()?, parts.next()?.parse().ok()?))
}

struct Display { year: i32, month: u32, day: u32, hour: u32, minute: u32 }
impl Display {
    fn format(&self, pattern: &str) -> String {
        match pattern {
            "%Y-%m-%d %H:%M" => format!("{:04}-{:02}-{:02} {:02}:{:02}", self.year, self.month, self.day, self.hour, self.minute),
            "%Y-%m-%d %H:00" => format!("{:04}-{:02}-{:02} {:02}:00", self.year, self.month, self.day, self.hour),
            _ => format!("{:04}-{:02}-{:02}", self.year, self.month, self.day),
        }
    }
}

fn display(timestamp_ms: i64) -> Display {
    let secs = timestamp_ms.max(0) / 1000 + TZ_OFFSET_MS / 1000;
    let (year, month, day) = civil_from_days(secs.div_euclid(86_400));
    let clock = secs.rem_euclid(86_400);
    Display { year, month, day, hour: (clock / 3600) as u32, minute: (clock % 3600 / 60) as u32 }
}

fn floor_hour(timestamp_ms: i64) -> i64 { timestamp_ms.max(0) / MS_HOUR * MS_HOUR }
fn floor_day(timestamp_ms: i64) -> i64 { let local = timestamp_ms.max(0) + TZ_OFFSET_MS; local / MS_DAY * MS_DAY - TZ_OFFSET_MS }

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

fn civil_to_unix(year: i32, month: u32, day: u32, hour: u32) -> i64 {
    let y = if month <= 2 { year - 1 } else { year } as i64;
    let era = if y >= 0 { y } else { y - 399 } / 400;
    let yoe = (y - era * 400) as u64;
    let mp = if month > 2 { month - 3 } else { month + 9 } as u64;
    let doy = (153 * mp + 2) / 5 + day as u64 - 1;
    let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    (era * 146_097 + doe as i64 - 719_468) * 86_400 + hour as i64 * 3600
}

fn text<'a>(value: &'a Value, key: &str) -> &'a str { value[key].as_str().unwrap_or("").trim() }

#[allow(dead_code)]
fn unused_map() -> Map<String, Value> { Map::new() }

fn display_model(raw: &str) -> String {
    let name = raw.trim();
    if name.is_empty() { String::new() } else if name == "default" { "auto".to_string() } else { name.to_string() }
}
