use base64::Engine;
use serde_json::Value;
use sha2::{Digest, Sha256};

const JUNK: [char; 7] = ['\u{2026}', '\u{2022}', '\u{FEFF}', '\u{200B}', '\u{200C}', '\u{200D}', '\u{00A0}'];

#[derive(Debug, PartialEq, Eq)]
pub struct TokenError {
    pub message: String,
}

pub fn normalize(raw: &str) -> Result<String, TokenError> {
    let mut value = raw.trim().trim_matches(['"', '\'']).to_string();
    if value.is_empty() {
        return Ok(String::new());
    }
    for ch in JUNK {
        value = value.replace(ch, "");
    }
    if let Some(cookie) = cookie_value(&value) {
        value = cookie;
    } else if value.to_ascii_lowercase().starts_with("workoscursorsessiontoken=") {
        value = value.split_once('=').map(|(_, rest)| rest).unwrap_or("").trim().to_string();
    }
    value = value.chars().filter(|c| !c.is_whitespace()).collect();
    if !(value.contains("%3A%3A") || value.contains("%3a%3a")) {
        if let Some(index) = value.find("::") {
            value.replace_range(index..index + 2, "%3A%3A");
        } else if looks_like_jwt(&value) {
            let uid = extract_user_id(&value);
            if !uid.is_empty() {
                value = format!("{uid}%3A%3A{value}");
            }
        }
    }
    if value.chars().any(|c| (c as u32) > 255) || value.contains('\u{FFFD}') {
        return Err(TokenError {
            message: "读到的 Token 已损坏（常见于 Chrome Cookie 解密失败，不是复制漏了）。请再点一次「导入」，或改用 Firefox，或在开发者工具里完整复制 WorkosCursorSessionToken。".to_string(),
        });
    }
    Ok(value)
}

pub fn variants(token: &str) -> Vec<String> {
    let mut list = Vec::new();
    let mut add = |value: &str| {
        let text = value.trim();
        if !text.is_empty() && !list.iter().any(|item: &String| item == text) {
            list.push(text.to_string());
        }
    };
    let raw = token.trim();
    if let Ok(normalized) = normalize(raw) {
        add(&normalized);
    }
    add(raw);
    let mut jwt = raw.to_string();
    if let Some(index) = raw.find("%3A%3A") {
        jwt = raw[index + 6..].to_string();
        add(&raw.replacen("%3A%3A", "::", 1));
    } else if let Some(index) = raw.find("%3a%3a") {
        jwt = raw[index + 6..].to_string();
        add(&raw.replacen("%3a%3a", "::", 1));
    } else if let Some(index) = raw.find("::") {
        jwt = raw[index + 2..].to_string();
        add(&raw.replacen("::", "%3A%3A", 1));
    }
    if looks_like_jwt(&jwt) {
        add(&jwt);
        if let Some(sub) = jwt_string(&jwt, "sub") {
            if !sub.is_empty() {
                let uid = sub.rsplit('|').next().unwrap_or("");
                add(&format!("{uid}%3A%3A{jwt}"));
                add(&format!("{uid}::{jwt}"));
                if sub != uid {
                    add(&format!("{sub}%3A%3A{jwt}"));
                    add(&format!("{sub}::{jwt}"));
                }
            }
        }
    }
    if list.len() > 4 { list.truncate(4); }
    list
}

pub fn account_id(token: &str) -> String {
    let value = normalize(token).unwrap_or_else(|_| token.trim().to_string());
    if value.is_empty() {
        return String::new();
    }
    let mut jwt = value.as_str();
    let mut prefix = "";
    for sep in ["%3A%3A", "%3a%3a", "::"] {
        if let Some(index) = value.find(sep) {
            prefix = &value[..index];
            jwt = &value[index + sep.len()..];
            break;
        }
    }
    if looks_like_jwt(jwt) {
        let uid = extract_user_id(jwt);
        if !uid.is_empty() {
            return safe_account_id(&uid);
        }
    }
    if !prefix.trim().is_empty() {
        return safe_account_id(prefix.rsplit('|').next().unwrap_or(prefix));
    }
    if looks_like_jwt(&value) {
        let uid = extract_user_id(&value);
        if !uid.is_empty() {
            return safe_account_id(&uid);
        }
    }
    let digest = Sha256::digest(value.as_bytes());
    let hex: String = digest.iter().take(8).map(|b| format!("{b:02x}")).collect();
    format!("tok_{hex}")
}

pub fn safe_account_id(value: &str) -> String {
    let mut cleaned = String::new();
    for ch in value.chars() {
        if ch.is_ascii_alphanumeric() || matches!(ch, '.' | '_' | '-') {
            cleaned.push(ch);
        } else {
            cleaned.push('_');
        }
    }
    let cleaned = cleaned.trim_matches(['.', '_', '-']).to_string();
    let cleaned = if cleaned.is_empty() { "account".to_string() } else { cleaned };
    cleaned.chars().take(80).collect()
}

pub fn looks_like_jwt(value: &str) -> bool {
    let parts: Vec<&str> = value.split('.').collect();
    parts.len() == 3 && parts.iter().all(|part| !part.is_empty())
}

pub fn jwt_type(token: &str) -> String {
    jwt_claim(token, "type").trim().to_ascii_lowercase()
}

pub fn can_write_to_cursor(token: &str) -> bool {
    let jwt = extract_jwt(token);
    looks_like_jwt(&jwt) && jwt_type(&jwt) != "web"
}

pub fn extract_jwt(token: &str) -> String {
    let mut value = token.trim().to_string();
    if value.is_empty() {
        return String::new();
    }
    if let Ok(normalized) = normalize(&value) {
        value = normalized;
    }
    let mut jwt = value.as_str();
    for sep in ["%3A%3A", "%3a%3a", "::"] {
        if let Some(index) = value.find(sep) {
            jwt = &value[index + sep.len()..];
            break;
        }
    }
    if looks_like_jwt(jwt) {
        return jwt.to_string();
    }
    if looks_like_jwt(&value) { value } else { String::new() }
}

fn extract_user_id(jwt: &str) -> String {
    let sub = jwt_subject(jwt);
    sub.rsplit('|').next().unwrap_or("").to_string()
}

fn jwt_subject(token: &str) -> String {
    let jwt = if looks_like_jwt(token) { token.to_string() } else { extract_jwt(token) };
    jwt_string(&jwt, "sub").unwrap_or_default()
}

fn jwt_claim(token: &str, name: &str) -> String {
    let jwt = if looks_like_jwt(token) { token.to_string() } else { extract_jwt(token) };
    jwt_string(&jwt, name).unwrap_or_default()
}

fn jwt_string(jwt: &str, name: &str) -> Option<String> {
    let payload = jwt_payload(jwt)?;
    let value = payload.get(name)?;
    match value {
        Value::String(text) => Some(text.clone()),
        Value::Null => None,
        other => Some(other.to_string()),
    }
}

fn jwt_payload(jwt: &str) -> Option<serde_json::Map<String, Value>> {
    let part = jwt.split('.').nth(1)?;
    let mut text = part.replace('-', "+").replace('_', "/");
    while text.len() % 4 != 0 {
        text.push('=');
    }
    let bytes = base64::engine::general_purpose::STANDARD.decode(text).ok()?;
    let json = String::from_utf8(bytes).ok()?;
    serde_json::from_str::<Value>(&json).ok()?.as_object().cloned()
}

fn cookie_value(value: &str) -> Option<String> {
    let lower = value.to_ascii_lowercase();
    let marker = "workoscursorsessiontoken=";
    let mut search_from = 0;
    while let Some(found) = lower[search_from..].find(marker) {
        let index = search_from + found;
        let prev = if index == 0 { None } else { value.as_bytes().get(index - 1).copied() };
        if index == 0 || prev.is_some_and(|b| b == b';' || b.is_ascii_whitespace()) {
            let rest = &value[index + marker.len()..];
            let end = rest.find(|c: char| c == ';' || c.is_whitespace()).unwrap_or(rest.len());
            return Some(rest[..end].trim().to_string());
        }
        search_from = index + marker.len();
    }
    None
}
