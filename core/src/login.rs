use crate::token;
use base64::Engine;
use serde_json::{json, Map, Value};
use sha2::{Digest, Sha256};
use std::collections::BTreeMap;

pub const LOGIN_DEEP_URL_BASE: &str = "https://cursor.com/loginDeepControl";
const COOKIE_NAME: &str = "WorkosCursorSessionToken";
const CALLBACK_KEYS: [&str; 6] = [
    "workoscursorsessiontoken",
    "accesstoken",
    "access_token",
    "token",
    "cookie",
    "session_token",
];

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PasteItem {
    pub kind: String,
    pub token: String,
    pub email: String,
    pub password: String,
    pub message: String,
}

pub fn sanitize_email(raw: Option<&str>) -> String {
    let text = raw.unwrap_or("").trim().to_lowercase();
    if !looks_like_email(&text) || !email_shape(&text) {
        return String::new();
    }
    text
}

pub fn default_label(email: &str, existing_label: Option<&str>) -> String {
    let current = existing_label.unwrap_or("").trim();
    if !current.is_empty() {
        return current.to_string();
    }
    sanitize_email(Some(email))
}

pub fn token_from_cookies(cookies: &[(String, String)]) -> String {
    for (name, value) in cookies {
        if !name.eq_ignore_ascii_case(COOKIE_NAME) {
            continue;
        }
        let raw = value.trim();
        if raw.is_empty() {
            return String::new();
        }
        return token::normalize(raw).unwrap_or_else(|_| raw.to_string());
    }
    String::new()
}

pub fn autofill_script(email: &str, password: &str) -> String {
    let payload = serde_json::to_string(&json!({
        "email": sanitize_email(Some(email)),
        "password": password,
    }))
    .unwrap_or_else(|_| "{}".to_string());
    format!(
        "(function(){{\
const c={payload};\
function setNative(el,val){{\
if(!el)return;\
const proto=el.tagName==='TEXTAREA'?window.HTMLTextAreaElement.prototype:window.HTMLInputElement.prototype;\
const desc=Object.getOwnPropertyDescriptor(proto,'value');\
if(desc&&desc.set)desc.set.call(el,val);else el.value=val;\
el.dispatchEvent(new Event('input',{{bubbles:true}}));\
el.dispatchEvent(new Event('change',{{bubbles:true}}));\
}}\
if(document.querySelector('iframe[src*=\"challenges.cloudflare\"],iframe[src*=\"turnstile\"],input[autocomplete=\"one-time-code\"]'))\
return 'need-user';\
const emailEl=document.querySelector('input[type=\"email\"],input[name=\"email\"],input[autocomplete=\"username\"],input[autocomplete=\"email\"]');\
if(emailEl&&c.email)setNative(emailEl,c.email);\
const passEl=document.querySelector('input[type=\"password\"]');\
if(passEl&&c.password)setNative(passEl,c.password);\
const buttons=[...document.querySelectorAll('button,[type=submit]')];\
const go=buttons.find(b=>/continue|sign in|log in|登录|继续/i.test((b.innerText||b.value||'')));\
if(go&&!window.__cttClicked){{window.__cttClicked=Date.now();go.click();return 'clicked';}}\
return passEl&&c.password?'filled':'waiting';\
}})();"
    )
}

pub fn code_challenge(verifier: Option<&str>) -> String {
    let text = verifier.unwrap_or("").trim();
    if text.is_empty() {
        return String::new();
    }
    let digest = Sha256::digest(text.as_bytes());
    base64::engine::general_purpose::STANDARD
        .encode(digest)
        .trim_end_matches('=')
        .replace('+', "-")
        .replace('/', "_")
}

pub fn login_deep_url(uuid: Option<&str>, verifier: Option<&str>) -> String {
    let login_id = uuid.unwrap_or("").trim();
    let challenge = code_challenge(verifier);
    if login_id.is_empty() || challenge.is_empty() {
        return String::new();
    }
    format!(
        "{LOGIN_DEEP_URL_BASE}?challenge={}&uuid={}&mode=login",
        percent_encode(&challenge),
        percent_encode(login_id)
    )
}

pub fn token_from_callback(text: Option<&str>) -> String {
    let raw = text.unwrap_or("").trim();
    if raw.is_empty() {
        return String::new();
    }
    if !raw.contains("://") && !raw.to_ascii_lowercase().starts_with("cursor.com") {
        if !looks_like_session_token(raw) {
            return String::new();
        }
        return normalized_token(raw);
    }
    let (main, fragment) = match raw.find('#') {
        Some(hash) => (&raw[..hash], &raw[hash + 1..]),
        None => (raw, ""),
    };
    let query = main.find('?').map(|at| &main[at + 1..]).unwrap_or("");
    let mut found = query_map(query);
    for (key, value) in query_map(fragment) {
        found.entry(key).or_insert(value);
    }
    for key in CALLBACK_KEYS {
        if let Some(value) = found.get(key) {
            if !value.is_empty() && looks_like_session_token(value) {
                return normalized_token(value);
            }
        }
    }
    String::new()
}

pub fn token_from_poll(payload: &Value) -> String {
    let mut access = read_string(payload, "accessToken");
    if access.is_empty() {
        access = read_string(payload, "access_token");
    }
    if access.is_empty() {
        return String::new();
    }
    normalized_token(&access)
}

pub fn profile_key(email: Option<&str>, account_id: Option<&str>) -> String {
    let mail = sanitize_email(email);
    if !mail.is_empty() {
        return format!("email:{mail}");
    }
    let ident = account_id.unwrap_or("").trim();
    if ident.is_empty() || ident.chars().any(|ch| ch.is_whitespace() || ch == '/' || ch == '\\') {
        return String::new();
    }
    let ident: String = ident.chars().take(120).collect();
    format!("id:{ident}")
}

pub fn assign_profile(index: &Map<String, Value>, key: Option<&str>, new_id: Option<&str>) -> Value {
    let fresh = valid_profile_id(new_id.unwrap_or(""));
    let mut cleaned = clean_profile_index(index);
    if fresh.is_empty() {
        return json!({"index": cleaned, "profile_id": ""});
    }
    let name = canonical_profile_key(key.unwrap_or(""));
    if name.is_empty() {
        return json!({"index": cleaned, "profile_id": fresh});
    }
    let current = cleaned.get(&name).and_then(Value::as_str).filter(|value| !value.is_empty()).map(str::to_string);
    if let Some(current) = current {
        return json!({"index": cleaned, "profile_id": current});
    }
    cleaned.insert(name, json!(fresh.clone()));
    json!({"index": cleaned, "profile_id": fresh})
}

pub fn parse_account_paste(text: Option<&str>) -> Vec<PasteItem> {
    let raw = text.unwrap_or("").replace("\r\n", "\n").replace('\r', "\n");
    let mut items = Vec::new();
    let mut pending_email = String::new();
    for raw_line in raw.split('\n') {
        let line = raw_line.trim();
        if line.is_empty() {
            continue;
        }
        if let Some((email, password)) = labeled_both(line) {
            flush_pending(&mut items, &mut pending_email);
            items.push(item("credentials", "", &email, &password, ""));
            continue;
        }
        let email_only = labeled_email(line);
        if !email_only.is_empty() {
            flush_pending(&mut items, &mut pending_email);
            pending_email = email_only;
            continue;
        }
        if let Some(password_only) = labeled_password(line) {
            if !pending_email.is_empty() && !password_only.is_empty() {
                items.push(item("credentials", "", &pending_email, &password_only, ""));
                pending_email.clear();
            } else if !pending_email.is_empty() {
                flush_pending(&mut items, &mut pending_email);
            } else {
                items.push(item("error", "", "", "", "只有密码没有账号"));
            }
            continue;
        }
        if let Some((email, password)) = email_separator(line) {
            flush_pending(&mut items, &mut pending_email);
            items.push(item("credentials", "", &email, &password, ""));
            continue;
        }
        if looks_like_token(line) {
            flush_pending(&mut items, &mut pending_email);
            items.push(item("token", line, "", "", ""));
            continue;
        }
        flush_pending(&mut items, &mut pending_email);
        items.push(item("error", "", "", "", "无法识别"));
    }
    flush_pending(&mut items, &mut pending_email);
    items
}

fn flush_pending(items: &mut Vec<PasteItem>, pending_email: &mut String) {
    if pending_email.is_empty() {
        return;
    }
    items.push(item("error", "", pending_email, "", "只有账号没有密码"));
    pending_email.clear();
}

fn item(kind: &str, token: &str, email: &str, password: &str, message: &str) -> PasteItem {
    PasteItem {
        kind: kind.to_string(),
        token: token.to_string(),
        email: email.to_string(),
        password: password.to_string(),
        message: message.to_string(),
    }
}

fn looks_like_email(value: &str) -> bool {
    let text = value.trim();
    match text.find('@') {
        Some(at) => at > 0 && at < text.len() - 1 && !text.contains(' '),
        None => false,
    }
}

fn email_shape(text: &str) -> bool {
    let (local, rest) = match text.split_once('@') {
        Some(pair) => pair,
        None => return false,
    };
    if local.is_empty() || local.chars().any(|ch| ch.is_whitespace() || ch == '@') {
        return false;
    }
    let (domain, tld) = match rest.rsplit_once('.') {
        Some(pair) => pair,
        None => return false,
    };
    !domain.is_empty()
        && !tld.is_empty()
        && !domain.chars().any(|ch| ch.is_whitespace() || ch == '@')
        && !tld.chars().any(|ch| ch.is_whitespace() || ch == '@')
}

fn looks_like_token(line: &str) -> bool {
    let text = line.trim();
    if text.is_empty() {
        return false;
    }
    let lower = text.to_lowercase();
    if lower.contains("workoscursorsessiontoken=") || lower.contains("%3a%3a") || text.contains("::") {
        return true;
    }
    let parts: Vec<&str> = text.split('.').collect();
    parts.len() == 3 && parts.iter().all(|part| !part.is_empty())
}

fn labeled_both(line: &str) -> Option<(String, String)> {
    let rest = label_prefix(line, &["账号", "帐号", "账户", "邮箱", "用户名"])?;
    let (email_raw, after_email) = take_email(rest)?;
    let after_email = after_email.trim_start();
    let password = after_email.strip_prefix("密码")?.trim_start();
    let password = password.strip_prefix(['：', ':'])?.trim();
    let email = sanitize_email(Some(email_raw));
    if email.is_empty() || password.is_empty() {
        return None;
    }
    Some((email, password.to_string()))
}

fn labeled_email(line: &str) -> String {
    let rest = match label_prefix(line, &["账号", "帐号", "账户", "邮箱", "用户名"]) {
        Some(rest) => rest,
        None => return String::new(),
    };
    let (email_raw, after) = match take_email(rest) {
        Some(pair) => pair,
        None => return String::new(),
    };
    if !after.trim().is_empty() {
        return String::new();
    }
    sanitize_email(Some(email_raw))
}

fn labeled_password(line: &str) -> Option<String> {
    let rest = line.trim_start().strip_prefix("密码")?;
    let password = rest.trim_start().strip_prefix(['：', ':'])?.trim();
    Some(password.to_string())
}

fn email_separator(line: &str) -> Option<(String, String)> {
    let (email_raw, rest) = take_email(line)?;
    let email = sanitize_email(Some(email_raw));
    if email.is_empty() || rest.is_empty() {
        return None;
    }
    let stripped = rest.trim_start();
    let password = if let Some(tail) = stripped.strip_prefix("----") {
        tail.trim()
    } else if stripped.starts_with([':', '：']) {
        stripped[stripped.chars().next().map(char::len_utf8).unwrap_or(0)..].trim()
    } else if rest.starts_with([' ', '\t']) {
        rest.trim()
    } else {
        return None;
    };
    if password.is_empty() {
        return None;
    }
    Some((email, password.to_string()))
}

fn label_prefix<'a>(line: &'a str, labels: &[&str]) -> Option<&'a str> {
    let line = line.trim_start();
    for label in labels {
        if let Some(rest) = line.strip_prefix(label) {
            let rest = rest.trim_start();
            return rest.strip_prefix(['：', ':']).map(str::trim_start);
        }
    }
    None
}

fn take_email(text: &str) -> Option<(&str, &str)> {
    let at = text.find('@')?;
    if at == 0 || !text[..at].bytes().all(is_email_local) {
        return None;
    }
    let domain = &text[at + 1..];
    let span = domain.bytes().take_while(|byte| is_email_domain(*byte)).count();
    if span == 0 {
        return None;
    }
    let mut end = None;
    for (index, byte) in domain[..span].bytes().enumerate() {
        if byte != b'.' {
            continue;
        }
        let letters = domain[index + 1..span].bytes().take_while(|byte| byte.is_ascii_alphabetic()).count();
        if letters >= 2 {
            end = Some(index + 1 + letters);
        }
    }
    let end = end?;
    Some((&text[..at + 1 + end], &text[at + 1 + end..]))
}

fn is_email_local(byte: u8) -> bool {
    byte.is_ascii_alphanumeric() || matches!(byte, b'.' | b'_' | b'%' | b'+' | b'-')
}

fn is_email_domain(byte: u8) -> bool {
    byte.is_ascii_alphanumeric() || matches!(byte, b'.' | b'-')
}

fn looks_like_session_token(value: &str) -> bool {
    let text = value.trim();
    if text.is_empty() || text.chars().any(char::is_whitespace) {
        return false;
    }
    let lower = text.to_lowercase();
    if lower.contains("workoscursorsessiontoken=") || lower.contains("%3a%3a") || text.contains("::") {
        return true;
    }
    let parts: Vec<&str> = text.split('.').collect();
    parts.len() == 3 && parts.iter().all(|part| !part.is_empty())
}

fn query_map(query: &str) -> BTreeMap<String, String> {
    let mut found = BTreeMap::new();
    for part in query.split('&').filter(|part| !part.is_empty()) {
        let (raw_key, raw_value) = match part.find('=') {
            Some(eq) => (&part[..eq], &part[eq + 1..]),
            None => (part, ""),
        };
        let name = percent_decode(raw_key).trim().to_lowercase();
        if name.is_empty() || found.contains_key(&name) {
            continue;
        }
        found.insert(name, percent_decode(raw_value).trim().to_string());
    }
    found
}

fn percent_decode(value: &str) -> String {
    let bytes = value.as_bytes();
    let mut out = Vec::new();
    let mut index = 0;
    while index < bytes.len() {
        if bytes[index] == b'%' && index + 2 < bytes.len() {
            if let (Some(hi), Some(lo)) = (hex_value(bytes[index + 1]), hex_value(bytes[index + 2])) {
                out.push((hi << 4) | lo);
                index += 3;
                continue;
            }
            return value.to_string();
        }
        if bytes[index] == b'+' {
            return value.to_string();
        }
        out.push(bytes[index]);
        index += 1;
    }
    String::from_utf8(out).unwrap_or_else(|_| value.to_string())
}

fn hex_value(byte: u8) -> Option<u8> {
    match byte {
        b'0'..=b'9' => Some(byte - b'0'),
        b'a'..=b'f' => Some(byte - b'a' + 10),
        b'A'..=b'F' => Some(byte - b'A' + 10),
        _ => None,
    }
}

fn percent_encode(value: &str) -> String {
    let mut out = String::new();
    for byte in value.as_bytes() {
        let ch = *byte as char;
        if ch.is_ascii_alphanumeric() || matches!(ch, '-' | '.' | '_' | '~') {
            out.push(ch);
        } else {
            out.push('%');
            out.push_str(&format!("{byte:02X}"));
        }
    }
    out
}

fn normalized_token(value: &str) -> String {
    let text = value.trim();
    if text.is_empty() {
        return String::new();
    }
    token::normalize(text).unwrap_or_else(|_| text.to_string())
}

fn read_string(payload: &Value, name: &str) -> String {
    match payload.get(name) {
        Some(Value::String(text)) => text.trim().to_string(),
        _ => String::new(),
    }
}

fn valid_profile_id(value: &str) -> String {
    let text = value.trim().to_lowercase();
    let bytes = text.as_bytes();
    if bytes.len() != 36 {
        return String::new();
    }
    for (index, byte) in bytes.iter().enumerate() {
        let ok = if matches!(index, 8 | 13 | 18 | 23) {
            *byte == b'-'
        } else {
            byte.is_ascii_hexdigit()
        };
        if !ok {
            return String::new();
        }
    }
    text
}

fn canonical_profile_key(key: &str) -> String {
    if let Some(email) = key.strip_prefix("email:") {
        return profile_key(Some(email), None);
    }
    if let Some(account_id) = key.strip_prefix("id:") {
        return profile_key(None, Some(account_id));
    }
    String::new()
}

fn clean_profile_index(index: &Map<String, Value>) -> Map<String, Value> {
    let mut cleaned = Map::new();
    for (key, value) in index {
        let name = canonical_profile_key(key);
        let profile_id = value.as_str().map(valid_profile_id).unwrap_or_default();
        if !name.is_empty() && !profile_id.is_empty() {
            cleaned.insert(name, json!(profile_id));
        }
    }
    cleaned
}
