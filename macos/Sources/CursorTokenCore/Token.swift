import CryptoKit
import Foundation

public enum Token {
    public static let cookieName = "WorkosCursorSessionToken"
    public static let junk: [Character] = ["\u{2026}", "\u{2022}", "\u{FEFF}", "\u{200B}", "\u{200C}", "\u{200D}", "\u{00A0}"]

    public static func normalize(_ raw: String) throws -> String {
        let result = RustCore.object("normalize_token", ["input": raw])
        if result["ok"] as? Bool == true { return result["value"] as? String ?? "" }
        throw CursorAPIError(result["error"] as? String ?? "Token 无效", statusCode: 401)
    }

    public static func variants(_ token: String) -> [String] {
        RustCore.strings("token_variants", ["input": token])
    }

    public static func accountId(from token: String) -> String {
        RustCore.string("account_id", ["input": token])
    }

    public static func safeAccountId(_ value: String) -> String {
        RustCore.string("safe_account_id", ["input": value])
    }

    public static func isAuthErrorMessage(_ message: String?) -> Bool {
        RustCore.bool("is_auth_error_message", ["message": message ?? ""])
    }

    public static func looksLikeJWT(_ value: String) -> Bool {
        let parts = value.split(separator: ".", omittingEmptySubsequences: false)
        return parts.count == 3 && parts.allSatisfy { !$0.isEmpty }
    }

    public static func jwtPayload(_ jwt: String) -> [String: Any]? {
        let parts = jwt.split(separator: ".", omittingEmptySubsequences: false)
        guard parts.count == 3 else { return nil }
        var payload = String(parts[1])
            .replacingOccurrences(of: "-", with: "+")
            .replacingOccurrences(of: "_", with: "/")
        let pad = (4 - payload.count % 4) % 4
        payload += String(repeating: "=", count: pad)
        guard let data = Data(base64Encoded: payload),
              let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
        else { return nil }
        return obj
    }

    public static func extractUserId(fromJWT jwt: String) -> String {
        let sub = jwtSubject(jwt)
        return sub.split(separator: "|").last.map(String.init) ?? ""
    }

    public static func extractJWT(_ token: String) -> String {
        RustCore.string("extract_jwt", ["input": token])
    }

    public static func jwtSubject(_ token: String) -> String {
        let jwt = looksLikeJWT(token) ? token : extractJWT(token)
        guard let payload = jwtPayload(jwt) else { return "" }
        return payload["sub"] as? String ?? ""
    }

    public static func jwtClaim(_ token: String, _ name: String) -> String {
        let jwt = looksLikeJWT(token) ? token : extractJWT(token)
        guard let payload = jwtPayload(jwt) else { return "" }
        if let text = payload[name] as? String { return text }
        if let num = payload[name] as? NSNumber { return num.stringValue }
        return ""
    }

    /// JWT `type` claim: `session` signs Cursor in; `web` is a browser cookie.
    public static func jwtType(_ token: String) -> String {
        RustCore.string("jwt_type", ["input": token])
    }

    public static func canWriteToCursor(_ token: String) -> Bool {
        RustCore.bool("can_write_to_cursor", ["input": token])
    }
}
