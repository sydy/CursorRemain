//! Pure update-note and version parsing. Filesystem, zip, and network stay in the shells.

use serde_json::Value;

pub const REPO_OWNER: &str = "sydy";
pub const REPO_NAME: &str = "CursorRemain";
pub const LATEST_TAG: &str = "latest";
pub const WINDOWS_ASSET_NAME: &str = "CursorRemain-windows.zip";
pub const WINDOWS_LIGHT_ASSET_NAME: &str = "CursorRemain-windows-light.zip";
pub const MACOS_ASSET_NAME: &str = "CursorRemain-macos.zip";
pub const LEGACY_WINDOWS_ASSET_NAME: &str = "CursorTokenTray-windows.zip";
pub const LEGACY_MACOS_ASSET_NAME: &str = "CursorTokenTray-macos.zip";
pub const WINDOWS_EXE_NAME: &str = "CursorRemain.exe";
pub const LEGACY_WINDOWS_EXE_NAME: &str = "CursorTokenTray.exe";
pub const MAC_APP_NAME: &str = "CursorRemain.app";
pub const LEGACY_MAC_APP_NAME: &str = "CursorTokenTray.app";
pub const MAX_ZIP_BYTES: i64 = 140 * 1024 * 1024;
pub const MAX_LIGHT_ZIP_BYTES: i64 = 40 * 1024 * 1024;
pub const AUTO_CHECK_INTERVAL_SECS: i64 = 12 * 60 * 60;
pub const PENDING_INSTALL_FILE_NAME: &str = "update-pending.json";

#[derive(Clone, Debug, PartialEq)]
pub struct AppReleaseAsset {
    pub id: i64,
    pub name: String,
    pub url: String,
    pub size: i64,
    pub updated_at: String,
}

#[derive(Clone, Debug, PartialEq)]
pub struct AppRelease {
    pub tag: String,
    pub version: String,
    pub body: String,
    pub commit_sha: String,
    pub page_url: String,
    pub published_at: String,
    pub assets: Vec<AppReleaseAsset>,
}

#[derive(Clone, Debug, PartialEq)]
pub struct UpdateDecision {
    pub up_to_date: bool,
    pub available: bool,
    pub message: String,
    pub release: Option<AppRelease>,
    pub asset: Option<AppReleaseAsset>,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct RememberedInstall {
    pub sha: String,
    pub asset_id: i64,
}

pub fn normalize_sha(raw: Option<&str>) -> String {
    let mut s = raw.unwrap_or("").trim().to_ascii_lowercase();
    if let Some(rest) = s.strip_prefix("sha-") {
        s = rest.to_string();
    }
    if !(7..=40).contains(&s.len()) || !s.bytes().all(|b| b.is_ascii_hexdigit()) {
        return String::new();
    }
    s
}

pub fn extract_sha(text: Option<&str>) -> String {
    let raw = text.unwrap_or("");
    if let Some(full) = find_word_hex(raw, 40, 40) {
        return normalize_sha(Some(&full));
    }
    if let Some(short) = find_marked_hex(raw) {
        return normalize_sha(Some(&short));
    }
    String::new()
}

pub fn same_sha(left: Option<&str>, right: Option<&str>) -> bool {
    let a = normalize_sha(left);
    let b = normalize_sha(right);
    if a.is_empty() || b.is_empty() {
        return false;
    }
    let min = a.len().min(b.len());
    if min < 7 {
        return a == b;
    }
    a.starts_with(b.as_str()) || b.starts_with(a.as_str())
}

pub fn sha_from_informational_version(informational: Option<&str>) -> String {
    let info = informational.unwrap_or("");
    match info.rfind('+') {
        Some(plus) => normalize_sha(Some(&info[plus + 1..])),
        None => String::new(),
    }
}

pub fn version_from_informational(informational: Option<&str>) -> String {
    let mut info = informational.unwrap_or("").trim().to_string();
    if info.is_empty() {
        return String::new();
    }
    if let Some(plus) = info.find('+') {
        info.truncate(plus);
    }
    normalize_product_version(Some(&info))
}

pub fn normalize_product_version(raw: Option<&str>) -> String {
    let value = raw.unwrap_or("").trim();
    let value = value.strip_prefix(['v', 'V']).unwrap_or(value);
    match product_version_prefix(value) {
        Some(end) => value[..end].to_string(),
        None => String::new(),
    }
}

pub fn extract_product_version(text: Option<&str>) -> String {
    let raw = text.unwrap_or("");
    if let Some(tagged) = find_word_version(raw) {
        return tagged;
    }
    normalize_product_version(Some(raw))
}

pub fn product_version_from_release(tag: Option<&str>, body: Option<&str>) -> String {
    let from_tag = normalize_product_version(tag);
    if !from_tag.is_empty() {
        return from_tag;
    }
    let raw = body.unwrap_or("");
    if let Some(labeled) = find_labeled_version(raw) {
        return labeled;
    }
    extract_product_version(Some(raw))
}

pub fn compare_product_versions(left: Option<&str>, right: Option<&str>) -> i32 {
    let (Ok(a), Ok(b)) = (try_parse_product_version(left), try_parse_product_version(right)) else {
        return 0;
    };
    match a[0].cmp(&b[0]).then(a[1].cmp(&b[1])).then(a[2].cmp(&b[2])) {
        std::cmp::Ordering::Less => -1,
        std::cmp::Ordering::Equal => 0,
        std::cmp::Ordering::Greater => 1,
    }
}

pub fn try_parse_product_version(raw: Option<&str>) -> Result<[i32; 3], ()> {
    let version = normalize_product_version(raw);
    if version.is_empty() {
        return Err(());
    }
    let core = version.split_once('-').map(|(head, _)| head).unwrap_or(version.as_str());
    let bits: Vec<&str> = core.split('.').collect();
    if bits.len() != 3 {
        return Err(());
    }
    let mut parts = [0; 3];
    for (i, bit) in bits.iter().enumerate() {
        if bit.is_empty() || !bit.bytes().all(|b| b.is_ascii_digit()) {
            return Err(());
        }
        parts[i] = bit.parse::<i32>().map_err(|_| ())?;
    }
    Ok(parts)
}

pub fn format_release_label(version: Option<&str>, sha: Option<&str>) -> String {
    let ver = normalize_product_version(version);
    let short = short_sha(sha);
    if !ver.is_empty() && !short.is_empty() && short != LATEST_TAG {
        return format!("{ver} ({short})");
    }
    if !ver.is_empty() {
        return ver;
    }
    if short.is_empty() {
        LATEST_TAG.to_string()
    } else {
        short
    }
}

pub fn short_sha(sha: Option<&str>) -> String {
    let value = normalize_sha(sha);
    if value.is_empty() {
        return LATEST_TAG.to_string();
    }
    if value.len() > 7 {
        value[..7].to_string()
    } else {
        value
    }
}

pub fn choose_release(official: Option<&AppRelease>, rolling: Option<&AppRelease>, current_version: Option<&str>) -> Option<AppRelease> {
    let Some(official) = official else {
        return rolling.cloned();
    };
    let Some(rolling) = rolling else {
        return Some(official.clone());
    };
    let current = normalize_product_version(current_version);
    let mut official_ver = normalize_product_version(Some(&official.version));
    if official_ver.is_empty() {
        official_ver = product_version_from_release(Some(&official.tag), Some(&official.body));
    }
    if !current.is_empty() && !official_ver.is_empty() && compare_product_versions(Some(&official_ver), Some(&current)) > 0 {
        return Some(official.clone());
    }
    Some(rolling.clone())
}

/// `product_version` is the shell's assembly version. An empty sha returns that version alone.
pub fn display_version(sha: Option<&str>, product_version: &str) -> String {
    let mut value = normalize_sha(sha);
    if value.len() > 7 {
        value.truncate(7);
    }
    if value.is_empty() {
        product_version.to_string()
    } else {
        format!("{product_version} ({value})")
    }
}

pub fn is_allowed_download_url(url: Option<&str>) -> bool {
    let Some(url) = url.map(str::trim).filter(|s| !s.is_empty()) else {
        return false;
    };
    let Some(rest) = strip_https(url) else {
        return false;
    };
    if rest.is_empty() {
        return false;
    }
    let authority = rest.split(['/', '?', '#']).next().unwrap_or("");
    let host = authority.rsplit('@').next().unwrap_or(authority);
    let host = host.split('%').next().unwrap_or(host);
    let host = if let Some(inner) = host.strip_prefix('[') {
        inner.split(']').next().unwrap_or("")
    } else {
        host.split(':').next().unwrap_or(host)
    };
    let host = host.trim().to_ascii_lowercase();
    if host.is_empty() {
        return false;
    }
    host == "github.com" || host.ends_with(".github.com") || host == "githubusercontent.com" || host.ends_with(".githubusercontent.com")
}

pub fn looks_packaged_windows(process_path: Option<&str>) -> bool {
    let path = process_path.unwrap_or("").trim();
    if path.is_empty() {
        return false;
    }
    let normalized = path.replace('/', "\\");
    let name = normalized.rsplit('\\').next().unwrap_or(&normalized);
    if !is_our_windows_exe(Some(name)) {
        return false;
    }
    let lower = normalized.to_ascii_lowercase();
    !(lower.contains("\\bin\\") || lower.contains("\\obj\\") || lower.contains("\\.build\\"))
}

pub fn looks_packaged_macos(bundle_or_exe_path: Option<&str>) -> bool {
    let path = bundle_or_exe_path.unwrap_or("").trim().replace('\\', "/");
    if path.is_empty() {
        return false;
    }
    let lower = path.to_ascii_lowercase();
    if lower.contains("/.build/") || lower.contains("/deriveddata/") {
        return false;
    }
    if lower.ends_with(".app") || lower.contains(".app/contents/") {
        let app = app_bundle_path(&path);
        let name = app.rsplit('/').next().unwrap_or(&app);
        return is_our_mac_app(Some(name));
    }
    false
}

pub fn app_bundle_path(path: &str) -> String {
    let value = path.replace('\\', "/");
    let value = value.trim_end_matches('/').to_string();
    let lower = value.to_ascii_lowercase();
    if let Some(idx) = lower.find(".app/") {
        return value[..idx + 4].to_string();
    }
    value
}

pub fn is_our_windows_exe(file_name: Option<&str>) -> bool {
    let name = file_name.unwrap_or("").trim();
    name.eq_ignore_ascii_case(WINDOWS_EXE_NAME) || name.eq_ignore_ascii_case(LEGACY_WINDOWS_EXE_NAME)
}

pub fn is_our_mac_app(file_name: Option<&str>) -> bool {
    let name = file_name.unwrap_or("").trim();
    name.eq_ignore_ascii_case(MAC_APP_NAME) || name.eq_ignore_ascii_case(LEGACY_MAC_APP_NAME)
}

pub fn parse_release(raw: &Value) -> Result<AppRelease, String> {
    if !raw.is_object() {
        return Err("release json must be an object".into());
    }
    let tag = json_str(raw, "tag_name");
    let mut page = json_str(raw, "html_url");
    if page.is_empty() {
        page = if !tag.is_empty() && tag != LATEST_TAG {
            format!("https://github.com/{REPO_OWNER}/{REPO_NAME}/releases/tag/{tag}")
        } else {
            latest_release_page_url()
        };
    }
    let body = json_str(raw, "body");
    let mut sha = extract_sha(Some(&body));
    if sha.is_empty() {
        sha = normalize_sha(Some(&json_str(raw, "target_commitish")));
    }
    let mut assets = Vec::new();
    if let Some(arr) = raw.get("assets").and_then(Value::as_array) {
        for item in arr {
            let name = json_str(item, "name");
            let url = json_str(item, "browser_download_url");
            if name.is_empty() || url.is_empty() {
                continue;
            }
            let id = item.get("id").and_then(json_i64).unwrap_or(0);
            let size = item.get("size").and_then(Value::as_i64).or_else(|| item.get("size").and_then(Value::as_u64).and_then(|n| i64::try_from(n).ok())).unwrap_or(0);
            let updated_raw = json_str(item, "updated_at");
            let updated_at = if !updated_raw.is_empty() && parse_iso(&updated_raw).is_some() { updated_raw } else { String::new() };
            assets.push(AppReleaseAsset { id, name, url, size, updated_at });
        }
    }
    let version_tag = if tag.is_empty() { String::new() } else { tag.clone() };
    Ok(AppRelease {
        tag: if tag.is_empty() { LATEST_TAG.to_string() } else { tag },
        version: product_version_from_release(Some(&version_tag), Some(&body)),
        body,
        commit_sha: sha,
        page_url: page,
        published_at: json_str(raw, "published_at"),
        assets,
    })
}

pub fn parse_release_text(json_text: &str) -> Result<AppRelease, String> {
    let value: Value = serde_json::from_str(json_text).map_err(|err| err.to_string())?;
    parse_release(&value)
}

pub fn known_assets(tag: Option<&str>) -> Vec<AppReleaseAsset> {
    vec![
        AppReleaseAsset { id: 0, name: WINDOWS_ASSET_NAME.to_string(), url: asset_download_url(WINDOWS_ASSET_NAME, tag), size: 0, updated_at: String::new() },
        AppReleaseAsset { id: 0, name: MACOS_ASSET_NAME.to_string(), url: asset_download_url(MACOS_ASSET_NAME, tag), size: 0, updated_at: String::new() },
    ]
}

pub fn asset_download_url(asset_name: &str, tag: Option<&str>) -> String {
    let release_tag = tag.map(str::trim).filter(|s| !s.is_empty()).unwrap_or(LATEST_TAG);
    format!("https://github.com/{REPO_OWNER}/{REPO_NAME}/releases/download/{release_tag}/{asset_name}")
}

pub fn parse_release_page(html: Option<&str>) -> Result<AppRelease, String> {
    let html = html.unwrap_or("");
    let mut sha = extract_sha(Some(html));
    if sha.is_empty() {
        if let Some(commit) = find_commit_hex(html) {
            sha = normalize_sha(Some(&commit));
        }
    }
    if sha.is_empty() {
        return Err("发布页里找不到提交哈希".into());
    }
    Ok(AppRelease {
        tag: LATEST_TAG.to_string(),
        version: product_version_from_release(Some(""), Some(html)),
        body: html.to_string(),
        commit_sha: sha,
        page_url: latest_release_page_url(),
        published_at: String::new(),
        assets: known_assets(None),
    })
}

pub fn parse_tag_ref_sha(raw: &Value) -> String {
    if raw.get("object").is_some() {
        return normalize_sha(raw["object"].get("sha").and_then(Value::as_str));
    }
    normalize_sha(raw.get("sha").and_then(Value::as_str))
}

pub fn parse_tag_ref_sha_text(json_text: &str) -> Result<String, String> {
    let value: Value = serde_json::from_str(json_text).map_err(|err| err.to_string())?;
    Ok(parse_tag_ref_sha(&value))
}

pub fn find_asset<'a>(release: &'a AppRelease, asset_name: &str) -> Option<&'a AppReleaseAsset> {
    release.assets.iter().find(|asset| asset.name.eq_ignore_ascii_case(asset_name))
}

pub fn asset_name_candidates(preferred: &str) -> Vec<&'static str> {
    if preferred.eq_ignore_ascii_case(WINDOWS_LIGHT_ASSET_NAME) {
        return vec![WINDOWS_LIGHT_ASSET_NAME, WINDOWS_ASSET_NAME];
    }
    if preferred.eq_ignore_ascii_case(WINDOWS_ASSET_NAME) {
        return vec![WINDOWS_ASSET_NAME];
    }
    if preferred.eq_ignore_ascii_case(MACOS_ASSET_NAME) {
        return vec![MACOS_ASSET_NAME];
    }
    Vec::new()
}

/// Candidates for a name that is not one of the known platform zips. The known names use [asset_name_candidates].
pub fn asset_name_candidates_or_self(preferred: &str) -> Vec<String> {
    let known = asset_name_candidates(preferred);
    if !known.is_empty() {
        return known.into_iter().map(str::to_string).collect();
    }
    vec![preferred.to_string()]
}

pub fn preferred_windows_asset_name(prefer_light: bool) -> &'static str {
    if prefer_light { WINDOWS_LIGHT_ASSET_NAME } else { WINDOWS_ASSET_NAME }
}

pub fn is_windows_light_asset_name(name: Option<&str>) -> bool {
    name.unwrap_or("").eq_ignore_ascii_case(WINDOWS_LIGHT_ASSET_NAME)
}

pub fn max_bytes_for_asset(name: Option<&str>) -> i64 {
    if is_windows_light_asset_name(name) { MAX_LIGHT_ZIP_BYTES } else { MAX_ZIP_BYTES }
}

pub fn find_preferred_asset<'a>(release: &'a AppRelease, preferred_name: &str) -> Option<&'a AppReleaseAsset> {
    for name in asset_name_candidates_or_self(preferred_name) {
        let Some(asset) = find_asset(release, &name) else { continue };
        if !is_allowed_download_url(Some(&asset.url)) {
            continue;
        }
        if asset.size > max_bytes_for_asset(Some(&name)) {
            continue;
        }
        return Some(asset);
    }
    None
}

pub fn evaluate(release: &AppRelease, asset_name: &str, current_sha: &str, installed_sha: &str, installed_asset_id: i64, current_version: Option<&str>) -> UpdateDecision {
    let Some(asset) = find_preferred_asset(release, asset_name).cloned() else {
        return UpdateDecision { up_to_date: false, available: false, message: "最新发布没有本平台安装包".into(), release: None, asset: None };
    };
    if asset.size > max_bytes_for_asset(Some(&asset.name)) {
        return UpdateDecision { up_to_date: false, available: false, message: "安装包过大，已取消更新".into(), release: None, asset: None };
    }
    let mut remote_version = normalize_product_version(Some(&release.version));
    if remote_version.is_empty() {
        remote_version = product_version_from_release(Some(&release.tag), Some(&release.body));
    }
    let local_version = normalize_product_version(current_version);
    if !remote_version.is_empty() && !local_version.is_empty() {
        let cmp = compare_product_versions(Some(&remote_version), Some(&local_version));
        if cmp < 0 {
            return up_to_date(release, asset);
        }
        if cmp > 0 {
            return available(release, asset);
        }
    }
    let mut local_sha = normalize_sha(Some(current_sha));
    if local_sha.is_empty() {
        local_sha = normalize_sha(Some(installed_sha));
    }
    let remote_sha = normalize_sha(Some(&release.commit_sha));
    if !remote_sha.is_empty() && same_sha(Some(&local_sha), Some(&remote_sha)) {
        return up_to_date(release, asset);
    }
    if remote_sha.is_empty() && installed_asset_id > 0 && installed_asset_id == asset.id {
        return up_to_date(release, asset);
    }
    if !remote_sha.is_empty() && !same_sha(Some(&local_sha), Some(&remote_sha)) && !local_sha.is_empty() {
        return available(release, asset);
    }
    if local_sha.is_empty() {
        return available(release, asset);
    }
    if !remote_sha.is_empty() && !same_sha(Some(&local_sha), Some(&remote_sha)) {
        return available(release, asset);
    }
    up_to_date(release, asset)
}

pub fn remembered_install_after_helper(sha: &str, asset_id: i64, helper_started: bool) -> Option<RememberedInstall> {
    helper_started.then(|| RememberedInstall { sha: normalize_sha(Some(sha)), asset_id })
}

/// JSON body written by the shell. This function does not touch the filesystem.
pub fn pending_install_json(sha: &str, asset_id: i64) -> Value {
    serde_json::json!({"sha": normalize_sha(Some(sha)), "asset_id": asset_id})
}

pub fn read_pending_install_json(text: &str) -> Option<RememberedInstall> {
    let doc: Value = serde_json::from_str(text).ok()?;
    let sha = normalize_sha(doc.get("sha").and_then(Value::as_str));
    if sha.is_empty() {
        return None;
    }
    let id = doc.get("asset_id").and_then(json_i64).unwrap_or(0);
    Some(RememberedInstall { sha, asset_id: id })
}

pub fn confirm_pending_install(current_sha: &str, pending: Option<&RememberedInstall>) -> Option<RememberedInstall> {
    let pending = pending?;
    let current = normalize_sha(Some(current_sha));
    if current.is_empty() {
        return None;
    }
    same_sha(Some(&current), Some(&pending.sha)).then(|| pending.clone())
}

pub fn pending_install_failed(current_sha: &str, pending: Option<&RememberedInstall>) -> bool {
    let Some(pending) = pending else { return false };
    let current = normalize_sha(Some(current_sha));
    if current.is_empty() {
        return false;
    }
    !same_sha(Some(&current), Some(&pending.sha))
}

pub fn should_fallback_from_api(status_code: i32) -> bool {
    matches!(status_code, 401 | 403 | 404 | 429) || status_code >= 500
}

pub fn should_auto_check(last_check_at: &str, now: &str, manual: bool) -> bool {
    if manual {
        return true;
    }
    let Some(last) = parse_iso(last_check_at) else { return true };
    let Some(now) = parse_iso(now) else { return true };
    now.saturating_sub(last) >= AUTO_CHECK_INTERVAL_SECS
}

/// Format a UTC unix timestamp as `yyyy-MM-ddTHH:mm:ssZ`.
pub fn now_iso(unix_seconds: i64) -> String {
    format_unix_utc(unix_seconds)
}

pub fn parse_iso(raw: &str) -> Option<i64> {
    parse_rfc3339(raw.trim())
}

pub fn release_to_json(release: &AppRelease) -> Value {
    serde_json::json!({
        "tag": release.tag,
        "version": release.version,
        "body": release.body,
        "sha": release.commit_sha,
        "page_url": release.page_url,
        "published_at": release.published_at,
        "assets": release.assets.iter().map(asset_to_json).collect::<Vec<_>>(),
    })
}

pub fn asset_to_json(asset: &AppReleaseAsset) -> Value {
    serde_json::json!({
        "id": asset.id,
        "name": asset.name,
        "url": asset.url,
        "size": asset.size,
        "updated_at": asset.updated_at,
    })
}

pub fn decision_to_json(decision: &UpdateDecision) -> Value {
    serde_json::json!({
        "up_to_date": decision.up_to_date,
        "available": decision.available,
        "message": decision.message,
        "release": decision.release.as_ref().map(release_to_json),
        "asset": decision.asset.as_ref().map(asset_to_json),
    })
}

fn up_to_date(release: &AppRelease, asset: AppReleaseAsset) -> UpdateDecision {
    UpdateDecision { up_to_date: true, available: false, message: "已是最新版本".into(), release: Some(release.clone()), asset: Some(asset) }
}

fn available(release: &AppRelease, asset: AppReleaseAsset) -> UpdateDecision {
    let version = if release.version.is_empty() { product_version_from_release(Some(&release.tag), Some(&release.body)) } else { release.version.clone() };
    UpdateDecision {
        up_to_date: false,
        available: true,
        message: format!("发现新版本 {}", format_release_label(Some(&version), Some(&release.commit_sha))),
        release: Some(release.clone()),
        asset: Some(asset),
    }
}

fn latest_release_page_url() -> String {
    format!("https://github.com/{REPO_OWNER}/{REPO_NAME}/releases/tag/{LATEST_TAG}")
}

fn json_str(value: &Value, key: &str) -> String {
    value.get(key).and_then(Value::as_str).unwrap_or("").to_string()
}

fn json_i64(value: &Value) -> Option<i64> {
    value.as_i64().or_else(|| value.as_u64().and_then(|n| i64::try_from(n).ok())).or_else(|| value.as_str().and_then(|text| text.trim().parse().ok()))
}

fn strip_https(url: &str) -> Option<&str> {
    let (scheme, rest) = url.split_once("://")?;
    if scheme.eq_ignore_ascii_case("https") { Some(rest) } else { None }
}

/// `(?i)\b([0-9a-f]{min,max})\b`
fn find_word_hex(text: &str, min: usize, max: usize) -> Option<String> {
    let bytes = text.as_bytes();
    let mut i = 0;
    while i < bytes.len() {
        if bytes[i].is_ascii_hexdigit() && (i == 0 || !is_word(bytes[i - 1])) {
            let start = i;
            while i < bytes.len() && bytes[i].is_ascii_hexdigit() {
                i += 1;
            }
            let len = i - start;
            if (min..=max).contains(&len) && (i == bytes.len() || !is_word(bytes[i])) {
                return Some(text[start..i].to_string());
            }
            continue;
        }
        i += 1;
    }
    None
}

/// `(?i)(?:commit/|`)([0-9a-f]{7,40})(?:`|\b)`
fn find_marked_hex(text: &str) -> Option<String> {
    let lower = text.to_ascii_lowercase();
    let bytes = lower.as_bytes();
    let mut i = 0;
    while i < bytes.len() {
        let marker = if bytes[i..].starts_with(b"commit/") {
            Some(i + "commit/".len())
        } else if bytes[i] == b'`' {
            Some(i + 1)
        } else {
            None
        };
        if let Some(start) = marker {
            let mut end = start;
            while end < bytes.len() && bytes[end].is_ascii_hexdigit() {
                end += 1;
            }
            let len = end - start;
            if (7..=40).contains(&len) {
                let closed = end < bytes.len() && (bytes[end] == b'`' || !is_word(bytes[end]));
                let at_end = end == bytes.len();
                if closed || at_end {
                    return Some(text[start..end].to_string());
                }
            }
            i = start;
            continue;
        }
        i += 1;
    }
    None
}

/// `(?i)/commit/([0-9a-f]{7,40})`
fn find_commit_hex(text: &str) -> Option<String> {
    let lower = text.to_ascii_lowercase();
    let bytes = lower.as_bytes();
    let mut from = 0;
    while let Some(rel) = bytes[from..].windows(8).position(|w| w == b"/commit/") {
        let start = from + rel + 8;
        let mut end = start;
        while end < bytes.len() && bytes[end].is_ascii_hexdigit() {
            end += 1;
        }
        if (7..=40).contains(&(end - start)) {
            return Some(text[start..end].to_string());
        }
        from = start;
    }
    None
}

/// `(?i)\bv?(\d+\.\d+\.\d+)\b`
fn find_word_version(text: &str) -> Option<String> {
    let bytes = text.as_bytes();
    let mut i = 0;
    while i < bytes.len() {
        let boundary = i == 0 || !is_word(bytes[i - 1]);
        let mut start = i;
        if boundary && (bytes[i] == b'v' || bytes[i] == b'V') && i + 1 < bytes.len() && bytes[i + 1].is_ascii_digit() {
            start = i + 1;
        }
        if boundary && start < bytes.len() && bytes[start].is_ascii_digit() {
            if let Some(end) = semver_core(&text[start..]) {
                let after = start + end;
                if after == bytes.len() || !is_word(bytes[after]) {
                    return Some(text[start..after].to_string());
                }
            }
        }
        i += 1;
    }
    None
}

/// `(?i)(?:\*\*)?版本(?:\*\*)?\s*[:：]\s*`?v?(\d+\.\d+\.\d+)`
fn find_labeled_version(text: &str) -> Option<String> {
    let bytes = text.as_bytes();
    let mut i = 0;
    while i + "版本".len() <= bytes.len() {
        if text[i..].starts_with("版本") {
            let mut j = i + "版本".len();
            if text[j..].starts_with("**") {
                j += 2;
            }
            while j < bytes.len() && bytes[j].is_ascii_whitespace() {
                j += 1;
            }
            if j < bytes.len() && (bytes[j] == b':' || text[j..].starts_with('：')) {
                j += if bytes[j] == b':' { 1 } else { '：'.len_utf8() };
                while j < bytes.len() && bytes[j].is_ascii_whitespace() {
                    j += 1;
                }
                if j < bytes.len() && bytes[j] == b'`' {
                    j += 1;
                }
                if j < bytes.len() && (bytes[j] == b'v' || bytes[j] == b'V') {
                    j += 1;
                }
                if let Some(end) = semver_core(&text[j..]) {
                    return Some(text[j..j + end].to_string());
                }
            }
        }
        i += text[i..].chars().next().map(|c| c.len_utf8()).unwrap_or(1);
    }
    None
}

fn product_version_prefix(value: &str) -> Option<usize> {
    let core = semver_core(value)?;
    let rest = &value[core..];
    if let Some(stripped) = rest.strip_prefix('-') {
        let mut extra = 0;
        for (idx, ch) in stripped.char_indices() {
            if ch.is_ascii_alphanumeric() || ch == '.' || ch == '-' {
                extra = idx + ch.len_utf8();
            } else {
                break;
            }
        }
        if extra == 0 {
            return Some(core);
        }
        return Some(core + 1 + extra);
    }
    Some(core)
}

fn semver_core(value: &str) -> Option<usize> {
    let bytes = value.as_bytes();
    let mut i = 0;
    for part in 0..3 {
        if part > 0 {
            if i >= bytes.len() || bytes[i] != b'.' {
                return None;
            }
            i += 1;
        }
        let start = i;
        while i < bytes.len() && bytes[i].is_ascii_digit() {
            i += 1;
        }
        if i == start {
            return None;
        }
    }
    Some(i)
}

fn is_word(byte: u8) -> bool {
    byte.is_ascii_alphanumeric() || byte == b'_'
}

fn format_unix_utc(unix: i64) -> String {
    let (year, month, day, hour, minute, second) = civil_from_unix(unix);
    format!("{year:04}-{month:02}-{day:02}T{hour:02}:{minute:02}:{second:02}Z")
}

/// Accepts `yyyy-MM-ddTHH:mm:ss[.fff]Z` and numeric offsets. Returns UTC unix seconds.
pub fn parse_rfc3339(raw: &str) -> Option<i64> {
    if raw.len() < 20 {
        return None;
    }
    let bytes = raw.as_bytes();
    if bytes[4] != b'-' || bytes[7] != b'-' || !matches!(bytes[10], b'T' | b't' | b' ') || bytes[13] != b':' || bytes[16] != b':' {
        return None;
    }
    let year = parse_digits(&bytes[0..4])?;
    let month = parse_digits(&bytes[5..7])?;
    let day = parse_digits(&bytes[8..10])?;
    let hour = parse_digits(&bytes[11..13])?;
    let minute = parse_digits(&bytes[14..16])?;
    let second = parse_digits(&bytes[17..19])?;
    if !(1..=12).contains(&month) || day == 0 || hour > 23 || minute > 59 || second > 60 {
        return None;
    }
    let mut idx = 19;
    if idx < bytes.len() && bytes[idx] == b'.' {
        idx += 1;
        let start = idx;
        while idx < bytes.len() && bytes[idx].is_ascii_digit() {
            idx += 1;
        }
        if idx == start {
            return None;
        }
    }
    let mut offset = 0i64;
    if idx < bytes.len() && matches!(bytes[idx], b'Z' | b'z') {
        idx += 1;
    } else if idx < bytes.len() && matches!(bytes[idx], b'+' | b'-') {
        let sign = if bytes[idx] == b'-' { -1 } else { 1 };
        let rest = &bytes[idx + 1..];
        let (oh, om, used) = if rest.len() >= 5 && rest[2] == b':' {
            (parse_digits(&rest[0..2])?, parse_digits(&rest[3..5])?, 6)
        } else if rest.len() >= 4 && rest.iter().take(4).all(|b| b.is_ascii_digit()) {
            (parse_digits(&rest[0..2])?, parse_digits(&rest[2..4])?, 5)
        } else if rest.len() >= 2 && rest.iter().take(2).all(|b| b.is_ascii_digit()) {
            (parse_digits(&rest[0..2])?, 0, 3)
        } else {
            return None;
        };
        if oh > 23 || om > 59 {
            return None;
        }
        offset = sign * (oh as i64 * 3600 + om as i64 * 60);
        idx += used;
    } else {
        return None;
    }
    if idx != bytes.len() {
        return None;
    }
    let days = days_from_civil(year, month, day)?;
    Some(days * 86400 + hour as i64 * 3600 + minute as i64 * 60 + second.min(59) as i64 - offset)
}

fn parse_digits(bytes: &[u8]) -> Option<u32> {
    if bytes.is_empty() || !bytes.iter().all(|b| b.is_ascii_digit()) {
        return None;
    }
    std::str::from_utf8(bytes).ok()?.parse().ok()
}

fn days_from_civil(year: u32, month: u32, day: u32) -> Option<i64> {
    let dim = [0u32, 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
    let leap = year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);
    let max = if month == 2 && leap { 29 } else { *dim.get(month as usize)? };
    if day == 0 || day > max || year == 0 {
        return None;
    }
    let y = year as i64;
    let m = month as i64;
    let d = day as i64;
    let y = if m <= 2 { y - 1 } else { y };
    let era = if y >= 0 { y } else { y - 399 } / 400;
    let yoe = (y - era * 400) as u64;
    let mp = if m > 2 { m - 3 } else { m + 9 };
    let doy = (153 * mp + 2) / 5 + d - 1;
    let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy as u64;
    Some(era * 146097 + doe as i64 - 719468)
}

fn civil_from_unix(unix: i64) -> (i32, u32, u32, u32, u32, u32) {
    let secs = unix.rem_euclid(86400) as u32;
    let z = unix.div_euclid(86400) + 719468;
    let era = if z >= 0 { z } else { z - 146096 } / 146097;
    let doe = (z - era * 146097) as u64;
    let yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
    let y = yoe as i64 + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = doy - (153 * mp + 2) / 5 + 1;
    let m = if mp < 10 { mp + 3 } else { mp - 9 };
    let y = if m <= 2 { y + 1 } else { y };
    (y as i32, m as u32, d as u32, secs / 3600, (secs % 3600) / 60, secs % 60)
}
