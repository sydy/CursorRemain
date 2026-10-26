import CursorTokenCore
import Foundation

enum LoginProfileStore {
    static func fileURL() -> URL {
        AppPaths.configDirectory().appendingPathComponent("login-profiles.json")
    }

    static func load() -> [String: String] {
        guard let data = try? Data(contentsOf: fileURL()),
              let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
        else { return [:] }
        var index: [String: String] = [:]
        for (key, value) in obj {
            index[key] = value as? String ?? ""
        }
        return CursorPasswordLogin.cleanProfileIndex(index)
    }

    static func save(_ index: [String: String]) {
        let cleaned = CursorPasswordLogin.cleanProfileIndex(index)
        let url = fileURL()
        try? FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        guard let data = try? JSONSerialization.data(withJSONObject: cleaned, options: [.sortedKeys]) else { return }
        try? data.write(to: url, options: .atomic)
    }

    static func assign(email: String, accountId: String = "") -> String {
        let current = load()
        let key = CursorPasswordLogin.profileKey(email: email, accountId: accountId)
        let assigned = CursorPasswordLogin.assignProfile(current, key: key, newId: UUID().uuidString.lowercased())
        if assigned.index != current { save(assigned.index) }
        return assigned.profileId
    }

    static func bind(email: String, token: String, profileId: String) {
        var ident = CursorPasswordLogin.validProfileId(profileId)
        if ident.isEmpty { ident = UUID().uuidString.lowercased() }
        var index = load()
        let keys = [
            CursorPasswordLogin.profileKey(email: email),
            CursorPasswordLogin.profileKey(email: Token.jwtClaim(token, "email")),
            CursorPasswordLogin.profileKey(email: "", accountId: Token.accountId(from: token)),
        ]
        for key in keys where !key.isEmpty {
            index = CursorPasswordLogin.bindProfile(index, key: key, profileId: ident)
        }
        save(index)
    }
}
