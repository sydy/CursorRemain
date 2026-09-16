import Foundation

#if canImport(CryptoKit) && canImport(Security)
import CryptoKit
import Security

/// Encrypts session tokens at rest with AES-GCM. The wrap key lives in the
/// macOS keychain (service `com.harker.cursortokentray`). If the keychain is
/// unavailable Protect throws so a save never replaces ciphertext with plaintext.
///
/// Release builds are ad-hoc signed, so a default keychain ACL binds to that
/// build's CDHash. 「始终允许」then expires on the next auto-update. New items
/// use an ACL that any application may read, and a v1 item is copied to v2
/// after one successful unlock instead of minting a replacement key.
public enum TokenProtector {
    public static let prefix = "enc:v1:"
    public static let decryptFailedMessage = "Token 解密失败，请重新导入"
    public static let service = "com.harker.cursortokentray"
    static let keyAccount = "wrap-key-v2"
    static let legacyKeyAccount = "wrap-key-v1"
    static let accessDescriptor = "Cursor 余量 Token 密钥"

    public static func isProtected(_ value: String) -> Bool {
        value.hasPrefix(prefix)
    }

    public static func protect(_ plaintext: String) throws -> String {
        let value = plaintext.trimmingCharacters(in: .whitespacesAndNewlines)
        if value.isEmpty || isProtected(value) { return plaintext }
        guard let key = wrapKey() else {
            throw CursorAPIError("无法使用钥匙串加密 Token，配置未写入")
        }
        let sealed = try AES.GCM.seal(Data(value.utf8), using: key)
        guard let combined = sealed.combined else {
            throw CursorAPIError("无法使用钥匙串加密 Token，配置未写入")
        }
        return prefix + combined.base64EncodedString()
    }

    /// Never returns an `enc:v1:` blob as if it were a session token.
    public static func unprotect(_ stored: String) -> String {
        tryUnprotect(stored).value
    }

    public static func tryUnprotect(_ stored: String) -> (value: String, ok: Bool) {
        if stored.isEmpty || !isProtected(stored) { return (stored, true) }
        let b64 = String(stored.dropFirst(prefix.count))
        guard let data = Data(base64Encoded: b64), let key = wrapKey() else {
            return ("", false)
        }
        do {
            let box = try AES.GCM.SealedBox(combined: data)
            let opened = try AES.GCM.open(box, using: key)
            return (String(data: opened, encoding: .utf8) ?? "", true)
        } catch {
            return ("", false)
        }
    }

    public static func diskToken(plaintext: String, storedRaw: String, decryptFailed: Bool) throws -> String {
        if decryptFailed && isProtected(storedRaw) { return storedRaw }
        return try protect(plaintext)
    }

    enum KeyLookup: Equatable {
        case found
        case missing
        case unavailable
    }

    static func classifyRead(_ status: OSStatus) -> KeyLookup {
        switch status {
        case errSecSuccess: return .found
        case errSecItemNotFound: return .missing
        default: return .unavailable
        }
    }

    static func shouldMintNewKey(current: KeyLookup, legacy: KeyLookup) -> Bool {
        current == .missing && legacy == .missing
    }

    static func wrapKey() -> SymmetricKey? {
        let current = readKey(account: keyAccount)
        if case .found(let key) = current { return key }

        let legacy = readKey(account: legacyKeyAccount)
        if case .found(let key) = legacy {
            _ = storeKey(key)
            return key
        }

        guard shouldMintNewKey(current: current.lookup, legacy: legacy.lookup) else { return nil }
        let key = SymmetricKey(size: .bits256)
        return storeKey(key) ? key : nil
    }

    enum KeyMaterial {
        case found(SymmetricKey)
        case missing
        case unavailable

        var lookup: KeyLookup {
            switch self {
            case .found: return .found
            case .missing: return .missing
            case .unavailable: return .unavailable
            }
        }
    }

    static func readKey(account: String) -> KeyMaterial {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne,
        ]
        var item: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &item)
        switch classifyRead(status) {
        case .found:
            guard let data = item as? Data, data.count == 32 else { return .missing }
            return .found(SymmetricKey(data: data))
        case .missing:
            return .missing
        case .unavailable:
            return .unavailable
        }
    }

    static func storeKey(_ key: SymmetricKey) -> Bool {
        let data = key.withUnsafeBytes { Data($0) }
        let access = makeOpenAccess()
        let match: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: keyAccount,
        ]
        var attrs: [String: Any] = [
            kSecValueData as String: data,
            kSecAttrLabel as String: "Cursor 余量",
        ]
        if let access {
            attrs[kSecAttrAccess as String] = access
        }
        let updated = SecItemUpdate(match as CFDictionary, attrs as CFDictionary)
        if updated == errSecSuccess { return true }
        if updated != errSecItemNotFound { return false }
        var add = match
        add.merge(attrs) { _, new in new }
        return SecItemAdd(add as CFDictionary, nil) == errSecSuccess
    }

    /// Empty trusted list + cleared ACL prompts: any later ad-hoc build can
    /// read the wrap key without another login-keychain password dialog.
    static func makeOpenAccess() -> SecAccess? {
        var access: SecAccess?
        let trusted = [] as CFArray
        guard SecAccessCreate(accessDescriptor as CFString, trusted, &access) == errSecSuccess,
              let access
        else { return nil }
        openAllACLs(access)
        return access
    }

    static func openAllACLs(_ access: SecAccess) {
        var list: CFArray?
        guard SecAccessCopyACLList(access, &list) == errSecSuccess,
              let acls = list as? [SecACL]
        else { return }
        for acl in acls {
            var apps: CFArray?
            var descriptor: CFString?
            var selector = SecKeychainPromptSelector()
            guard SecACLCopyContents(acl, &apps, &descriptor, &selector) == errSecSuccess else {
                continue
            }
            let desc = (descriptor as String?) ?? accessDescriptor
            _ = SecACLSetContents(acl, nil, desc as CFString, SecKeychainPromptSelector())
        }
    }
}

#else

public enum TokenProtector {
    public static let prefix = "enc:v1:"
    public static let decryptFailedMessage = "Token 解密失败，请重新导入"

    public static func isProtected(_ value: String) -> Bool { value.hasPrefix(prefix) }
    public static func protect(_ plaintext: String) throws -> String { plaintext }
    public static func unprotect(_ stored: String) -> String { tryUnprotect(stored).value }
    public static func tryUnprotect(_ stored: String) -> (value: String, ok: Bool) {
        if isProtected(stored) { return ("", false) }
        return (stored, true)
    }
    public static func diskToken(plaintext: String, storedRaw: String, decryptFailed: Bool) throws -> String {
        if decryptFailed && isProtected(storedRaw) { return storedRaw }
        return try protect(plaintext)
    }
}

#endif
