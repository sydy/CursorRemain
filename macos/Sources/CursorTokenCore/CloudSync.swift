import Foundation

public enum CloudSyncApi {
    public static let baseUrl = "https://sync.harker.cn"
}

public enum CloudSync {
    public static func applySession(_ cfg: inout AppConfig, email: String, dataPassphrase: String, access: String, refresh: String) {
        cfg.cloudEmail = email.trimmingCharacters(in: .whitespaces).lowercased()
        cfg.cloudAccessToken = access
        cfg.cloudRefreshToken = refresh
        cfg.syncSecret = dataPassphrase
        cfg.syncEnabled = cfg.cloudLoggedIn
        cfg.syncLastError = ""
        cfg.syncSecretDecryptFailed = false
    }

    public static func clearSession(_ cfg: inout AppConfig, keepEmail: Bool = true) {
        if !keepEmail { cfg.cloudEmail = "" }
        cfg.cloudAccessToken = ""
        cfg.cloudRefreshToken = ""
        cfg.syncSecret = ""
        cfg.cloudRevision = 0
        cfg.syncEnabled = false
    }

    public static func register(email: String, password: String) throws {
        _ = try send(method: "POST", path: "/v1/auth/register", access: nil, body: ["email": email, "password": password])
    }

    public static func verifyEmail(email: String, code: String) throws {
        _ = try send(method: "POST", path: "/v1/auth/verify-email", access: nil, body: ["email": email, "code": code])
    }

    public static func resendVerification(email: String, password: String) throws {
        _ = try send(method: "POST", path: "/v1/auth/verification-email", access: nil, body: ["email": email, "password": password])
    }

    public static func requestPasswordReset(email: String) throws {
        _ = try send(method: "POST", path: "/v1/auth/password-reset/request", access: nil, body: ["email": email])
    }

    public static func confirmPasswordReset(email: String, code: String, newPassword: String) throws {
        _ = try send(method: "POST", path: "/v1/auth/password-reset/confirm", access: nil, body: ["email": email, "code": code, "new_password": newPassword])
    }

    public static func login(email: String, password: String) throws -> (email: String, access: String, refresh: String, storage: String) {
        let payload = try send(method: "POST", path: "/v1/auth/login", access: nil, body: ["email": email, "password": password])
        return try readAuth(payload)
    }

    public static func setStorage(_ cfg: inout AppConfig, mode: String) throws {
        let value = AccountSync.normalizeSyncStorage(mode)
        if cfg.cloudLoggedIn {
            _ = try authed(&cfg, method: "PUT", path: "/v1/me/storage", body: ["mode": value])
        }
        cfg.syncStorage = value
    }

    public static func changePassword(_ cfg: inout AppConfig, oldPassword: String, newPassword: String) throws {
        let old = oldPassword.trimmingCharacters(in: .whitespaces)
        let next = newPassword.trimmingCharacters(in: .whitespaces)
        let dataKey = cfg.syncSecret
        if next.count < 8 { throw CursorAPIError("密码至少 8 位") }
        if old == next { throw CursorAPIError("新密码不能与当前密码相同") }
        let got = try authed(&cfg, method: "GET", path: "/v1/sync", body: nil)
        var revision = intValue(got["revision"]) ?? 0
        var body: [String: Any] = [
            "old_password": old,
            "new_password": next,
            "revision": revision,
        ]
        if !AccountSync.usesServerStore(cfg), let envelope = got["envelope"] as? [String: Any] {
            let remote = try AccountSync.decryptEnvelope(envelope, passphrase: dataKey)
            body["envelope"] = try AccountSync.encryptEnvelope(remote, passphrase: dataKey)
        }
        do {
            let put = try authed(&cfg, method: "POST", path: "/v1/auth/password", body: body)
            applyPasswordResult(&cfg, password: dataKey, payload: put, fallbackRevision: revision)
        } catch let err as CursorAPIError where err.statusCode == 409 {
            let retry = try authed(&cfg, method: "GET", path: "/v1/sync", body: nil)
            revision = intValue(retry["revision"]) ?? 0
            body["revision"] = revision
            if !AccountSync.usesServerStore(cfg), let envelope = retry["envelope"] as? [String: Any] {
                let remote = try AccountSync.decryptEnvelope(envelope, passphrase: dataKey)
                body["envelope"] = try AccountSync.encryptEnvelope(remote, passphrase: dataKey)
            }
            let put = try authed(&cfg, method: "POST", path: "/v1/auth/password", body: body)
            applyPasswordResult(&cfg, password: dataKey, payload: put, fallbackRevision: revision)
        }
    }

    public static func changeDataPassphrase(_ cfg: inout AppConfig, newPassphrase: String) throws {
        let next = newPassphrase.trimmingCharacters(in: .whitespaces)
        guard next.count >= AccountSync.minimumDataPassphraseLength else { throw CursorAPIError("数据加密口令至少 16 位") }
        let current = cfg.syncSecret
        guard !current.isEmpty else { throw CursorAPIError("当前设备没有解锁数据加密口令") }
        let got = try authed(&cfg, method: "GET", path: "/v1/sync", body: nil)
        var revision = intValue(got["revision"]) ?? 0
        var remote = try parseRemote(got, passphrase: current)
        var local = AccountSync.snapshotFromConfig(cfg)
        local.deviceId = cfg.syncDeviceId
        var merged = AccountSync.mergeSnapshots(local, remote)
        var envelope = try AccountSync.encryptEnvelope(merged, passphrase: next)
        do {
            let put = try authed(&cfg, method: "PUT", path: "/v1/sync", body: ["revision": revision, "envelope": envelope])
            cfg.cloudRevision = intValue(put["revision"]) ?? (revision + 1)
        } catch let err as CursorAPIError where err.statusCode == 409 {
            let retry = try authed(&cfg, method: "GET", path: "/v1/sync", body: nil)
            revision = intValue(retry["revision"]) ?? 0
            remote = try parseRemote(retry, passphrase: current)
            local = AccountSync.snapshotFromConfig(cfg)
            local.deviceId = cfg.syncDeviceId
            merged = AccountSync.mergeSnapshots(local, remote)
            envelope = try AccountSync.encryptEnvelope(merged, passphrase: next)
            let put = try authed(&cfg, method: "PUT", path: "/v1/sync", body: ["revision": revision, "envelope": envelope])
            cfg.cloudRevision = intValue(put["revision"]) ?? (revision + 1)
        }
        cfg.syncSecret = next
    }

    static func applyPasswordResult(_ cfg: inout AppConfig, password: String, payload: [String: Any], fallbackRevision: Int) {
        let access = str(payload["access_token"])
        let refresh = str(payload["refresh_token"])
        let email = str(payload["email"]).isEmpty ? cfg.cloudEmail : str(payload["email"])
        applySession(&cfg, email: email, dataPassphrase: password, access: access, refresh: refresh)
        cfg.cloudRevision = intValue(payload["revision"]) ?? fallbackRevision
    }

    public static func deleteAccount(_ cfg: inout AppConfig, password: String) throws {
        let secret = password.trimmingCharacters(in: .whitespaces)
        if secret.isEmpty { throw CursorAPIError("请输入密码以确认注销") }
        _ = try authed(&cfg, method: "DELETE", path: "/v1/me", body: ["password": secret])
        clearSession(&cfg, keepEmail: false)
    }

    public static func logout(_ cfg: inout AppConfig) {
        let refresh = cfg.cloudRefreshToken
        if !refresh.isEmpty {
            _ = try? send(method: "POST", path: "/v1/auth/logout", access: nil, body: ["refresh_token": refresh])
        }
        clearSession(&cfg)
    }

    public static func reconcile(_ cfg: inout AppConfig, now: Date? = nil, write: Bool = true) -> SyncStatus {
        var status = SyncStatus(path: CloudSyncApi.baseUrl)
        let reason = AccountSync.syncReady(cfg)
        if !reason.isEmpty {
            status.message = reason
            cfg.syncLastError = reason
            return status
        }
        if AccountSync.usesServerStore(cfg) {
            return reconcileStore(&cfg, status: &status, now: now, write: write)
        }
        _ = AccountSync.ensureDeviceId(&cfg)
        let stamp = AccountSync.nowIso(now)
        let passphrase = cfg.syncSecret
        var local = AccountSync.snapshotFromConfig(cfg)
        local.deviceId = cfg.syncDeviceId
        do {
            let got = try authed(&cfg, method: "GET", path: "/v1/sync", body: nil)
            var revision = intValue(got["revision"]) ?? 0
            let remote = try parseRemote(got, passphrase: passphrase)
            let merged = AccountSync.mergeSnapshots(local, remote)
            var changed = AccountSync.applySnapshotToConfig(&cfg, merged)
            if write && AccountSync.snapshotIdentity(merged) != AccountSync.snapshotIdentity(remote) {
                let put = try putMerged(
                    &cfg,
                    merged: merged,
                    remote: remote,
                    revision: revision,
                    stamp: stamp,
                    passphrase: passphrase
                )
                changed = put.changed || changed
                revision = put.revision
                if put.pushed {
                    cfg.cloudRevision = put.revision
                    status.pushed = true
                } else {
                    cfg.cloudRevision = revision
                }
            } else {
                cfg.cloudRevision = revision
            }
            cfg.syncLastAt = stamp
            status.ok = true
            status.changed = changed
            if changed && status.pushed { status.message = "已合并并对齐云端" }
            else if changed { status.message = "已从云端导入账号、设置和用量" }
            else if status.pushed { status.message = "已上传到云端" }
            else { status.message = "账号、设置和用量已与云端一致" }
            let note = status.pushed ? AccountSync.trimNote(merged) : ""
            status.message = AccountSync.appendTrimNote(status.message, note)
            cfg.syncLastError = note
            return status
        } catch let err as CursorAPIError where err.statusCode == 401 {
            clearSession(&cfg)
            status.message = "登录已过期，请重新登录"
            cfg.syncLastError = status.message
            return status
        } catch {
            let message = (error as? CursorAPIError)?.message ?? error.localizedDescription
            AppLog.log("云同步失败: \(message.isEmpty ? "同步失败" : message)")
            cfg.syncLastError = message.isEmpty ? "同步失败" : message
            status.message = cfg.syncLastError
            return status
        }
    }

    static func putMerged(
        _ cfg: inout AppConfig,
        merged: SyncSnapshot,
        remote: SyncSnapshot,
        revision: Int,
        stamp: String,
        passphrase: String
    ) throws -> (pushed: Bool, changed: Bool, revision: Int) {
        var merged = merged
        var remote = remote
        var revision = revision
        merged.updatedAt = stamp
        merged.deviceId = cfg.syncDeviceId
        let envelope = try AccountSync.encryptEnvelope(merged, passphrase: passphrase)
        do {
            let put = try authed(&cfg, method: "PUT", path: "/v1/sync", body: ["revision": revision, "envelope": envelope])
            return (true, false, intValue(put["revision"]) ?? (revision + 1))
        } catch let err as CursorAPIError where err.statusCode == 409 {
            let got = try authed(&cfg, method: "GET", path: "/v1/sync", body: nil)
            revision = intValue(got["revision"]) ?? 0
            remote = try parseRemote(got, passphrase: passphrase)
            var local = AccountSync.snapshotFromConfig(cfg)
            local.deviceId = cfg.syncDeviceId
            merged = AccountSync.mergeSnapshots(local, remote)
            let changed = AccountSync.applySnapshotToConfig(&cfg, merged)
            if AccountSync.snapshotIdentity(merged) == AccountSync.snapshotIdentity(remote) {
                return (false, changed, revision)
            }
            merged.updatedAt = stamp
            merged.deviceId = cfg.syncDeviceId
            let retryEnvelope = try AccountSync.encryptEnvelope(merged, passphrase: passphrase)
            let put = try authed(&cfg, method: "PUT", path: "/v1/sync", body: ["revision": revision, "envelope": retryEnvelope])
            return (true, changed, intValue(put["revision"]) ?? (revision + 1))
        }
    }

    static func parseRemote(_ payload: [String: Any], passphrase: String) throws -> SyncSnapshot {
        guard let envelope = payload["envelope"] as? [String: Any] else { return SyncSnapshot() }
        return try AccountSync.decryptEnvelope(envelope, passphrase: passphrase)
    }

    static func reconcileStore(_ cfg: inout AppConfig, status: inout SyncStatus, now: Date?, write: Bool) -> SyncStatus {
        _ = AccountSync.ensureDeviceId(&cfg)
        let stamp = AccountSync.nowIso(now)
        var local = AccountSync.snapshotFromConfig(cfg)
        local.deviceId = cfg.syncDeviceId
        do {
            let got = try authed(&cfg, method: "GET", path: "/v1/store", body: nil)
            var revision = intValue(got["revision"]) ?? 0
            let remote = (got["snapshot"] as? [String: Any]).map(AccountSync.parseSnapshot) ?? SyncSnapshot()
            var merged = AccountSync.mergeSnapshots(local, remote)
            var changed = AccountSync.applySnapshotToConfig(&cfg, merged)
            if write && AccountSync.snapshotIdentity(merged) != AccountSync.snapshotIdentity(remote) {
                merged.updatedAt = stamp
                merged.deviceId = cfg.syncDeviceId
                let put = try putStore(&cfg, merged: merged, revision: revision, stamp: stamp)
                changed = put.changed || changed
                revision = put.revision
                status.pushed = put.pushed
            }
            cfg.cloudRevision = revision
            cfg.syncLastAt = stamp
            cfg.syncStorage = "server"
            status.ok = true
            status.changed = changed
            if changed && status.pushed { status.message = "已合并并对齐云端" }
            else if changed { status.message = "已从云端导入账号、设置和用量" }
            else if status.pushed { status.message = "已上传到云端" }
            else { status.message = "账号、设置和用量已与云端一致" }
            cfg.syncLastError = ""
            return status
        } catch let err as CursorAPIError where err.statusCode == 401 {
            clearSession(&cfg)
            status.message = "登录已过期，请重新登录"
            cfg.syncLastError = status.message
            return status
        } catch {
            let message = (error as? CursorAPIError)?.message ?? error.localizedDescription
            AppLog.log("云同步失败: \(message.isEmpty ? "同步失败" : message)")
            cfg.syncLastError = message.isEmpty ? "同步失败" : message
            status.message = cfg.syncLastError
            return status
        }
    }

    static func putStore(_ cfg: inout AppConfig, merged: SyncSnapshot, revision: Int, stamp: String) throws -> (pushed: Bool, changed: Bool, revision: Int) {
        func body(_ snap: SyncSnapshot, revision: Int) throws -> [String: Any] {
            let raw = AccountSync.canonicalJSON(snap)
            let snapshot = try JSONSerialization.jsonObject(with: Data(raw.utf8))
            return ["revision": revision, "snapshot": snapshot]
        }
        var merged = merged
        merged.updatedAt = stamp
        merged.deviceId = cfg.syncDeviceId
        do {
            let put = try authed(&cfg, method: "PUT", path: "/v1/store", body: try body(merged, revision: revision))
            return (true, false, intValue(put["revision"]) ?? (revision + 1))
        } catch let err as CursorAPIError where err.statusCode == 409 {
            let got = try authed(&cfg, method: "GET", path: "/v1/store", body: nil)
            let nextRevision = intValue(got["revision"]) ?? 0
            let remote = (got["snapshot"] as? [String: Any]).map(AccountSync.parseSnapshot) ?? SyncSnapshot()
            var local = AccountSync.snapshotFromConfig(cfg)
            local.deviceId = cfg.syncDeviceId
            merged = AccountSync.mergeSnapshots(local, remote)
            let changed = AccountSync.applySnapshotToConfig(&cfg, merged)
            if AccountSync.snapshotIdentity(merged) == AccountSync.snapshotIdentity(remote) {
                return (false, changed, nextRevision)
            }
            merged.updatedAt = stamp
            merged.deviceId = cfg.syncDeviceId
            let put = try authed(&cfg, method: "PUT", path: "/v1/store", body: try body(merged, revision: nextRevision))
            return (true, changed, intValue(put["revision"]) ?? (nextRevision + 1))
        }
    }

    static func readAuth(_ payload: [String: Any]) throws -> (email: String, access: String, refresh: String, storage: String) {
        let access = str(payload["access_token"])
        let refresh = str(payload["refresh_token"])
        let email = str(payload["email"])
        if access.isEmpty { throw CursorAPIError("登录失败") }
        return (email, access, refresh, str(payload["sync_storage"]))
    }

    static func authed(_ cfg: inout AppConfig, method: String, path: String, body: [String: Any]?) throws -> [String: Any] {
        do {
            return try send(method: method, path: path, access: cfg.cloudAccessToken, body: body)
        } catch let err as CursorAPIError where err.statusCode == 401 {
            if !refresh(&cfg) { throw err }
            return try send(method: method, path: path, access: cfg.cloudAccessToken, body: body)
        }
    }

    static func refresh(_ cfg: inout AppConfig) -> Bool {
        let token = cfg.cloudRefreshToken.trimmingCharacters(in: .whitespaces)
        if token.isEmpty { return false }
        do {
            let payload = try send(method: "POST", path: "/v1/auth/refresh", access: nil, body: ["refresh_token": token])
            cfg.cloudAccessToken = str(payload["access_token"])
            if let next = payload["refresh_token"] as? String, !next.isEmpty {
                cfg.cloudRefreshToken = next
            }
            return !cfg.cloudAccessToken.isEmpty
        } catch {
            AppLog.log("刷新同步登录失败: \(error.localizedDescription)")
            clearSession(&cfg)
            return false
        }
    }

    static var testSend: ((String, String, String?, [String: Any]?) throws -> [String: Any])?

    static func send(method: String, path: String, access: String?, body: [String: Any]?) throws -> [String: Any] {
        if let testSend {
            return try testSend(method, path, access, body)
        }
        guard let url = URL(string: CloudSyncApi.baseUrl.trimmingCharacters(in: CharacterSet(charactersIn: "/")) + path) else {
            throw CursorAPIError("无法连接同步服务器")
        }
        var req = URLRequest(url: url, timeoutInterval: 60)
        req.httpMethod = method
        req.setValue("application/json", forHTTPHeaderField: "Accept")
        if let access, !access.isEmpty {
            req.setValue("Bearer \(access)", forHTTPHeaderField: "Authorization")
        }
        if let body {
            req.setValue("application/json", forHTTPHeaderField: "Content-Type")
            req.httpBody = try JSONSerialization.data(withJSONObject: body)
        }
        let box = RequestBox()
        let sem = DispatchSemaphore(value: 0)
        if Thread.isMainThread {
            throw CursorAPIError("同步不能在主线程等待网络")
        }
        URLSession.shared.dataTask(with: req) { data, response, error in
            box.data = data
            box.response = response
            box.error = error
            sem.signal()
        }.resume()
        _ = sem.wait(timeout: .now() + 70)
        if box.error != nil { throw CursorAPIError("无法连接同步服务器") }
        let status = (box.response as? HTTPURLResponse)?.statusCode ?? 0
        let obj = (try? JSONSerialization.jsonObject(with: box.data ?? Data())) as? [String: Any] ?? [:]
        if status >= 400 {
            let detail = str(obj["detail"])
            throw CursorAPIError(detail.isEmpty ? "同步失败" : detail, statusCode: status)
        }
        return obj
    }

    static func str(_ value: Any?) -> String {
        (value as? String) ?? ""
    }

    static func intValue(_ value: Any?) -> Int? {
        if let n = value as? Int { return n }
        if let n = value as? NSNumber { return n.intValue }
        return nil
    }
}

private final class RequestBox: @unchecked Sendable {
    var data: Data?
    var response: URLResponse?
    var error: Error?
}
