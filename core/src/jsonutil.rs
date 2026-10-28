use serde_json::Value;

static NULL: Value = Value::Null;

pub fn null() -> &'static Value {
    &NULL
}

pub fn has(value: &Value, key: &str) -> bool {
    value.as_object().is_some_and(|o| o.contains_key(key))
}

pub fn get<'a>(value: &'a Value, key: &str) -> &'a Value {
    value.get(key).unwrap_or(null())
}

pub fn child<'a>(value: &'a Value, key: &str) -> &'a Value {
    match value.get(key) {
        Some(value) if value.is_object() => value,
        _ => null(),
    }
}

pub fn is_empty(value: &Value) -> bool {
    match value {
        Value::Null => true,
        Value::Object(map) => map.is_empty(),
        _ => false,
    }
}

pub fn as_string(value: &Value) -> Option<String> {
    match value {
        Value::String(text) => Some(text.clone()),
        Value::Number(n) => Some(n.to_string()),
        _ => None,
    }
}

pub fn as_f64(value: &Value) -> Option<f64> {
    match value {
        Value::Number(n) => n.as_f64().or_else(|| n.as_i64().map(|i| i as f64)).or_else(|| n.as_u64().map(|i| i as f64)),
        Value::String(text) => text.trim().parse::<f64>().ok(),
        _ => None,
    }
}

pub fn as_int(value: &Value) -> Option<i64> {
    as_f64(value).map(|n| n.round() as i64)
}

pub fn as_bool(value: &Value) -> bool {
    match value {
        Value::Bool(v) => *v,
        Value::Number(n) => n.as_f64().unwrap_or(0.0) != 0.0,
        Value::String(text) => matches!(text.as_str(), "1" | "true" | "yes"),
        _ => false,
    }
}

pub fn first_present<'a>(value: &'a Value, keys: &[&str]) -> &'a Value {
    for key in keys {
        if has(value, key) {
            return get(value, key);
        }
    }
    null()
}

pub fn opt_bool(value: &Value, keys: &[&str]) -> Option<bool> {
    for key in keys {
        if has(value, key) {
            return Some(as_bool(get(value, key)));
        }
    }
    None
}
