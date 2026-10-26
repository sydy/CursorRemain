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
        let text = (raw ?? "").trimmingCharacters(in: .whitespaces).lowercased()
        if !looksLikeEmail(text) { return "" }
        let range = NSRange(text.startIndex..., in: text)
        if emailRe.firstMatch(in: text, range: range) == nil { return "" }
        return text
    }

    public static func defaultLabel(_ email: String, existing: String = "") -> String {
        let current = existing.trimmingCharacters(in: .whitespaces)
        if !current.isEmpty { return current }
        return sanitizeEmail(email)
    }

    public static func tokenFromCookies(_ cookies: [(name: String, value: String)]) -> String? {
        for item in cookies {
            if item.name.caseInsensitiveCompare(Token.cookieName) != .orderedSame { continue }
            let raw = item.value.trimmingCharacters(in: .whitespaces)
            if raw.isEmpty { return "" }
            return (try? Token.normalize(raw)) ?? raw
        }
        return ""
    }

    public static func autofillScript(email: String, password: String) -> String {
        let payloadObj: [String: String] = [
            "email": sanitizeEmail(email),
            "password": password,
        ]
        let data = (try? JSONSerialization.data(withJSONObject: payloadObj, options: [])) ?? Data("{}".utf8)
        let payload = String(data: data, encoding: .utf8) ?? "{}"
        return
            "(function(){" +
            "const c=\(payload);" +
            "function setNative(el,val){" +
            "if(!el)return;" +
            "const proto=el.tagName==='TEXTAREA'?window.HTMLTextAreaElement.prototype:window.HTMLInputElement.prototype;" +
            "const desc=Object.getOwnPropertyDescriptor(proto,'value');" +
            "if(desc&&desc.set)desc.set.call(el,val);else el.value=val;" +
            "el.dispatchEvent(new Event('input',{bubbles:true}));" +
            "el.dispatchEvent(new Event('change',{bubbles:true}));" +
            "}" +
            "if(document.querySelector('iframe[src*=\"challenges.cloudflare\"],iframe[src*=\"turnstile\"],input[autocomplete=\"one-time-code\"]'))" +
            "return 'need-user';" +
            "const emailEl=document.querySelector('input[type=\"email\"],input[name=\"email\"],input[autocomplete=\"username\"],input[autocomplete=\"email\"]');" +
            "if(emailEl&&c.email)setNative(emailEl,c.email);" +
            "const passEl=document.querySelector('input[type=\"password\"]');" +
            "if(passEl&&c.password)setNative(passEl,c.password);" +
            "const buttons=[...document.querySelectorAll('button,[type=submit]')];" +
            "const go=buttons.find(b=>/continue|sign in|log in|登录|继续/i.test((b.innerText||b.value||'')));" +
            "if(go&&!window.__cttClicked){window.__cttClicked=Date.now();go.click();return 'clicked';}" +
            "return passEl&&c.password?'filled':'waiting';" +
            "})();"
    }

    public static func codeChallenge(_ verifier: String) -> String {
        let text = verifier.trimmingCharacters(in: .whitespacesAndNewlines)
        if text.isEmpty { return "" }
        let digest = Data(SHA256.hash(data: Data(text.utf8)))
        return digest.base64EncodedString()
            .replacingOccurrences(of: "+", with: "-")
            .replacingOccurrences(of: "/", with: "_")
            .replacingOccurrences(of: "=", with: "")
    }

    public static func loginDeepURL(uuid: String, verifier: String) -> String {
        let loginId = uuid.trimmingCharacters(in: .whitespacesAndNewlines)
        let challenge = codeChallenge(verifier)
        if loginId.isEmpty || challenge.isEmpty { return "" }
        return "\(loginDeepURLBase)?challenge=\(percentEncode(challenge))&uuid=\(percentEncode(loginId))&mode=login"
    }

    public static func tokenFromCallback(_ text: String?) -> String {
        let raw = (text ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        if raw.isEmpty { return "" }
        if !raw.contains("://"), !raw.lowercased().hasPrefix("cursor.com") {
            if !looksLikeSessionToken(raw) { return "" }
            return normalizedToken(raw)
        }
        let parts = raw.split(separator: "#", maxSplits: 1, omittingEmptySubsequences: false)
        let main = String(parts.first ?? "")
        let fragment = parts.count > 1 ? String(parts[1]) : ""
        let query = main.split(separator: "?", maxSplits: 1, omittingEmptySubsequences: false).dropFirst().first.map(String.init) ?? ""
        var found = queryMap(query)
        for (key, value) in queryMap(fragment) where found[key] == nil {
            found[key] = value
        }
        for key in callbackKeys {
            let value = found[key] ?? ""
            if !value.isEmpty, looksLikeSessionToken(value) {
                return normalizedToken(value)
            }
        }
        return ""
    }

    public static func tokenFromPoll(_ payload: [String: Any]) -> String {
        let camel = payload["accessToken"] as? String ?? ""
        let snake = payload["access_token"] as? String ?? ""
        let raw = camel.trimmingCharacters(in: .whitespacesAndNewlines)
        let fallback = snake.trimmingCharacters(in: .whitespacesAndNewlines)
        let token = raw.isEmpty ? fallback : raw
        if token.isEmpty { return "" }
        return normalizedToken(token)
    }

    public static func validProfileId(_ value: String?) -> String {
        let text = (value ?? "").trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        let pattern = #"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"#
        return text.range(of: pattern, options: .regularExpression) == nil ? "" : text
    }

    public static func profileKey(email: String, accountId: String = "") -> String {
        let mail = sanitizeEmail(email)
        if !mail.isEmpty { return "email:" + mail }
        let ident = accountId.trimmingCharacters(in: .whitespacesAndNewlines)
        if ident.isEmpty || ident.contains(where: { $0.isWhitespace || $0 == "/" || $0 == "\\" }) { return "" }
        return "id:" + String(ident.prefix(120))
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
        let fresh = validProfileId(newId)
        var cleaned = cleanProfileIndex(index)
        if fresh.isEmpty { return (cleaned, "") }
        let name = canonicalProfileKey(key)
        if name.isEmpty { return (cleaned, fresh) }
        if let current = cleaned[name], !current.isEmpty { return (cleaned, current) }
        cleaned[name] = fresh
        return (cleaned, fresh)
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
