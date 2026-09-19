import Foundation

public struct AccountPasteItem: Equatable {
    public let kind: String
    public let token: String
    public let email: String
    public let password: String
    public let message: String

    public init(kind: String, token: String = "", email: String = "", password: String = "", message: String = "") {
        self.kind = kind
        self.token = token
        self.email = email
        self.password = password
        self.message = message
    }
}

public enum CursorAccountPaste {
    static let emailToken = #"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}"#
    static let labeledBoth = regex(#"^(?:账号|帐号|账户|邮箱|用户名)\s*[：:]\s*("# + emailToken + #")\s*密码\s*[：:]\s*(.+)$"#)
    static let labeledEmail = regex(#"^(?:账号|帐号|账户|邮箱|用户名)\s*[：:]\s*("# + emailToken + #")\s*$"#)
    static let labeledPassword = regex(#"^密码\s*[：:]\s*(.+)$"#)
    static let emailAtStart = regex(#"^("# + emailToken + #")(.*)$"#)

    public static func looksLikeToken(_ line: String?) -> Bool {
        let text = (line ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        if text.isEmpty { return false }
        let lower = text.lowercased()
        if lower.contains("workoscursorsessiontoken=") { return true }
        if lower.contains("%3a%3a") || text.contains("::") { return true }
        let parts = text.split(separator: ".", omittingEmptySubsequences: false)
        return parts.count == 3 && parts.allSatisfy { !$0.isEmpty }
    }

    public static func isSingleToken(_ text: String?) -> Bool {
        let items = parse(text)
        return items.count == 1 && items[0].kind == "token"
    }

    public static func tokenValues(_ text: String?) -> [String] {
        let items = parse(text)
        guard !items.isEmpty, items.allSatisfy({ $0.kind == "token" }) else { return [] }
        return items.map(\.token).filter { !$0.isEmpty }
    }

    public static func parse(_ text: String?) -> [AccountPasteItem] {
        let raw = (text ?? "").replacingOccurrences(of: "\r\n", with: "\n").replacingOccurrences(of: "\r", with: "\n")
        var items: [AccountPasteItem] = []
        var pendingEmail = ""

        func flushPending() {
            guard !pendingEmail.isEmpty else { return }
            items.append(AccountPasteItem(kind: "error", email: pendingEmail, message: "只有账号没有密码"))
            pendingEmail = ""
        }

        for rawLine in raw.split(separator: "\n", omittingEmptySubsequences: false) {
            let line = rawLine.trimmingCharacters(in: .whitespacesAndNewlines)
            if line.isEmpty { continue }

            if let both = parseLabeledBoth(line) {
                flushPending()
                items.append(AccountPasteItem(kind: "credentials", email: both.0, password: both.1))
                continue
            }

            let emailOnly = parseLabeledEmail(line)
            if !emailOnly.isEmpty {
                flushPending()
                pendingEmail = emailOnly
                continue
            }

            if let passwordOnly = parseLabeledPassword(line) {
                if !pendingEmail.isEmpty && !passwordOnly.isEmpty {
                    items.append(AccountPasteItem(kind: "credentials", email: pendingEmail, password: passwordOnly))
                    pendingEmail = ""
                } else if !pendingEmail.isEmpty {
                    flushPending()
                } else {
                    items.append(AccountPasteItem(kind: "error", message: "只有密码没有账号"))
                }
                continue
            }

            if let separated = parseEmailSeparator(line) {
                flushPending()
                items.append(AccountPasteItem(kind: "credentials", email: separated.0, password: separated.1))
                continue
            }

            if looksLikeToken(line) {
                flushPending()
                items.append(AccountPasteItem(kind: "token", token: line))
                continue
            }

            flushPending()
            items.append(AccountPasteItem(kind: "error", message: "无法识别"))
        }

        flushPending()
        return items
    }

    static func regex(_ pattern: String) -> NSRegularExpression {
        try! NSRegularExpression(pattern: pattern)
    }

    static func firstMatch(_ re: NSRegularExpression, _ text: String) -> NSTextCheckingResult? {
        re.firstMatch(in: text, range: NSRange(text.startIndex..., in: text))
    }

    static func group(_ match: NSTextCheckingResult, _ index: Int, in text: String) -> String {
        guard let range = Range(match.range(at: index), in: text) else { return "" }
        return String(text[range])
    }

    static func parseLabeledBoth(_ line: String) -> (String, String)? {
        guard let match = firstMatch(labeledBoth, line) else { return nil }
        let email = CursorPasswordLogin.sanitizeEmail(group(match, 1, in: line))
        let password = group(match, 2, in: line).trimmingCharacters(in: .whitespacesAndNewlines)
        if email.isEmpty || password.isEmpty { return nil }
        return (email, password)
    }

    static func parseLabeledEmail(_ line: String) -> String {
        guard let match = firstMatch(labeledEmail, line) else { return "" }
        return CursorPasswordLogin.sanitizeEmail(group(match, 1, in: line))
    }

    static func parseLabeledPassword(_ line: String) -> String? {
        guard let match = firstMatch(labeledPassword, line) else { return nil }
        return group(match, 1, in: line).trimmingCharacters(in: .whitespacesAndNewlines)
    }

    static func parseEmailSeparator(_ line: String) -> (String, String)? {
        guard let match = firstMatch(emailAtStart, line) else { return nil }
        let email = CursorPasswordLogin.sanitizeEmail(group(match, 1, in: line))
        if email.isEmpty { return nil }
        let rest = group(match, 2, in: line)
        if rest.isEmpty { return nil }
        let stripped = rest.drop { $0 == " " || $0 == "\t" }
        let password: String
        if stripped.hasPrefix("----") {
            password = String(stripped.dropFirst(4)).trimmingCharacters(in: .whitespacesAndNewlines)
        } else if stripped.first == ":" || stripped.first == "：" {
            password = String(stripped.dropFirst()).trimmingCharacters(in: .whitespacesAndNewlines)
        } else if rest.first == " " || rest.first == "\t" {
            password = rest.trimmingCharacters(in: .whitespacesAndNewlines)
        } else {
            return nil
        }
        if password.isEmpty { return nil }
        return (email, password)
    }
}
