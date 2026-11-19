import CryptoKit
import Foundation

/// 豆包账号的凭据规则。
///
/// Cookie 头等同于完整登录态：只在本机加密落盘，任何日志都不能出现它的内容。
public enum DoubaoCredentials {
    public static let authMessage = "豆包登录已失效，请重新登录或粘贴新的 Cookie"
    public static let loginURL = "https://www.doubao.com/"
    public static let cookieDomainSuffix = "doubao.com"
    public static let maxFetchIntervalSeconds: TimeInterval = 5 * 60

    /// 去掉 `Cookie:` 前缀、换行和空对；只保留 `name=value`。
    public static func normalize(_ raw: String) -> String {
        RustCore.string("normalize_doubao_cookie", ["input": raw])
    }

    public static func accountId(cookie: String) -> String {
        accountId(payload: .null, cookie: cookie)
    }

    /// 优先真实响应里的用户 id，其次 Cookie 里的用户 id。
    public static func accountId(payload: JSONValue, cookie: String) -> String {
        let result = RustCore.object("doubao_identity", ["payload": payload.raw, "cookie": cookie])
        return result["account_id"] as? String ?? ""
    }

    public static func userId(payload: JSONValue, cookie: String) -> String {
        let result = RustCore.object("doubao_identity", ["payload": payload.raw, "cookie": cookie])
        return result["user_id"] as? String ?? ""
    }

    public static func isValidAccountId(_ raw: String) -> Bool {
        let text = raw.trimmingCharacters(in: .whitespaces)
        guard text.hasPrefix("doubao_") else { return false }
        let rest = text.dropFirst("doubao_".count)
        return !rest.isEmpty && rest.allSatisfy { $0.isASCII && ($0.isLetter || $0.isNumber || $0 == "_") }
    }

    public static func randomAccountId() -> String {
        let hex = UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased()
        return "doubao_" + String(hex.prefix(12))
    }

    public static func isAuthCode(_ code: Int) -> Bool {
        RustCore.bool("is_doubao_auth_code", ["code": code])
    }

    /// 登录窗口按账号隔离站点数据。`WKWebsiteDataStore(forIdentifier:)` 只收 UUID，
    /// 所以把账号 id 摘要成一个稳定的 UUID（同 id 每次得到同一个值）。
    public static func profileUUID(_ accountId: String) -> String {
        let digest = SHA256.hash(data: Data(("doubao-profile|" + accountId).utf8))
        var bytes = Array(digest.prefix(16))
        bytes[6] = (bytes[6] & 0x0F) | 0x40
        bytes[8] = (bytes[8] & 0x3F) | 0x80
        let hex = bytes.map { String(format: "%02x", $0) }.joined()
        let parts = [String(hex.prefix(8)), String(hex.dropFirst(8).prefix(4)), String(hex.dropFirst(12).prefix(4)),
                     String(hex.dropFirst(16).prefix(4)), String(hex.dropFirst(20))]
        return parts.joined(separator: "-")
    }

    /// 浏览器里取到的 Cookie 是否看起来已登录（有会话 Cookie 才值得拿去验证）。
    public static func looksLoggedIn(_ cookie: String) -> Bool {
        let names = ["sessionid", "sessionid_ss", "sid_tt", "odin_tt", "uid_tt"]
        let pairs = RustCore.object("doubao_identity", ["payload": [:], "cookie": cookie])
        if (pairs["user_id"] as? String ?? "").isEmpty { return false }
        let normalized = normalize(cookie)
        return names.contains { name in
            normalized.contains(name + "=")
        }
    }
}
