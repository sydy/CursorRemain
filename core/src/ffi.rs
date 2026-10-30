use crate::{config, events, login, status, sync, token, update, usage, validity};
use serde_json::{json, Value};
use std::ffi::{CStr, CString};
use std::os::raw::c_char;

/// Run one pure operation. `op` names the operation and `input` is UTF-8 JSON.
/// The returned string is owned by Rust until [cr_free].
#[no_mangle]
pub extern "C" fn cr_call(op: *const c_char, input: *const c_char) -> *mut c_char {
    let op = unsafe { CStr::from_ptr(op) }.to_string_lossy();
    let input = unsafe { CStr::from_ptr(input) }.to_string_lossy();
    let value = match serde_json::from_str::<Value>(&input) {
        Ok(value) => value,
        Err(err) => return own(&json!({"error": err.to_string(), "kind": "parse"})),
    };
    own(&dispatch(&op, &value))
}

#[no_mangle]
pub extern "C" fn cr_free(ptr: *mut c_char) {
    if ptr.is_null() {
        return;
    }
    unsafe { drop(CString::from_raw(ptr)) }
}

fn dispatch(op: &str, input: &Value) -> Value {
    match op {
        "normalize_token" => match token::normalize(text(input, "input")) {
            Ok(value) => json!({"ok": true, "value": value}),
            Err(err) => json!({"ok": false, "error": err.message, "kind": "parse"}),
        },
        "token_variants" => json!(token::variants(text(input, "input"))),
        "account_id" => json!(token::account_id(text(input, "input"))),
        "safe_account_id" => json!(token::safe_account_id(text(input, "input"))),
        "jwt_type" => json!(token::jwt_type(text(input, "input"))),
        "can_write_to_cursor" => json!(token::can_write_to_cursor(text(input, "input"))),
        "extract_jwt" => json!(token::extract_jwt(text(input, "input"))),
        "parse_usage_summary" => usage::parse_usage_summary(&input["payload"], input["now"].as_str()),
        "parse_sand_usage" => usage::parse_sand_usage_status(&input["payload"], input["now"].as_str()).unwrap_or(Value::Null),
        "apply_sand_usage" => {
            let mut snapshot = input["snapshot"].clone();
            usage::apply_sand_usage(&mut snapshot, &input["payload"], input["now"].as_str());
            snapshot
        }
        "parse_aggregated_usage" => usage::parse_aggregated_usage(&input["payload"], input["auto_percent"].as_f64(), input["api_percent"].as_f64()),
        "format_membership" => json!(usage::format_membership_type(input["input"].as_str())),
        "format_usd_cents" => json!(usage::format_usd_cents(input["cents"].as_f64())),
        "format_spend_range" => json!(usage::format_spend_range(input["used"].as_f64(), input["limit"].as_f64())),
        "format_token_count" => json!(usage::format_token_count(input["n"].as_f64())),
        "format_plan_caption" => json!(status::format_plan_caption(input["membership"].as_str(), input["label"].as_str())),
        "status_pill_text" => json!(status::status_pill_text(input["remaining"].as_f64(), input["error"].as_bool().unwrap_or(false))),
        "short_error" => json!(status::short_error(input["text"].as_str(), input["max_len"].as_u64().unwrap_or(40) as usize)),
        "export_file_slug" => json!(status::export_file_slug(input["label"].as_str())),
        "format_export_filename" => json!(status::format_export_filename(text(input, "prefix"), input["label"].as_str(), text(input, "day"))),
        "format_compare_account_name" => json!(status::format_compare_account_name(input["name"].as_str(), input["active"].as_bool().unwrap_or(false))),
        "format_token_save_result" => json!(status::format_token_save_result(input["ok"].as_i64().unwrap_or(0), input["fail"].as_i64().unwrap_or(0))),
        "format_compare_sync" => {
            let failures = input["failures"].as_array().map(|rows| rows.iter().filter_map(|v| v.as_str().map(str::to_string)).collect::<Vec<_>>()).unwrap_or_default();
            json!(status::format_compare_sync(input["ok"].as_i64().unwrap_or(0), &failures, text(input, "stamp")))
        },
        "format_report_sync_result" => json!(status::format_report_sync_result(input["count"].as_i64().unwrap_or(0), input["fetched"].as_i64().unwrap_or(0), text(input, "stamp"), input["truncated"].as_bool().unwrap_or(false), input["total_available"].as_i64().unwrap_or(0), text(input, "note"), input["has_token"].as_bool().unwrap_or(true))),
        "format_compare_sync_progress" => json!(status::format_compare_sync_progress(input["index"].as_i64().unwrap_or(0), input["total"].as_i64().unwrap_or(0), text(input, "name"), input["page"].as_i64().unwrap_or(0))),
        "format_report_per_million_kpi" => json!(status::format_report_per_million_kpi(input["total_cny"].as_f64().unwrap_or(0.0), input["total_tokens"].as_i64().unwrap_or(0))),
        "format_report_spend_kpi" => json!(status::format_report_spend_kpi(input["total_cny"].as_f64().unwrap_or(0.0), input["plan_cny"].as_f64().unwrap_or(0.0), input["on_demand_cny"].as_f64().unwrap_or(0.0), input["usd_cny_rate"].as_f64().unwrap_or(0.0), input["uses_actual"].as_bool().unwrap_or(false), input["window_plan_cny"].as_f64().unwrap_or(0.0))),
        "is_auth_error_message" => json!(status::is_auth_error_message(text(input, "message"))),
        "format_flyout_error" => json!(status::format_flyout_error(text(input, "error"))),
        "format_compare_hint" => json!(status::format_compare_hint(input["mixed"].as_bool().unwrap_or(false))),
        "format_report_sync_progress" => json!(status::format_report_sync_progress(input["page"].as_i64().unwrap_or(0))),
        "format_report_filter_empty" => json!(status::format_report_filter_empty(input["total"].as_i64().unwrap_or(0))),
        "format_account_menu_title" => json!(status::format_account_menu_title(text(input, "label"), input["remaining"].as_f64())),
        "is_trim_note" => json!(status::is_trim_note(text(input, "text"))),
        "format_cloud_sync_notify" => json!(status::format_cloud_sync_notify(input["ok"].as_bool().unwrap_or(false), text(input, "message"))),
        "format_report_cache_status" => json!(status::format_report_cache_status(input["count"].as_i64().unwrap_or(0), text(input, "label"))),
        "format_report_sync_error" => json!(status::format_report_sync_error(text(input, "error"))),
        "format_cloud_decrypt_note" => json!(status::format_cloud_decrypt_note(input["decrypt_error"].as_bool().unwrap_or(false), input["sync_secret_failed"].as_bool().unwrap_or(false), input["cloud_access_failed"].as_bool().unwrap_or(false))),
        "format_account_plan" => json!(status::format_account_plan(text(input, "membership"))),
        "format_account_check_status" => json!(status::format_account_check_status(input["decrypt_failed"].as_bool().unwrap_or(false), text(input, "error"), input["remaining"].as_f64(), input["has_token"].as_bool().unwrap_or(false))),
        "format_account_kind" => json!(status::format_account_kind(text(input, "kind"))),
        "format_trim_note" => json!(status::format_trim_note(input["dropped"].as_i64().unwrap_or(0))),
        "append_trim_note" => json!(status::append_trim_note(text(input, "message"), text(input, "note"))),
        "daily_avg_burn" => json!(status::daily_avg_burn(input["points"].as_array().map(Vec::as_slice).unwrap_or(&[]))),
        "flyout_settings_title" => json!(status::flyout_settings_title(text(input, "error"))),
        "compare_row_note" => json!(events::compare_row_note(input["plan_cny"].as_f64().unwrap_or(0.0), input["window_plan_cny"].as_f64().unwrap_or(0.0), input["has_token"].as_bool().unwrap_or(true), text(input, "last_error"))),
        "compare_best_per_million" => json!(events::compare_best_per_million(input["rows"].as_array().map(Vec::as_slice).unwrap_or(&[]))),
        "compare_windows_mixed" => json!(events::compare_windows_mixed(input["rows"].as_array().map(Vec::as_slice).unwrap_or(&[]))),
        "format_estimated_days" => json!(status::format_estimated_days(input.get("usage"))),
        "format_estimate_caption" => json!(status::format_estimate_caption(input.get("usage"))),
        "build_status_lines" => status::build_status_lines(input.get("usage").filter(|v| !v.is_null()), input["error"].as_str(), input["updated_at"].as_str(), input["label"].as_str(), input["now"].as_str()),
        "is_team_membership" => json!(usage::is_team_membership(input["membership"].as_str(), input["limit_type"].as_str())),
        "is_x_membership" => json!(usage::is_x_membership(input["input"].as_str())),
        "membership_key" => json!(usage::membership_key(input["input"].as_str())),
        "dashboard_url" => json!(usage::dashboard_url(text(input, "membership"), text(input, "limit_type"), flag(input, "x"))),
        "dashboard_menu_label" => json!(usage::dashboard_menu_label(text(input, "membership"), text(input, "limit_type"), flag(input, "x"))),
        "dashboard_button_label" => json!(usage::dashboard_button_label(text(input, "membership"), text(input, "limit_type"), flag(input, "x"))),
        "dashboard_link_label" => json!(usage::dashboard_link_label(text(input, "membership"), flag(input, "x"), flag(input, "team"))),
        "is_grok_bot_model" => json!(usage::is_grok_bot_model(input["input"].as_str())),
        "is_first_party_model" => json!(usage::is_first_party_model(input["input"].as_str())),
        "usage_category" => json!(usage::usage_category(input["input"].as_str())),
        "classify_kind" => json!(events::classify_kind(input["kind"].as_str(), input["costs"].as_str(), input["chargeable"].as_bool().unwrap_or(false))),
        "kind_label" => json!(events::kind_label(input["kind"].as_str())),
        "category_label" => json!(events::category_label(input["category"].as_str())),
        "classify_category" => json!(events::classify_category(input["model"].as_str())),
        "parse_usage_page" => events::parse_page(&input["payload"]),
        "merge_usage_events" => json!(events::merge(input["existing"].as_array().map(Vec::as_slice).unwrap_or(&[]), input["incoming"].as_array().map(Vec::as_slice).unwrap_or(&[]))),
        "prune_usage_events" => json!(events::prune(input["events"].as_array().map(Vec::as_slice).unwrap_or(&[]), input["min_timestamp_ms"].as_i64().unwrap_or(0))),
        "format_event_cost" => json!(events::format_cost(&input["event"])),
        "usage_events_csv" => json!(events::usage_events_csv_full(
            input["events"].as_array().map(Vec::as_slice).unwrap_or(&[]),
            input.get("spend").filter(|s| s.is_object()),
            input["allocation_base"].as_array().map(Vec::as_slice),
            input.get("allocation").filter(|a| a.is_object()),
            if input["lf"].as_bool().unwrap_or(false) { "\n" } else { "\r\n" }
        )),
        "normalize_appearance" => json!(config::normalize_appearance(input["input"].as_str())),
        "build_usage_report" => events::build_report(input["events"].as_array().map(Vec::as_slice).unwrap_or(&[]), &input["filter"], input.get("spend"), input.get("allocation")),
        "build_usage_chart" => events::build_chart(input["events"].as_array().map(Vec::as_slice).unwrap_or(&[]), input["hourly"].as_bool().unwrap_or(false), &strings(input, "hidden_models")),
        "chart_model_label" => json!(events::chart_model_label(input["input"].as_str())),
        "format_event_time" => json!(events::format_time(input["timestamp_ms"].as_i64().unwrap_or(0))),
        "event_date" => json!(events::event_date(input["timestamp_ms"].as_i64().unwrap_or(0))),
        "event_hour" => json!(events::event_hour(input["timestamp_ms"].as_i64().unwrap_or(0))),
        "resolve_sync_window" => events::resolve_sync_window(input["billing_cycle_start"].as_str(), input["billing_cycle_end"].as_str(), input["end_overridden"].as_bool().unwrap_or(false), input["api_billing_cycle_end"].as_str(), input["now_ms"].as_i64().unwrap_or(0), input["watermark_ms"].as_i64().unwrap_or(0)),
        "normalize_report_range" => events::normalize_report_range(input["start"].as_str(), input["end"].as_str()),
        "default_monthly_plan_usd" => json!(events::default_monthly_plan_usd(input["membership"].as_str())),
        "format_cny" => json!(events::format_cny(input["cny"].as_f64())),
        "format_discount" => json!(events::format_discount(input["cny"].as_f64().unwrap_or(f64::NAN), input["cents"].as_f64().unwrap_or(f64::NAN), input["rate"].as_f64().unwrap_or(f64::NAN), input["kind"].as_str())),
        "sanitize_channel" => json!(events::sanitize_channel(input["input"].as_str())),
        "channel_label" => json!(events::channel_label(input["input"].as_str())),
        "build_account_compare" => events::build_account_compare(input["items"].as_array().map(Vec::as_slice).unwrap_or(&[]), input["now_ms"].as_i64()),
        "account_compare_csv" => json!(events::account_compare_csv(&input["report"], if input["lf"].as_bool().unwrap_or(false) { "\n" } else { "\r\n" })),
        "merge_sync_snapshots" => sync::merge_snapshots(&input["local"], &input["remote"]),
        "compare_iso" => json!(sync::compare_iso(text(input, "left"), text(input, "right"))),
        "newer_iso" => json!(sync::newer_iso(input["left"].as_str(), input["right"].as_str())),
        "parse_thresholds" => json!(sync::parse_thresholds(&input["value"])),
        "parse_settings_stamp" => status::parse_settings_stamp(text(input, "raw")).map(|dt| dt.to_json()).unwrap_or(Value::Null),
        "is_exhaustion_risk" => json!(status::is_exhaustion_risk(input["estimated_usable_days"].as_f64(), input["days_remaining"].as_i64())),
        "sanitize_account_kind" => json!(validity::sanitize_kind(input["input"].as_str())),
        "clamp_account_days" => json!(validity::clamp_days(&input["value"])),
        "clamp_account_hours" => json!(validity::clamp_hours(&input["value"])),
        "validity_equals" => json!(validity::validity_equals(input["a_kind"].as_str(), input["a_start"].as_str(), &input["a_days"], &input["a_hours"], input["b_kind"].as_str(), input["b_start"].as_str(), &input["b_days"], &input["b_hours"])),
        "compute_account_end" => json!(validity::compute_end_iso(input["start"].as_str(), input["days"].as_i64().unwrap_or(0), input["hours"].as_i64().unwrap_or(0))),
        "apply_account_end_override" => validity::apply_end_override(&input["account"], input["api_end"].as_str(), input["now"].as_str(), input["days_remaining"].as_i64().unwrap_or(0)),
        "sanitize_email" => json!(login::sanitize_email(input["input"].as_str())),
        "default_account_label" => json!(login::default_label(text(input, "email"), input["existing"].as_str())),
        "token_from_cookies" => json!(login::token_from_cookies(&cookie_pairs(input))),
        "autofill_script" => json!(login::autofill_script(text(input, "email"), text(input, "password"))),
        "parse_account_paste" => json!(paste_items(&login::parse_account_paste(input["text"].as_str()))),
        "looks_like_token" => json!(login::looks_like_token(text(input, "line"))),
        "is_single_token" => json!(login::is_single_token(input["text"].as_str())),
        "token_values" => json!(login::token_values(input["text"].as_str())),
        "code_challenge" => json!(login::code_challenge(input["verifier"].as_str())),
        "login_deep_url" => json!(login::login_deep_url(input["uuid"].as_str(), input["verifier"].as_str())),
        "token_from_callback" => json!(login::token_from_callback(input["input"].as_str())),
        "token_from_poll" => json!(login::token_from_poll(input)),
        "profile_key" => json!(login::profile_key(input["email"].as_str(), input["account_id"].as_str())),
        "assign_profile" => login::assign_profile(input["index"].as_object().unwrap_or(&serde_json::Map::new()), input["key"].as_str(), input["new_id"].as_str()),
        "extract_sha" => json!(update::extract_sha(input["text"].as_str().or(input["input"].as_str()))),
        "normalize_sha" => json!(update::normalize_sha(input["input"].as_str())),
        "same_sha" => json!(update::same_sha(input["left"].as_str(), input["right"].as_str())),
        "sha_from_informational_version" => json!(update::sha_from_informational_version(input["input"].as_str())),
        "normalize_product_version" => json!(update::normalize_product_version(input["input"].as_str())),
        "product_version_from_release" => json!(update::product_version_from_release(input["tag"].as_str(), input["body"].as_str())),
        "compare_product_versions" => json!(update::compare_product_versions(input["left"].as_str(), input["right"].as_str())),
        "choose_release" => {
            let official = release_of(input.get("official"));
            let rolling = release_of(input.get("rolling"));
            update::choose_release(official.as_ref(), rolling.as_ref(), input["current_version"].as_str()).map(|release| update::release_to_json(&release)).unwrap_or(Value::Null)
        },
        "display_version" => json!(update::display_version(input["sha"].as_str(), text(input, "product_version"))),
        "is_allowed_download_url" => json!(update::is_allowed_download_url(input["url"].as_str())),
        "looks_packaged_windows" => json!(update::looks_packaged_windows(input["path"].as_str())),
        "looks_packaged_macos" => json!(update::looks_packaged_macos(input["path"].as_str())),
        "is_windows_light_asset_name" => json!(update::is_windows_light_asset_name(input["name"].as_str())),
        "parse_release" => match input.get("json").filter(|value| !value.is_null()) {
            Some(raw) => match update::parse_release(raw) {
                Ok(release) => update::release_to_json(&release),
                Err(err) => json!({"error": err, "kind": "parse"}),
            },
            None => json!({"error": "json required", "kind": "parse"}),
        },
        "parse_release_page" => match update::parse_release_page(input["html"].as_str()) {
            Ok(release) => update::release_to_json(&release),
            Err(err) => json!({"error": err, "kind": "parse"}),
        },
        "parse_tag_ref_sha" => json!(update::parse_tag_ref_sha(input.get("json").unwrap_or(&Value::Null))),
        "find_asset" => release_of(input.get("release")).and_then(|release| update::find_asset(&release, text(input, "name")).map(update::asset_to_json)).unwrap_or(Value::Null),
        "asset_name_candidates" => json!(update::asset_name_candidates_or_self(text(input, "preferred"))),
        "preferred_windows_asset_name" => json!(update::preferred_windows_asset_name(flag(input, "prefer_light"))),
        "find_preferred_asset" => release_of(input.get("release")).and_then(|release| update::find_preferred_asset(&release, text(input, "preferred")).map(update::asset_to_json)).unwrap_or(Value::Null),
        "evaluate_update" => match release_of(input.get("release")) {
            Some(release) => update::decision_to_json(&update::evaluate(&release, text(input, "asset"), text(input, "current_sha"), text(input, "installed_sha"), input["installed_asset_id"].as_i64().unwrap_or(0), input["current_version"].as_str())),
            None => json!({"error": "release required", "kind": "parse"}),
        },
        "remembered_install_after_helper" => update::remembered_install_after_helper(text(input, "sha"), input["asset_id"].as_i64().unwrap_or(0), flag(input, "helper_started")).map(|item| json!({"sha": item.sha, "asset_id": item.asset_id})).unwrap_or(Value::Null),
        "pending_install_json" => update::pending_install_json(text(input, "sha"), input["asset_id"].as_i64().unwrap_or(0)),
        "read_pending_install" => update::read_pending_install_json(text(input, "json")).map(|item| json!({"sha": item.sha, "asset_id": item.asset_id})).unwrap_or(Value::Null),
        "confirm_pending_install" => remembered_of(input.get("pending")).and_then(|pending| update::confirm_pending_install(text(input, "current_sha"), Some(&pending))).map(|item| json!({"sha": item.sha, "asset_id": item.asset_id})).unwrap_or(Value::Null),
        "pending_install_failed" => json!(update::pending_install_failed(text(input, "current_sha"), remembered_of(input.get("pending")).as_ref())),
        "should_fallback_from_api" => json!(update::should_fallback_from_api(input["status_code"].as_i64().unwrap_or(0) as i32)),
        "should_auto_check" => json!(update::should_auto_check(text(input, "last_check_at"), text(input, "now"), flag(input, "manual"))),
        "now_iso" => json!(update::now_iso(input["unix"].as_i64().unwrap_or(0))),
        _ => json!({"error": format!("unknown op {op}"), "kind": "internal"}),
    }
}

fn text<'a>(input: &'a Value, key: &str) -> &'a str {
    input[key].as_str().unwrap_or("")
}

fn strings(input: &Value, key: &str) -> Vec<String> {
    input[key].as_array().map(|rows| rows.iter().filter_map(|row| row.as_str().map(str::to_string)).collect()).unwrap_or_default()
}

fn flag(input: &Value, key: &str) -> bool {
    input[key].as_bool().unwrap_or(false)
}

fn cookie_pairs(input: &Value) -> Vec<(String, String)> {
    input["cookies"]
        .as_array()
        .map(|rows| {
            rows.iter()
                .map(|row| (row["name"].as_str().unwrap_or("").to_string(), row["value"].as_str().unwrap_or("").to_string()))
                .collect()
        })
        .unwrap_or_default()
}

fn paste_items(items: &[login::PasteItem]) -> Vec<Value> {
    items.iter()
        .map(|item| {
            json!({
                "kind": item.kind,
                "token": item.token,
                "email": item.email,
                "password": item.password,
                "message": item.message,
            })
        })
        .collect()
}

fn release_of(value: Option<&Value>) -> Option<update::AppRelease> {
    let value = value.filter(|item| !item.is_null())?;
    let raw = value.get("release").filter(|item| item.is_object()).unwrap_or(value);
    Some(update::AppRelease {
        tag: text(raw, "tag").to_string(),
        version: text(raw, "version").to_string(),
        body: text(raw, "body").to_string(),
        commit_sha: raw["sha"].as_str().or(raw["commit_sha"].as_str()).unwrap_or("").to_string(),
        page_url: text(raw, "page_url").to_string(),
        published_at: text(raw, "published_at").to_string(),
        assets: raw["assets"].as_array().map(|rows| rows.iter().map(|row| update::AppReleaseAsset {
            id: row["id"].as_i64().unwrap_or(0),
            name: text(row, "name").to_string(),
            url: text(row, "url").to_string(),
            size: row["size"].as_i64().unwrap_or(0),
            updated_at: text(row, "updated_at").to_string(),
        }).collect()).unwrap_or_default(),
    })
}

fn remembered_of(value: Option<&Value>) -> Option<update::RememberedInstall> {
    let value = value.filter(|item| item.is_object())?;
    Some(update::RememberedInstall { sha: text(value, "sha").to_string(), asset_id: value["asset_id"].as_i64().unwrap_or(0) })
}

fn own(value: &Value) -> *mut c_char {
    CString::new(value.to_string()).unwrap_or_else(|_| CString::new("").unwrap()).into_raw()
}
