import AppKit
import CursorTokenCore
import Foundation

enum AppUpdater {
    private static var busy = false

    static var isBusy: Bool { busy }

    static var canSelfUpdate: Bool {
        AppUpdate.looksPackagedMacos(Bundle.main.bundlePath)
    }

    @MainActor
    static func run(
        store: AppStore,
        manual: Bool,
        confirmApply: ((String) -> Bool)? = nil
    ) async -> String {
        if busy { return "正在检查更新…" }
        let cfg = store.config
        if !manual && !cfg.autoUpdateEnabled { return "" }
        if !manual && !canSelfUpdate { return "" }
        if !AppUpdate.shouldAutoCheck(lastCheckAt: cfg.updateLastCheckAt, manual: manual) {
            return cfg.updateLastError.isEmpty ? "已是最新版本" : cfg.updateLastError
        }
        busy = true
        defer { busy = false }
        do {
            let release = try await fetchLatest()
            let decision = AppUpdate.evaluate(
                release: release,
                assetName: AppUpdate.macosAssetName,
                currentSha: AppUpdate.currentCommitSha,
                installedSha: cfg.updateInstalledSha,
                installedAssetId: cfg.updateInstalledAssetId
            )
            rememberCheck(store: store, error: decision.upToDate ? "" : (decision.available ? "" : decision.message))
            if decision.upToDate { return decision.message }
            guard decision.available, let asset = decision.asset else {
                return decision.message.isEmpty ? "检查更新失败" : decision.message
            }
            if !canSelfUpdate {
                if manual { openDownloadPage(decision.release?.pageUrl) }
                return decision.message + "。请从下载页安装"
            }
            if manual, let confirmApply, !confirmApply(decision.message) {
                return "已取消更新"
            }
            let staged = try await downloadAndStage(asset)
            if !launchHelper(newApp: staged) {
                let fail = "已下载更新，但无法启动安装脚本"
                rememberCheck(store: store, error: fail)
                return fail
            }
            if let remembered = AppUpdate.rememberedInstallAfterHelper(
                sha: release.commitSha,
                assetId: asset.id,
                helperStarted: true
            ) {
                rememberInstalled(store: store, sha: remembered.sha, assetId: remembered.assetId)
            }
            NSApp.terminate(nil)
            return decision.message + "，即将重启"
        } catch is CancellationError {
            return "已取消更新"
        } catch {
            let msg = "检查更新失败: \((error as? CursorAPIError)?.message ?? error.localizedDescription)"
            rememberCheck(store: store, error: msg)
            if manual && msg.contains("无法检查更新") {
                openDownloadPage()
            }
            return msg
        }
    }

    static func openDownloadPage(_ raw: String? = nil) {
        let url = URL(string: raw ?? "") ?? AppUpdate.latestReleasePageURL
        NSWorkspace.shared.open(url)
    }

    private static func fetchLatest() async throws -> AppRelease {
        do {
            var release = try await getJSON(AppUpdate.apiLatestReleaseURL, parse: AppUpdate.parseRelease, timeout: 10)
            if release.commitSha.isEmpty {
                if let sha = try? await getText(AppUpdate.apiLatestRefURL, accept: "application/vnd.github+json", timeout: 8) {
                    let parsed = AppUpdate.parseTagRefSha(sha)
                    if !parsed.isEmpty { release.commitSha = parsed }
                }
            }
            if release.assets.isEmpty {
                release.assets = AppUpdate.knownAssets()
            }
            return release
        } catch {
            if let status = (error as? CursorAPIError)?.statusCode, !AppUpdate.shouldFallbackFromApi(status), status > 0 {
                throw error
            }
            do {
                let html = try await getText(AppUpdate.latestReleasePageURL, accept: "text/html", timeout: 20)
                return try AppUpdate.parseReleasePage(html)
            } catch {
                throw CursorAPIError("无法检查更新（\(shortError(error))）。也可打开下载页手动安装")
            }
        }
    }

    private static func downloadAndStage(_ asset: AppReleaseAsset) async throws -> URL {
        guard AppUpdate.isAllowedDownloadURL(asset.url), let url = URL(string: asset.url) else {
            throw CursorAPIError("更新地址无效")
        }
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("CursorRemain-update-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        let zip = root.appendingPathComponent(AppUpdate.macosAssetName)
        var req = URLRequest(url: url)
        req.setValue(AppUpdate.userAgent, forHTTPHeaderField: "User-Agent")
        req.setValue("application/octet-stream", forHTTPHeaderField: "Accept")
        let (temp, resp) = try await URLSession.shared.download(for: req)
        if let http = resp as? HTTPURLResponse, !(200...299).contains(http.statusCode) {
            throw CursorAPIError("下载失败 HTTP \(http.statusCode)")
        }
        if let final = resp.url, !AppUpdate.isAllowedDownloadURL(final.absoluteString) {
            throw CursorAPIError("更新地址无效")
        }
        let size = (try? FileManager.default.attributesOfItem(atPath: temp.path)[.size] as? NSNumber)?.int64Value ?? 0
        if size > AppUpdate.maxZipBytes { throw CursorAPIError("安装包过大，已取消更新") }
        if FileManager.default.fileExists(atPath: zip.path) {
            try FileManager.default.removeItem(at: zip)
        }
        try FileManager.default.moveItem(at: temp, to: zip)
        let extract = root.appendingPathComponent("extract", isDirectory: true)
        try FileManager.default.createDirectory(at: extract, withIntermediateDirectories: true)
        try run("/usr/bin/ditto", ["-x", "-k", zip.path, extract.path])
        guard let app = findApp(in: extract) else {
            throw CursorAPIError("安装包里没有 CursorRemain.app")
        }
        return app
    }

    private static func findApp(in directory: URL) -> URL? {
        let fm = FileManager.default
        guard let en = fm.enumerator(at: directory, includingPropertiesForKeys: [.isDirectoryKey]) else { return nil }
        for case let url as URL in en {
            if url.pathExtension.lowercased() == "app",
               AppUpdate.isOurMacApp(url.lastPathComponent) {
                return url
            }
        }
        return nil
    }

    private static func launchHelper(newApp: URL) -> Bool {
        let dest = Bundle.main.bundleURL
        let script = FileManager.default.temporaryDirectory
            .appendingPathComponent("CursorRemain-apply-\(UUID().uuidString).sh")
        let body = """
        #!/bin/bash
        PID="$1"
        SRC="$2"
        DST="$3"
        for i in $(seq 1 80); do
          if ! kill -0 "$PID" 2>/dev/null; then
            break
          fi
          sleep 0.25
        done
        sleep 0.4
        TMP="${DST}.updating"
        rm -rf "$TMP"
        /usr/bin/ditto "$SRC" "$TMP" || exit 1
        if [ -d "$DST" ]; then
          rm -rf "${DST}.old"
          mv "$DST" "${DST}.old" || exit 1
        fi
        mv "$TMP" "$DST" || exit 1
        xattr -cr "$DST" >/dev/null 2>&1 || true
        rm -rf "${DST}.old"
        open "$DST"
        rm -f "$0"
        """
        do {
            try body.write(to: script, atomically: true, encoding: .utf8)
            try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: script.path)
            let proc = Process()
            proc.executableURL = URL(fileURLWithPath: "/bin/bash")
            proc.arguments = [script.path, String(ProcessInfo.processInfo.processIdentifier), newApp.path, dest.path]
            proc.standardOutput = FileHandle.nullDevice
            proc.standardError = FileHandle.nullDevice
            try proc.run()
            return true
        } catch {
            AppLog.log("无法启动更新脚本: \(error.localizedDescription)")
            return false
        }
    }

    @MainActor
    private static func rememberCheck(store: AppStore, error: String) {
        store.config = ConfigStore.update(from: store.settingsDirectory) { live in
            live.updateLastCheckAt = AppUpdate.nowIso()
            live.updateLastError = error
        }
    }

    @MainActor
    private static func rememberInstalled(store: AppStore, sha: String, assetId: Int64) {
        store.config = ConfigStore.update(from: store.settingsDirectory) { live in
            live.updateLastCheckAt = AppUpdate.nowIso()
            live.updateLastError = ""
            live.updateInstalledSha = AppUpdate.normalizeSha(sha)
            live.updateInstalledAssetId = assetId
        }
    }

    private static func getJSON(_ url: URL, parse: (String) throws -> AppRelease, timeout: TimeInterval) async throws -> AppRelease {
        try parse(try await getText(url, accept: "application/vnd.github+json", timeout: timeout))
    }

    private static func getText(_ url: URL, accept: String, timeout: TimeInterval) async throws -> String {
        var req = URLRequest(url: url, timeoutInterval: timeout)
        req.setValue(AppUpdate.userAgent, forHTTPHeaderField: "User-Agent")
        req.setValue(accept, forHTTPHeaderField: "Accept")
        if accept.contains("github") {
            req.setValue("2022-11-28", forHTTPHeaderField: "X-GitHub-Api-Version")
        }
        let (data, resp) = try await URLSession.shared.data(for: req)
        if let http = resp as? HTTPURLResponse, !(200...299).contains(http.statusCode) {
            throw CursorAPIError(AppUpdate.httpStatusMessage(http.statusCode), statusCode: http.statusCode)
        }
        return String(data: data, encoding: .utf8) ?? ""
    }

    private static func shortError(_ error: Error) -> String {
        if let api = error as? CursorAPIError { return api.message }
        let ns = error as NSError
        if ns.domain == NSURLErrorDomain && ns.code == NSURLErrorTimedOut { return "连接超时" }
        return error.localizedDescription
    }

    private static func run(_ launchPath: String, _ arguments: [String]) throws {
        let proc = Process()
        proc.executableURL = URL(fileURLWithPath: launchPath)
        proc.arguments = arguments
        proc.standardOutput = FileHandle.nullDevice
        proc.standardError = FileHandle.nullDevice
        try proc.run()
        proc.waitUntilExit()
        if proc.terminationStatus != 0 {
            throw CursorAPIError("解压更新失败")
        }
    }
}
