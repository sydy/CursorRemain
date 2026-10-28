use cursor_core::{
    account_id, apply_sand_usage, build_status_lines, format_membership_type, format_plan_caption, format_spend_range,
    format_token_count, format_usd_cents, normalize, parse_aggregated_usage, parse_sand_usage_status, parse_usage_summary,
    status_pill_text, variants,
};
use serde_json::{json, Value};
use std::fs;
use std::path::PathBuf;

fn normalize_events(events: &[Value]) -> Vec<Value> {
    events.iter().map(|ev| {
        let mut copy = ev.clone();
        if copy.get("timestamp_ms").is_none() {
            if let Some(ms) = copy["timestamp"].as_str().and_then(|text| cursor_core::events::event_timestamp_ms(text)) { copy["timestamp_ms"] = json!(ms); }
            else if let Some(ms) = copy["timestamp"].as_i64() { copy["timestamp_ms"] = json!(ms); }
        }
        if copy.get("charged_cents").is_none() { if let Some(cents) = copy.get("chargedCents") { copy["charged_cents"] = cents.clone(); } }
        copy
    }).collect()
}

fn fixture(name: &str) -> Value {
    let path = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("..").join("fixtures").join(name);
    let text = fs::read_to_string(&path).unwrap_or_else(|err| panic!("read {}: {err}", path.display()));
    serde_json::from_str(&text).unwrap_or_else(|err| panic!("parse {}: {err}", path.display()))
}

fn opt_f64(value: &Value, key: &str) -> Option<f64> {
    match &value[key] {
        Value::Number(n) => n.as_f64(),
        _ => None,
    }
}

fn opt_str<'a>(value: &'a Value, key: &str) -> Option<&'a str> {
    value[key].as_str()
}

#[test]
fn usage_summary_cases() {
    for case in fixture("usage_summary_cases.json").as_array().unwrap() {
        let name = case["name"].as_str().unwrap_or("");
        let got = parse_usage_summary(&case["payload"], None);
        let exp = &case["expected"];
        for key in [
            "used_percent", "remaining_percent", "auto_percent_used", "api_percent_used", "total_percent_used",
            "used_cents", "limit_cents", "remaining_cents", "on_demand_used_cents", "on_demand_limit_cents",
            "pooled_used_cents", "pooled_limit_cents", "prepaid_remaining_cents",
        ] {
            if !exp.get(key).is_some_and(|v| !v.is_null()) && !got.get(key).is_some_and(|v| !v.is_null()) {
                continue;
            }
            assert_eq!(opt_f64(&got, key), opt_f64(exp, key), "{name}.{key}");
        }
        for key in ["membership_type", "billing_mode", "limit_type", "dashboard_url", "dashboard_button_label", "dashboard_menu_label", "dashboard_link_label"] {
            assert_eq!(got[key].as_str(), exp[key].as_str(), "{name}.{key}");
        }
        for key in ["is_unlimited", "is_team_account", "shows_amount"] {
            assert_eq!(got[key].as_bool(), exp[key].as_bool(), "{name}.{key}");
        }
        let expect_x = exp.get("is_x_subscription").and_then(Value::as_bool).unwrap_or(false);
        assert_eq!(got["is_x_subscription"].as_bool(), Some(expect_x), "{name}.is_x_subscription");
        if exp.get("usage_reset_at").is_some_and(Value::is_string) {
            assert_eq!(opt_str(&got, "usage_reset_at"), opt_str(exp, "usage_reset_at"), "{name}.usage_reset_at");
        }
    }
}

#[test]
fn token_cases() {
    let root = fixture("token_cases.json");
    for row in root["account_ids"].as_array().unwrap() {
        assert_eq!(account_id(row["token"].as_str().unwrap()), row["id"].as_str().unwrap());
    }
    let got = variants(root["variants_jwt"]["input"].as_str().unwrap());
    let expected: Vec<String> = root["variants_jwt"]["variants"].as_array().unwrap().iter().map(|v| v.as_str().unwrap().to_string()).collect();
    assert_eq!(got, expected);
    for row in root["normalize"].as_array().unwrap() {
        assert_eq!(normalize(row["input"].as_str().unwrap()).unwrap(), row["output"].as_str().unwrap());
    }
    for row in root["normalize_errors"].as_array().unwrap() {
        let err = normalize(row["input"].as_str().unwrap()).unwrap_err();
        assert!(err.message.contains(row["message_contains"].as_str().unwrap()), "{}", err.message);
    }
}

#[test]
fn format_cases() {
    let root = fixture("format_cases.json");
    for row in root["membership"].as_array().unwrap() {
        assert_eq!(format_membership_type(row["input"].as_str()), row["output"].as_str().unwrap());
    }
    for row in root["usd"].as_array().unwrap() {
        assert_eq!(format_usd_cents(row["cents"].as_f64()), row["output"].as_str().unwrap());
    }
    for row in root["spend_range"].as_array().unwrap() {
        assert_eq!(format_spend_range(row["used"].as_f64(), row["limit"].as_f64()), row["output"].as_str().unwrap());
    }
    for row in root["token_count"].as_array().unwrap() {
        assert_eq!(format_token_count(row["n"].as_f64()), row["output"].as_str().unwrap());
    }
    for row in root["plan_caption"].as_array().unwrap() {
        assert_eq!(format_plan_caption(row["membership"].as_str(), row["label"].as_str()), row["output"].as_str().unwrap());
    }
    for row in root["status_pill"].as_array().unwrap() {
        assert_eq!(status_pill_text(row["remaining"].as_f64(), row["error"].as_bool().unwrap_or(false)), row["output"].as_str().unwrap());
    }

    let enterprise = fixture("usage_summary_cases.json")
        .as_array()
        .unwrap()
        .iter()
        .find(|row| row["name"] == "enterprise_overall")
        .unwrap()
        .clone();
    // Same assertions FixtureTests.FormatCases hard-codes. Parsing uses no wall clock.
    let parsed = parse_usage_summary(&enterprise["payload"], Some("2026-07-11T12:00:00Z"));
    let parsed_lines = build_status_lines(Some(&parsed), None, Some("12:00"), None, Some("2026-07-11T12:00:00Z"));
    assert!(line_value(&parsed_lines, "剩余").unwrap().contains("$73.84 / $100"));
    assert_eq!(line_value(&parsed_lines, "金额").as_deref(), Some("$73.84 / $100"));
    assert!(line_value(&parsed_lines, "团队额度").unwrap().contains('$'));
    assert_eq!(line_value(&parsed_lines, "计划").as_deref(), Some("Enterprise"));

    // status_lines_enterprise freezes the formatted rows. Its 0 remaining days and
    // 20.3-day estimate are snapshot fields, not a single UtcNow.
    let snap = json!({
        "remaining_percent": 26.2,
        "used_percent": 73.8,
        "shows_amount": true,
        "used_cents": 7384.0,
        "limit_cents": 10000.0,
        "membership_type": "Enterprise",
        "pooled_used_cents": 12725135.0,
        "pooled_limit_cents": 28122000.0,
        "billing_cycle_end": "2026-08-01T00:00:00.000Z",
        "days_remaining": 0,
        "estimated_usable_days": 20.3
    });
    let lines = build_status_lines(Some(&snap), None, Some("12:00"), None, Some("2026-07-11T12:00:00Z"));
    let expected = &root["status_lines_enterprise"];
    if let Some(map) = expected.as_object() {
        for (key, value) in map {
            assert_eq!(line_value(&lines, key).as_deref(), value.as_str(), "{key}");
        }
    }
}

fn line_value(lines: &Value, key: &str) -> Option<String> {
    lines.as_array()?.iter().find(|row| row["key"] == key)?.get("value")?.as_str().map(str::to_string)
}

#[test]
fn aggregated_usage_cases() {
    for case in fixture("aggregated_usage_cases.json").as_array().unwrap() {
        let got = parse_aggregated_usage(&case["payload"], case["auto_percent"].as_f64(), case["api_percent"].as_f64());
        assert_eq!(got["total"].as_i64(), case["total"].as_i64());
        let models = got["models"].as_array().unwrap();
        let expected = case["models"].as_array().unwrap();
        assert_eq!(models.len(), expected.len());
        for (got, exp) in models.iter().zip(expected) {
            assert_eq!(got["name"].as_str(), exp["name"].as_str());
            assert_eq!(got["tokens"].as_i64(), exp["tokens"].as_i64());
            assert_eq!(got["usage_percent"].as_f64(), exp["usage_percent"].as_f64());
        }
    }
}

#[test]
fn usage_event_parse_cases() {
    let root = fixture("usage_events_cases.json");
    let parsed = cursor_core::events::parse_page(&root["parse"][0]["payload"]);
    let csv = cursor_core::events::usage_events_csv(parsed["events"].as_array().unwrap(), "\r\n");
    assert!(csv.starts_with('\u{feff}'));
    assert!(csv.trim_start_matches('\u{feff}').starts_with(root["csv_header"].as_str().unwrap()));
    for row in root["kind"].as_array().unwrap() {
        assert_eq!(cursor_core::events::classify_kind(row["kind"].as_str(), row["usage_based_costs"].as_str(), row["is_chargeable"].as_bool().unwrap()), row["output"].as_str().unwrap());
    }
    for (key, label) in [("included", "套餐内"), ("free", "免费"), ("on_demand", "按需")] {
        assert_eq!(cursor_core::events::kind_label(Some(key)), root["labels"][key].as_str().unwrap_or(label));
    }
    for row in root["category"].as_array().unwrap() {
        assert_eq!(cursor_core::events::classify_category(row["model"].as_str()), row["output"].as_str().unwrap());
    }
    for case in root["parse"].as_array().unwrap() {
        let got = cursor_core::events::parse_page(&case["payload"]);
        assert_eq!(got["total_count"].as_i64(), case["total_count"].as_i64(), "{}", case["name"].as_str().unwrap_or(""));
        let events = got["events"].as_array().unwrap();
        let expected = case["events"].as_array().unwrap();
        assert_eq!(events.len(), expected.len());
        for (got, exp) in events.iter().zip(expected) {
            for key in ["id", "model", "kind", "user_email", "owning_user"] {
                assert_eq!(got[key].as_str(), exp[key].as_str(), "{key}");
            }
            assert_eq!(got["timestamp_ms"].as_i64(), exp["timestamp_ms"].as_i64());
            assert_eq!(got["tokens"].as_i64(), exp["tokens"].as_i64());
            assert_eq!(got["charged_cents"].as_f64(), exp["charged_cents"].as_f64());
            assert_eq!(got["total_cents"].as_f64(), exp["total_cents"].as_f64());
            assert_eq!(got["is_headless"].as_bool(), exp["is_headless"].as_bool());
        }
    }
    for case in root["report"].as_array().unwrap() {
        let got = cursor_core::events::build_report(case["events"].as_array().unwrap(), &case["filter"], None, None);
        let exp = &case["expected"];
        for key in ["event_count", "total_tokens", "included_count", "free_count", "on_demand_count", "headless_count"] {
            assert_eq!(got[key].as_i64(), exp[key].as_i64(), "{} {key}", case["name"].as_str().unwrap_or(""));
        }
        assert_eq!(got["total_cents"].as_f64(), exp["total_cents"].as_f64());
        assert_eq!(got["has_cost"].as_bool(), exp["has_cost"].as_bool());
        assert_eq!(got["daily"], exp["daily"]);
        assert_eq!(got["models"], exp["models"]);
    }
    for row in root["times"].as_array().unwrap() {
        let ts = row["timestamp_ms"].as_i64().unwrap();
        assert_eq!(cursor_core::events::format_time(ts), row["time"].as_str().unwrap());
        assert_eq!(cursor_core::events::event_date(ts), row["date"].as_str().unwrap());
        assert_eq!(cursor_core::events::event_hour(ts), row["hour"].as_str().unwrap());
    }
    let chart = fixture("usage_chart_cases.json");
    for row in chart["model_labels"].as_array().unwrap() {
        assert_eq!(cursor_core::events::chart_model_label(row["input"].as_str()), row["output"].as_str().unwrap());
    }
    for case in chart["cases"].as_array().unwrap() {
        let hidden = case["hidden_models"].as_array().unwrap().iter().map(|v| v.as_str().unwrap().to_string()).collect::<Vec<_>>();
        let got = cursor_core::events::build_chart(case["events"].as_array().unwrap(), case["hourly"].as_bool().unwrap(), &hidden);
        let exp = &case["expected"];
        assert_eq!(got["hourly"].as_bool(), exp["hourly"].as_bool());
        assert_eq!(got["caption"].as_str(), exp["caption"].as_str());
        assert_eq!(got["models"], exp["models"]);
        if exp.get("buckets").is_some() { assert_eq!(&got["buckets"], &exp["buckets"]); }
        else {
            let buckets = got["buckets"].as_array().unwrap();
            assert_eq!(buckets.len() as i64, exp["bucket_count"].as_i64().unwrap());
            assert_eq!(buckets[0]["key"].as_str(), exp["first_key"].as_str());
            assert_eq!(buckets.last().unwrap()["key"].as_str(), exp["last_key"].as_str());
            let nonzero: Vec<_> = buckets.iter().filter(|b| b["tokens"].as_i64().unwrap_or(0) != 0 || b["cents"].as_f64().unwrap_or(0.0) != 0.0 || b["count"].as_i64().unwrap_or(0) != 0).cloned().collect();
            assert_eq!(Value::Array(nonzero), exp["nonzero"]);
        }
    }
    let cny = &root["cny_spend"];
    for row in cny["defaults"].as_array().unwrap() {
        assert_eq!(cursor_core::events::default_monthly_plan_usd(row["membership"].as_str()), row["output"].as_f64().unwrap());
    }
    for row in cny["format"].as_array().unwrap() {
        assert_eq!(cursor_core::events::format_cny(row["cny"].as_f64()), row["output"].as_str().unwrap());
    }
    for row in root["discount_format"].as_array().unwrap() {
        assert_eq!(cursor_core::events::format_discount(row["cny"].as_f64().unwrap(), row["cents"].as_f64().unwrap(), row["rate"].as_f64().unwrap(), row["kind"].as_str()), row["output"].as_str().unwrap());
    }
    for case in cny["cases"].as_array().unwrap() {
        let events = normalize_events(case["events"].as_array().unwrap());
        let got = cursor_core::events::build_report(&events, &case["filter"], Some(&case["spend"]), case.get("allocation"));
        let exp = &case["expected"];
        for key in ["monthly_plan_usd", "usd_cny_rate", "plan_cny", "on_demand_cny", "total_cny"] {
            assert!((got[key].as_f64().unwrap() - exp[key].as_f64().unwrap()).abs() < 0.001, "{} {key}", case["name"].as_str().unwrap_or(""));
        }
        let event_cny: Vec<f64> = got["events"].as_array().unwrap().iter().map(|ev| ev["allocated_cny"].as_f64().unwrap_or(0.0)).collect();
        let expected_cny: Vec<f64> = exp["event_cny"].as_array().unwrap().iter().map(|v| v.as_f64().unwrap()).collect();
        assert_eq!(event_cny.len(), expected_cny.len());
        for (got, want) in event_cny.iter().zip(expected_cny) { assert!((got - want).abs() < 0.001); }
        for (got, want) in got["models"].as_array().unwrap().iter().zip(exp["models"].as_array().unwrap()) {
            assert_eq!(got["name"].as_str(), want["name"].as_str());
            assert!((got["cny"].as_f64().unwrap() - want["cny"].as_f64().unwrap()).abs() < 0.001);
        }
    }
    let compare = fixture("account_compare_cases.json");
    for row in compare["channel"].as_array().unwrap() {
        assert_eq!(cursor_core::events::sanitize_channel(row["input"].as_str()), row["output"].as_str().unwrap());
        assert_eq!(cursor_core::events::channel_label(row["input"].as_str()), row["label"].as_str().unwrap());
    }
    for case in compare["cases"].as_array().unwrap() {
        let items: Vec<Value> = case["items"].as_array().unwrap().iter().map(|item| {
            let mut copy = item.clone();
            copy["events"] = Value::Array(normalize_events(item["events"].as_array().unwrap()));
            copy
        }).collect();
        let got = cursor_core::events::build_account_compare(&items, compare["now_ms"].as_i64());
        let exp = &case["expected"];
        for (got, want) in got["rows"].as_array().unwrap().iter().zip(exp["rows"].as_array().unwrap()) {
            assert_eq!(got["account_id"].as_str(), want["account_id"].as_str());
            assert_eq!(got["channel"].as_str(), want["channel"].as_str());
            assert_eq!(got["window_source"].as_str(), want["window_source"].as_str());
            for key in ["window_days", "plan_cny", "daily_holding_cny", "window_plan_cny", "on_demand_cny", "total_cny", "cny_per_million", "cny_per_request"] {
                assert!((got[key].as_f64().unwrap() - want[key].as_f64().unwrap()).abs() < 0.001, "{key}");
            }
            assert_eq!(got["event_count"].as_i64(), want["event_count"].as_i64());
            assert_eq!(got["total_tokens"].as_i64(), want["total_tokens"].as_i64());
        }
    }
    for row in root["cost_format"].as_array().unwrap() {
        let event = serde_json::json!({
            "kind": row["kind"],
            "charged_cents": row.get("charged_cents").cloned().unwrap_or(Value::Null),
            "total_cents": row.get("total_cents").cloned().unwrap_or(Value::Null),
        });
        assert_eq!(cursor_core::events::format_cost(&event), row["output"].as_str().unwrap());
    }
}

#[test]
fn password_login_cases() {
    let root = fixture("password_login_cases.json");
    for row in root["sanitize_email"].as_array().unwrap() {
        assert_eq!(cursor_core::login::sanitize_email(row["input"].as_str()), row["output"].as_str().unwrap());
    }
    for row in root["default_label"].as_array().unwrap() {
        assert_eq!(
            cursor_core::login::default_label(row["email"].as_str().unwrap_or(""), row["existing"].as_str()),
            row["output"].as_str().unwrap()
        );
    }
    for row in root["cookies"].as_array().unwrap() {
        let cookies: Vec<(String, String)> = row["cookies"]
            .as_array()
            .unwrap()
            .iter()
            .map(|cookie| (cookie["name"].as_str().unwrap_or("").to_string(), cookie["value"].as_str().unwrap_or("").to_string()))
            .collect();
        assert_eq!(cursor_core::login::token_from_cookies(&cookies), row["token"].as_str().unwrap());
    }
    let auto = &root["autofill"];
    let script = cursor_core::login::autofill_script(auto["email"].as_str().unwrap_or(""), auto["password"].as_str().unwrap_or(""));
    for needle in auto["script_contains"].as_array().unwrap() {
        assert!(script.contains(needle.as_str().unwrap_or("")), "{script}");
    }
    for row in root["parse_paste"].as_array().unwrap() {
        let name = row["name"].as_str().unwrap_or("");
        let got = cursor_core::login::parse_account_paste(row["text"].as_str());
        let expected = row["items"].as_array().unwrap();
        assert_eq!(got.len(), expected.len(), "{name}");
        for (got, want) in got.iter().zip(expected) {
            assert_eq!(got.kind, want["kind"].as_str().unwrap(), "{name}");
            if want.get("email").is_some() {
                assert_eq!(got.email, want["email"].as_str().unwrap_or(""), "{name}");
            }
            if want.get("password").is_some() {
                assert_eq!(got.password, want["password"].as_str().unwrap_or(""), "{name}");
            }
            if want.get("token").is_some() {
                assert_eq!(got.token, want["token"].as_str().unwrap_or(""), "{name}");
            }
            if want.get("message").is_some() {
                assert_eq!(got.message, want["message"].as_str().unwrap_or(""), "{name}");
            }
        }
    }
    let link = &root["login_link"];
    assert_eq!(cursor_core::login::code_challenge(link["verifier"].as_str()), link["challenge"].as_str().unwrap());
    let login_url = cursor_core::login::login_deep_url(link["uuid"].as_str(), link["verifier"].as_str());
    assert_eq!(login_url, link["login_url"].as_str().unwrap());
    assert!(!login_url.contains(link["verifier"].as_str().unwrap_or("")));
    for row in root["callback"].as_array().unwrap() {
        assert_eq!(cursor_core::login::token_from_callback(row["input"].as_str()), row["token"].as_str().unwrap(), "{}", row["name"].as_str().unwrap_or(""));
    }
    for row in root["poll"].as_array().unwrap() {
        assert_eq!(cursor_core::login::token_from_poll(row), row["token"].as_str().unwrap(), "{}", row["name"].as_str().unwrap_or(""));
    }
    for row in root["profiles"].as_array().unwrap() {
        if row.get("email").is_some() || row.get("account_id").is_some() {
            assert_eq!(
                cursor_core::login::profile_key(row["email"].as_str(), row["account_id"].as_str()),
                row["key"].as_str().unwrap(),
                "{}",
                row["name"].as_str().unwrap_or("")
            );
        }
        if row.get("new_id").is_none() {
            continue;
        }
        let index = row["index"].as_object().cloned().unwrap_or_default();
        let assigned = cursor_core::login::assign_profile(&index, row["key"].as_str(), row["new_id"].as_str());
        assert_eq!(assigned["profile_id"].as_str(), row["profile_id"].as_str());
        let key = row["key"].as_str().unwrap_or("");
        let stored = assigned["index"].get(key).and_then(Value::as_str).unwrap_or("");
        assert_eq!(stored, row["stored"].as_str().unwrap_or(""));
    }
}

#[test]
fn account_validity_cases() {
    let root = fixture("account_validity_cases.json");
    for row in root["kind"].as_array().unwrap() {
        assert_eq!(cursor_core::validity::sanitize_kind(row["input"].as_str()), row["output"].as_str().unwrap());
    }
    for row in root["compute_end"].as_array().unwrap() {
        assert_eq!(cursor_core::validity::compute_end_iso(row["start"].as_str(), row["days"].as_i64().unwrap(), row["hours"].as_i64().unwrap()).as_deref(), row["end"].as_str(), "{}", row["name"].as_str().unwrap_or(""));
    }
    for row in root["override"].as_array().unwrap() {
        let got = cursor_core::validity::apply_end_override(&row["account"], row["api_end"].as_str(), row["now"].as_str(), 26);
        assert_eq!(got["billing_cycle_end"].as_str(), row["expected_end"].as_str());
        assert_eq!(got["days_remaining"].as_i64(), row["expected_days_remaining"].as_i64());
        assert_eq!(got["overridden"].as_bool(), row["overridden"].as_bool());
    }
}

#[test]
fn account_sync_merge_cases() {
    let root = fixture("account_sync_cases.json");
    for case in root["merge"].as_array().unwrap() {
        if case.get("apply").and_then(Value::as_bool) == Some(true) {
            continue;
        }
        let name = case["name"].as_str().unwrap_or("");
        let merged = cursor_core::sync::merge_snapshots(&case["local"], &case["remote"]);
        let expected = &case["expected"];
        assert_eq!(merged["active_account_id"].as_str(), expected["active_account_id"].as_str(), "{name}.active");
        let ids: Vec<&str> = merged["accounts"].as_array().unwrap().iter().filter_map(|a| a["id"].as_str()).collect();
        let want: Vec<&str> = expected["ids"].as_array().unwrap().iter().filter_map(Value::as_str).collect();
        assert_eq!(ids, want, "{name}.ids");
        for (id, label) in expected["labels"].as_object().unwrap() {
            assert_eq!(account(&merged, id)["label"].as_str(), label.as_str(), "{name}.label.{id}");
        }
        for (id, token) in expected["tokens"].as_object().unwrap() {
            assert_eq!(account(&merged, id)["token"].as_str(), token.as_str(), "{name}.token.{id}");
        }
        if let Some(emails) = expected.get("emails").and_then(Value::as_object) {
            for (id, email) in emails {
                assert_eq!(account(&merged, id)["email"].as_str(), email.as_str(), "{name}.email.{id}");
            }
        }
        if let Some(passwords) = expected.get("passwords").and_then(Value::as_object) {
            for (id, password) in passwords {
                assert_eq!(account(&merged, id)["password"].as_str(), password.as_str(), "{name}.password.{id}");
            }
        }
        let deleted: Vec<&str> = merged["deleted"].as_array().unwrap().iter().filter_map(|d| d["id"].as_str()).collect();
        let want_deleted: Vec<&str> = expected["deleted_ids"].as_array().unwrap().iter().filter_map(Value::as_str).collect();
        assert_eq!(deleted, want_deleted, "{name}.deleted");
        if let Some(exp_settings) = expected.get("settings") {
            let got = &merged["settings"];
            assert!(got.is_object(), "{name}.settings");
            assert_eq!(got["refresh_interval_minutes"].as_i64(), exp_settings["refresh_interval_minutes"].as_i64(), "{name}.refresh");
            assert_eq!(got["tray_display_mode"].as_str(), exp_settings["tray_display_mode"].as_str(), "{name}.tray");
            assert_eq!(got["notify_enabled"].as_bool(), exp_settings["notify_enabled"].as_bool(), "{name}.notify");
            assert_eq!(got["monthly_plan_usd"].as_f64(), exp_settings["monthly_plan_usd"].as_f64(), "{name}.plan");
            if let Some(thresholds) = exp_settings.get("alert_thresholds") {
                assert_eq!(&got["alert_thresholds"], thresholds, "{name}.thresholds");
            }
            if let Some(risk) = exp_settings.get("notify_exhaustion_risk") {
                assert_eq!(got["notify_exhaustion_risk"].as_bool(), risk.as_bool(), "{name}.risk");
            }
            if let Some(rate) = exp_settings.get("usd_cny_rate") {
                assert_eq!(got["usd_cny_rate"].as_f64(), rate.as_f64(), "{name}.rate");
            }
        }
        if let Some(remaining) = expected.get("remaining").and_then(Value::as_object) {
            for (id, value) in remaining {
                assert_eq!(account(&merged, id)["last_remaining"].as_f64(), value.as_f64(), "{name}.remaining.{id}");
            }
        }
        if let Some(ends) = expected.get("billing_cycle_end").and_then(Value::as_object) {
            for (id, value) in ends {
                assert_eq!(account(&merged, id)["billing_cycle_end"].as_str(), value.as_str(), "{name}.cycle.{id}");
            }
        }
        if let Some(history) = expected.get("usage_history_ts").and_then(Value::as_object) {
            for (id, value) in history {
                let got: Vec<f64> = usage_row(&merged, id)["history"].as_array().unwrap().iter().filter_map(|p| p["ts"].as_f64()).collect();
                let want: Vec<f64> = value.as_array().unwrap().iter().filter_map(Value::as_f64).collect();
                assert_eq!(got, want, "{name}.history.{id}");
            }
        }
        if let Some(events) = expected.get("usage_event_ids").and_then(Value::as_object) {
            for (id, value) in events {
                assert_eq!(sorted_ids(&usage_row(&merged, id)["events"]), sorted_strings(value), "{name}.events.{id}");
            }
        }
        if let Some(events) = expected.get("usage_team_event_ids").and_then(Value::as_object) {
            for (id, value) in events {
                assert_eq!(sorted_ids(&usage_row(&merged, id)["team_events"]), sorted_strings(value), "{name}.team.{id}");
            }
        }
    }
}

fn account<'a>(merged: &'a Value, id: &str) -> &'a Value {
    merged["accounts"].as_array().unwrap().iter().find(|a| a["id"].as_str() == Some(id)).unwrap_or(&Value::Null)
}

fn usage_row<'a>(merged: &'a Value, id: &str) -> &'a Value {
    merged["usage"].as_array().unwrap().iter().find(|row| row["account_id"].as_str() == Some(id)).unwrap_or(&Value::Null)
}

fn sorted_ids(events: &Value) -> Vec<String> {
    let mut ids: Vec<String> = events.as_array().unwrap().iter().filter_map(|ev| ev["id"].as_str().map(str::to_string)).collect();
    ids.sort();
    ids
}

fn sorted_strings(value: &Value) -> Vec<String> {
    let mut ids: Vec<String> = value.as_array().unwrap().iter().filter_map(|v| v.as_str().map(str::to_string)).collect();
    ids.sort();
    ids
}

#[test]
fn sand_usage_cases() {
    for case in fixture("sand_usage_cases.json").as_array().unwrap() {
        let name = case["name"].as_str().unwrap_or("");
        let now = case["now"].as_str();
        let got = parse_sand_usage_status(&case["payload"], now);
        let exp = &case["expected"];
        if !exp["shows_grok_bot"].as_bool().unwrap_or(false) {
            assert!(got.is_none(), "{name}");
            if exp.get("ring_percent_used").is_some_and(Value::is_number) {
                let mut snap = json!({
                    "remaining_percent": 100.0,
                    "is_x_subscription": true,
                    "membership_type": "Premium",
                });
                apply_sand_usage(&mut snap, &case["payload"], now);
                assert_eq!(snap["used_percent"].as_f64(), exp["ring_percent_used"].as_f64(), "{name}.ring");
                assert_eq!(snap["remaining_percent"].as_f64(), exp["ring_remaining_percent"].as_f64(), "{name}.remaining");
                assert_eq!(snap["usage_reset_at"].as_str(), exp["usage_reset_at"].as_str(), "{name}.reset");
                assert!(snap["grok_bot_percent_used"].is_null(), "{name}.grok");
                assert_eq!(snap["is_x_subscription"].as_bool(), Some(true), "{name}.x");
            }
            continue;
        }
        let got = got.unwrap_or_else(|| panic!("{name}"));
        assert_eq!(got["percent_used"].as_f64(), exp["percent_used"].as_f64(), "{name}.percent");
        assert_eq!(got["remaining_percent"].as_f64(), exp["remaining_percent"].as_f64(), "{name}.remaining");
        assert_eq!(got["period_start"].as_str(), exp["period_start"].as_str(), "{name}.start");
        assert_eq!(got["reset_at"].as_str(), exp["reset_at"].as_str(), "{name}.reset");
        assert_eq!(got["days_remaining"].as_i64(), exp["days_remaining"].as_i64(), "{name}.days");
    }
}

#[test]
fn update_release_cases() {
    let root = fixture("update_release_cases.json");
    for row in root["extract_sha"].as_array().unwrap() {
        assert_eq!(cursor_core::update::extract_sha(row["text"].as_str()), row["sha"].as_str().unwrap_or(""), "{}", row["name"].as_str().unwrap_or(""));
    }
    for row in root["same_sha"].as_array().unwrap() {
        assert_eq!(cursor_core::update::same_sha(row["left"].as_str(), row["right"].as_str()), row["same"].as_bool().unwrap());
    }
    for row in root["normalize_sha"].as_array().unwrap() {
        assert_eq!(cursor_core::update::normalize_sha(row["input"].as_str()), row["output"].as_str().unwrap_or(""));
    }
    for row in root["allowed_urls"].as_array().unwrap() {
        assert_eq!(cursor_core::update::is_allowed_download_url(row["url"].as_str()), row["ok"].as_bool().unwrap(), "{}", row["url"].as_str().unwrap_or(""));
    }
    for row in root["packaged_windows"].as_array().unwrap() {
        assert_eq!(cursor_core::update::looks_packaged_windows(row["path"].as_str()), row["ok"].as_bool().unwrap(), "{}", row["path"].as_str().unwrap_or(""));
    }
    for row in root["packaged_macos"].as_array().unwrap() {
        assert_eq!(cursor_core::update::looks_packaged_macos(row["path"].as_str()), row["ok"].as_bool().unwrap(), "{}", row["path"].as_str().unwrap_or(""));
    }
    for row in root["parse_release"].as_array().unwrap() {
        let parsed = cursor_core::update::parse_release(&row["json"]).unwrap();
        let exp = &row["expected"];
        assert_eq!(parsed.tag, exp["tag"].as_str().unwrap_or(""), "{}", row["name"].as_str().unwrap_or(""));
        if exp.get("version").is_some() {
            assert_eq!(parsed.version, exp["version"].as_str().unwrap_or(""));
        }
        assert_eq!(parsed.commit_sha, exp["sha"].as_str().unwrap_or(""));
        assert_eq!(parsed.page_url, exp["page_url"].as_str().unwrap_or(""));
        let windows = cursor_core::update::find_asset(&parsed, cursor_core::update::WINDOWS_ASSET_NAME);
        let macos = cursor_core::update::find_asset(&parsed, cursor_core::update::MACOS_ASSET_NAME);
        assert_eq!(windows.map(|a| a.url.as_str()), exp["windows_url"].as_str());
        assert_eq!(macos.map(|a| a.url.as_str()), exp["macos_url"].as_str());
        assert_eq!(windows.map(|a| a.id), exp["windows_asset_id"].as_i64());
        assert_eq!(macos.map(|a| a.id), exp["macos_asset_id"].as_i64());
        assert_eq!(windows.map(|a| a.updated_at.as_str()), exp["windows_updated_at"].as_str());
    }
    for row in root["parse_release_page"].as_array().unwrap() {
        let parsed = cursor_core::update::parse_release_page(row["html"].as_str()).unwrap();
        assert_eq!(parsed.commit_sha, row["sha"].as_str().unwrap_or(""));
        assert_eq!(cursor_core::update::find_asset(&parsed, cursor_core::update::WINDOWS_ASSET_NAME).map(|a| a.url.as_str()), row["windows_url"].as_str());
        assert_eq!(cursor_core::update::find_asset(&parsed, cursor_core::update::MACOS_ASSET_NAME).map(|a| a.url.as_str()), row["macos_url"].as_str());
    }
    for row in root["parse_tag_ref"].as_array().unwrap() {
        assert_eq!(cursor_core::update::parse_tag_ref_sha(&row["json"]), row["sha"].as_str().unwrap_or(""));
    }
    let sample = cursor_core::update::parse_release(&root["parse_release"][0]["json"]).unwrap();
    for row in root["evaluate"].as_array().unwrap() {
        let mut release = sample.clone();
        if row["clear_release_sha"].as_bool().unwrap_or(false) {
            release.commit_sha.clear();
        }
        if let Some(tag) = row["tag"].as_str() {
            release.tag = tag.to_string();
            release.version = row["version"].as_str().unwrap_or("").to_string();
        }
        let asset = if row["asset"].as_str() == Some("macos") { cursor_core::update::MACOS_ASSET_NAME } else { cursor_core::update::WINDOWS_ASSET_NAME };
        let decision = cursor_core::update::evaluate(&release, asset, row["current_sha"].as_str().unwrap_or(""), row["installed_sha"].as_str().unwrap_or(""), row["installed_asset_id"].as_i64().unwrap_or(0), row["current_version"].as_str());
        assert_eq!(decision.available, row["available"].as_bool().unwrap(), "{}", row["name"].as_str().unwrap_or(""));
        assert_eq!(decision.up_to_date, row["up_to_date"].as_bool().unwrap(), "{}", row["name"].as_str().unwrap_or(""));
    }
    for row in root["extract_version"].as_array().unwrap() {
        assert_eq!(cursor_core::update::product_version_from_release(row["tag"].as_str(), row["body"].as_str()), row["version"].as_str().unwrap_or(""));
    }
    for row in root["compare_versions"].as_array().unwrap() {
        assert_eq!(cursor_core::update::compare_product_versions(row["left"].as_str(), row["right"].as_str()), row["cmp"].as_i64().unwrap() as i32);
    }
    for row in root["choose_release"].as_array().unwrap() {
        let official_tag = row["official_tag"].as_str().unwrap_or("");
        let official = (!official_tag.is_empty()).then(|| cursor_core::update::AppRelease {
            tag: official_tag.to_string(),
            version: row["official_version"].as_str().unwrap_or("").to_string(),
            body: String::new(),
            commit_sha: String::new(),
            page_url: String::new(),
            published_at: String::new(),
            assets: sample.assets.clone(),
        });
        let rolling = cursor_core::update::AppRelease {
            tag: row["rolling_tag"].as_str().unwrap_or("latest").to_string(),
            version: String::new(),
            body: String::new(),
            commit_sha: String::new(),
            page_url: String::new(),
            published_at: String::new(),
            assets: sample.assets.clone(),
        };
        let chosen = cursor_core::update::choose_release(official.as_ref(), Some(&rolling), row["current_version"].as_str());
        let label = if official.as_ref().is_some_and(|item| chosen.as_ref().is_some_and(|got| got.tag == item.tag)) { "official" } else { "rolling" };
        assert_eq!(label, row["choice"].as_str().unwrap_or(""), "{}", row["name"].as_str().unwrap_or(""));
    }
}

#[test]
fn update_asset_and_pending_cases() {
    use cursor_core::update::*;
    assert_eq!(asset_name_candidates(WINDOWS_LIGHT_ASSET_NAME), vec![WINDOWS_LIGHT_ASSET_NAME, WINDOWS_ASSET_NAME]);
    assert_eq!(asset_name_candidates(WINDOWS_ASSET_NAME), vec![WINDOWS_ASSET_NAME]);
    assert_eq!(preferred_windows_asset_name(true), WINDOWS_LIGHT_ASSET_NAME);
    assert_eq!(preferred_windows_asset_name(false), WINDOWS_ASSET_NAME);
    let light = AppReleaseAsset { id: 9, name: WINDOWS_LIGHT_ASSET_NAME.into(), url: asset_download_url(WINDOWS_LIGHT_ASSET_NAME, None), size: 3_000_000, updated_at: String::new() };
    let full = AppReleaseAsset { id: 8, name: WINDOWS_ASSET_NAME.into(), url: asset_download_url(WINDOWS_ASSET_NAME, None), size: 60_000_000, updated_at: String::new() };
    let release = AppRelease { tag: "v2.1.0".into(), version: "2.1.0".into(), body: String::new(), commit_sha: "518192b000000000000000000000000000000000".into(), page_url: String::new(), published_at: String::new(), assets: vec![light, full.clone()] };
    assert_eq!(find_preferred_asset(&release, WINDOWS_LIGHT_ASSET_NAME).map(|a| a.id), Some(9));
    assert_eq!(find_preferred_asset(&release, WINDOWS_ASSET_NAME).map(|a| a.id), Some(8));
    let missing_light = AppRelease { assets: vec![full.clone()], ..release.clone() };
    assert_eq!(find_preferred_asset(&missing_light, WINDOWS_LIGHT_ASSET_NAME).map(|a| a.id), Some(8));
    let oversized = AppReleaseAsset { id: 11, name: WINDOWS_LIGHT_ASSET_NAME.into(), url: asset_download_url(WINDOWS_LIGHT_ASSET_NAME, None), size: MAX_LIGHT_ZIP_BYTES + 1, updated_at: String::new() };
    let oversized_release = AppRelease { assets: vec![oversized, full], ..release.clone() };
    assert_eq!(find_preferred_asset(&oversized_release, WINDOWS_LIGHT_ASSET_NAME).map(|a| a.id), Some(8));
    let decision = evaluate(&release, WINDOWS_LIGHT_ASSET_NAME, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "", 0, Some("2.0.0"));
    assert!(decision.available);
    assert_eq!(decision.asset.as_ref().map(|a| a.id), Some(9));

    let legacy = AppRelease {
        tag: "latest".into(),
        version: String::new(),
        body: String::new(),
        commit_sha: "518192b000000000000000000000000000000000".into(),
        page_url: String::new(),
        published_at: String::new(),
        assets: vec![AppReleaseAsset { id: 7, name: LEGACY_WINDOWS_ASSET_NAME.into(), url: asset_download_url(LEGACY_WINDOWS_ASSET_NAME, None), size: 0, updated_at: String::new() }],
    };
    assert!(find_preferred_asset(&legacy, WINDOWS_ASSET_NAME).is_none());
    let missing = evaluate(&legacy, WINDOWS_ASSET_NAME, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "", 0, None);
    assert!(!missing.available);
    assert_eq!(missing.message, "最新发布没有本平台安装包");

    assert_eq!(display_version(Some(""), "2.1.0"), "2.1.0");
    assert_eq!(display_version(Some("518192b000000000000000000000000000000000"), "2.1.0"), "2.1.0 (518192b)");
    assert_eq!(sha_from_informational_version(Some("2.1.0+518192b")), "518192b");
    assert_eq!(sha_from_informational_version(Some("2.1.0")), "");
    assert_eq!(normalize_product_version(Some("v2.1.0")), "2.1.0");
    assert!(should_fallback_from_api(403));
    assert!(should_fallback_from_api(429));
    assert!(!should_fallback_from_api(400));

    assert!(remembered_install_after_helper("518192b000000000000000000000000000000000", 99, false).is_none());
    let remembered = remembered_install_after_helper("518192B000000000000000000000000000000000", 99, true).unwrap();
    assert_eq!(remembered.sha, "518192b000000000000000000000000000000000");
    assert_eq!(remembered.asset_id, 99);
    let pending_body = pending_install_json("0ad980084152a814bdaefeb3bb162184db07d64a", 7);
    let pending = read_pending_install_json(&pending_body.to_string()).unwrap();
    assert_eq!(pending.sha, "0ad980084152a814bdaefeb3bb162184db07d64a");
    assert_eq!(pending.asset_id, 7);
    assert!(confirm_pending_install("eb9a0a2a0512609588b3458d87443971eed07ec3", Some(&pending)).is_none());
    assert!(pending_install_failed("eb9a0a2a0512609588b3458d87443971eed07ec3", Some(&pending)));
    assert!(!pending_install_failed("", Some(&pending)));
    assert_eq!(confirm_pending_install("0ad9800", Some(&pending)).unwrap().sha, pending.sha);

    let now = "2026-09-14T12:00:00Z";
    assert!(should_auto_check("", now, false));
    assert!(should_auto_check(&now_iso(parse_iso(now).unwrap() - 13 * 3600), now, false));
    assert!(!should_auto_check(&now_iso(parse_iso(now).unwrap() - 3600), now, false));
    assert!(should_auto_check(&now_iso(parse_iso(now).unwrap() - 3600), now, true));
}
