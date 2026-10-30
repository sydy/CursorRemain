import CryptoKit
import Foundation

public struct LoginPollResult: Equatable, Sendable {
    public var token: String
    public var pending: Bool
    public var message: String

    public init(token: String, pending: Bool, message: String) {
        self.token = token
        self.pending = pending
        self.message = message
    }
}

public enum CursorPasswordLogin {
    public static let loginURL = "https://cursor.com/login"
    public static let loginDeepURLBase = "https://cursor.com/loginDeepControl"
    public static let pollURL = "https://api2.cursor.sh/auth/poll"
    public static let timeoutSeconds = 180
    static let callbackKeys = [
        "workoscursorsessiontoken",
        "accesstoken",
        "access_token",
        "token",
        "cookie",
        "session_token",
    ]
    static let emailRe = try! NSRegularExpression(pattern: #"^[^@\s]+@[^@\s]+\.[^@\s]+$"#)

    public static func looksLikeEmail(_ value: String?) -> Bool {
        let text = (value ?? "").trimmingCharacters(in: .whitespaces)
        guard let at = text.firstIndex(of: "@") else { return false }
        return at > text.startIndex && at < text.index(before: text.endIndex) && !text.contains(" ")
    }

    public static func sanitizeEmail(_ raw: String?) -> String {
        RustCore.string("sanitize_email", ["input": raw ?? ""])
    }

    public static func defaultLabel(_ email: String, existing: String = "") -> String {
        RustCore.string("default_account_label", ["email": email, "existing": existing])
    }

    public static func tokenFromCookies(_ cookies: [(name: String, value: String)]) -> String? {
        RustCore.string("token_from_cookies", ["cookies": cookies.map { ["name": $0.name, "value": $0.value] }])
    }

    public static func autofillScript(email: String, password: String) -> String {
        RustCore.string("autofill_script", ["email": email, "password": password])
    }

    public static func codeChallenge(_ verifier: String) -> String {
        RustCore.string("code_challenge", ["verifier": verifier])
    }

    public static func loginDeepURL(uuid: String, verifier: String) -> String {
        RustCore.string("login_deep_url", ["uuid": uuid, "verifier": verifier])
    }

    public static func tokenFromCallback(_ text: String?) -> String {
        RustCore.string("token_from_callback", ["input": text ?? ""])
    }

    public static func tokenFromPoll(_ payload: [String: Any]) -> String {
        RustCore.string("token_from_poll", payload)
    }

    public static func validProfileId(_ value: String?) -> String {
        let text = (value ?? "").trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        let pattern = #"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"#
        return text.range(of: pattern, options: .regularExpression) == nil ? "" : text
    }

    public static func profileKey(email: String, accountId: String = "") -> String {
        RustCore.string("profile_key", ["email": email, "account_id": accountId])
    }

    public static func canonicalProfileKey(_ key: String) -> String {
        if key.hasPrefix("email:") { return profileKey(email: String(key.dropFirst(6))) }
        if key.hasPrefix("id:") { return profileKey(email: "", accountId: String(key.dropFirst(3))) }
        return ""
    }

    public static func cleanProfileIndex(_ index: [String: String]) -> [String: String] {
        var cleaned: [String: String] = [:]
        for (key, value) in index {
            let name = canonicalProfileKey(key)
            let profileId = validProfileId(value)
            if !name.isEmpty, !profileId.isEmpty { cleaned[name] = profileId }
        }
        return cleaned
    }

    public static func assignProfile(_ index: [String: String], key: String, newId: String) -> (index: [String: String], profileId: String) {
        let result = RustCore.object("assign_profile", ["index": index, "key": key, "new_id": newId])
        let stored = (result["index"] as? [String: Any] ?? [:]).mapValues { "\($0)" }
        return (stored, result["profile_id"] as? String ?? "")
    }

    public static func bindProfile(_ index: [String: String], key: String, profileId: String) -> [String: String] {
        var cleaned = cleanProfileIndex(index)
        let name = canonicalProfileKey(key)
        let ident = validProfileId(profileId)
        if name.isEmpty || ident.isEmpty { return cleaned }
        cleaned[name] = ident
        return cleaned
    }

    public static func tokenFromPollJSON(_ json: String) -> String {
        guard let data = json.data(using: .utf8),
              let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
        else { return "" }
        return tokenFromPoll(obj)
    }

    public static func pollSession(uuid: String, verifier: String, session: URLSession = .shared) async -> LoginPollResult {
        let loginId = uuid.trimmingCharacters(in: .whitespacesAndNewlines)
        let secret = verifier.trimmingCharacters(in: .whitespacesAndNewlines)
        if loginId.isEmpty || secret.isEmpty {
            return LoginPollResult(token: "", pending: false, message: "请先生成登录地址")
        }
        guard let url = URL(string: pollURL),
              let body = try? JSONSerialization.data(withJSONObject: ["uuid": loginId, "verifier": secret])
        else {
            return LoginPollResult(token: "", pending: false, message: "获取失败")
        }
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.timeoutInterval = 20
        request.httpBody = body
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        request.setValue("ide", forHTTPHeaderField: "x-cursor-client-type")
        request.setValue("Mozilla/5.0 CursorRemain/1.0", forHTTPHeaderField: "User-Agent")
        do {
            let (data, response) = try await session.data(for: request)
            let code = (response as? HTTPURLResponse)?.statusCode ?? 0
            if code == 404 {
                return await pollSessionGet(uuid: loginId, verifier: secret, session: session)
            }
            if (200..<300).contains(code) {
                let token = tokenFromPollJSON(String(data: data, encoding: .utf8) ?? "")
                if token.isEmpty {
                    return LoginPollResult(token: "", pending: false, message: "登录结果里没有 Cookie")
                }
                return LoginPollResult(token: token, pending: false, message: "")
            }
            return LoginPollResult(token: "", pending: false, message: "获取失败")
        } catch {
            return LoginPollResult(token: "", pending: false, message: "获取失败")
        }
    }

    static func percentEncode(_ value: String) -> String {
        var encoded = ""
        for byte in value.utf8 {
            let unreserved = (byte >= 65 && byte <= 90) || (byte >= 97 && byte <= 122) || (byte >= 48 && byte <= 57)
                || byte == 45 || byte == 46 || byte == 95 || byte == 126
            if unreserved, let ch = UnicodeScalar(UInt32(byte)) {
                encoded.unicodeScalars.append(ch)
            } else {
                encoded += String(format: "%%%02X", byte)
            }
        }
        return encoded
    }

    static func looksLikeSessionToken(_ value: String) -> Bool {
        let text = value.trimmingCharacters(in: .whitespacesAndNewlines)
        if text.isEmpty || text.contains(where: { $0.isWhitespace }) { return false }
        let lower = text.lowercased()
        if lower.contains("workoscursorsessiontoken=") || lower.contains("%3a%3a") || text.contains("::") {
            return true
        }
        let parts = text.split(separator: ".", omittingEmptySubsequences: false)
        return parts.count == 3 && parts.allSatisfy { !$0.isEmpty }
    }

    static func queryMap(_ query: String) -> [String: String] {
        var found: [String: String] = [:]
        for part in query.split(separator: "&", omittingEmptySubsequences: true) {
            let piece = String(part)
            let halves = piece.split(separator: "=", maxSplits: 1, omittingEmptySubsequences: false)
            let name = percentDecode(String(halves.first ?? "")).trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
            if name.isEmpty || found[name] != nil { continue }
            let rawValue = halves.count > 1 ? String(halves[1]) : ""
            found[name] = percentDecode(rawValue).trimmingCharacters(in: .whitespacesAndNewlines)
        }
        return found
    }

    static func percentDecode(_ value: String) -> String {
        value.removingPercentEncoding ?? value
    }

    static func normalizedToken(_ value: String) -> String {
        let text = value.trimmingCharacters(in: .whitespacesAndNewlines)
        if text.isEmpty { return "" }
        return (try? Token.normalize(text)) ?? text
    }

    static func pollSessionGet(uuid: String, verifier: String, session: URLSession) async -> LoginPollResult {
        let address = "\(pollURL)?uuid=\(percentEncode(uuid))&verifier=\(percentEncode(verifier))"
        guard let url = URL(string: address) else {
            return LoginPollResult(token: "", pending: false, message: "获取失败")
        }
        var request = URLRequest(url: url)
        request.timeoutInterval = 20
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        request.setValue("ide", forHTTPHeaderField: "x-cursor-client-type")
        request.setValue("Mozilla/5.0 CursorRemain/1.0", forHTTPHeaderField: "User-Agent")
        do {
            let (data, response) = try await session.data(for: request)
            let code = (response as? HTTPURLResponse)?.statusCode ?? 0
            if code == 404 {
                return LoginPollResult(token: "", pending: true, message: "登录尚未完成，请在浏览器登录后再获取")
            }
            if (200..<300).contains(code) {
                let token = tokenFromPollJSON(String(data: data, encoding: .utf8) ?? "")
                if token.isEmpty {
                    return LoginPollResult(token: "", pending: false, message: "登录结果里没有 Cookie")
                }
                return LoginPollResult(token: token, pending: false, message: "")
            }
            return LoginPollResult(token: "", pending: false, message: "获取失败")
        } catch {
            return LoginPollResult(token: "", pending: false, message: "获取失败")
        }
    }
}
