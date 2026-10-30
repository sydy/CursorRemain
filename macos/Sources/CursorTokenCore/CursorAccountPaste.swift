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
    public static func looksLikeToken(_ line: String?) -> Bool {
        RustCore.bool("looks_like_token", ["line": line ?? ""])
    }

    public static func isSingleToken(_ text: String?) -> Bool {
        RustCore.bool("is_single_token", ["text": text ?? ""])
    }

    public static func tokenValues(_ text: String?) -> [String] {
        RustCore.strings("token_values", ["text": text ?? ""])
    }

    public static func parse(_ text: String?) -> [AccountPasteItem] {
        let rows = RustCore.call("parse_account_paste", ["text": text ?? ""]) as? [[String: Any]] ?? []
        return rows.map { row in
            AccountPasteItem(kind: row["kind"] as? String ?? "", token: row["token"] as? String ?? "", email: row["email"] as? String ?? "", password: row["password"] as? String ?? "", message: row["message"] as? String ?? "")
        }
    }
}
