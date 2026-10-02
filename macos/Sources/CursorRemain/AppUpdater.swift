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
                installedAssetId: cfg.updateInstalledAssetId,
                currentVersion: AppUpdate.productVersion
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
            if !(await launchHelper(newApp: staged, expectedSha: release.commitSha)) {
                let fail = "已下载更新，但无法启动安装脚本"
                rememberCheck(store: store, error: fail)
                return fail
            }
            if let pending = AppUpdate.rememberedInstallAfterHelper(
                sha: release.commitSha,
                assetId: asset.id,
                helperStarted: true
            ) {
                AppUpdate.writePendingInstall(directory: pendingDirectory(store), sha: pending.sha, assetId: pending.assetId)
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
        let url = URL(string: raw ?? "") ?? AppUpdate.officialLatestPageURL
        NSWorkspace.shared.open(url)
    }

    private static func fetchLatest() async throws -> AppRelease {
        var apiError: Error?
        var official: AppRelease?
        var rolling: AppRelease?
        do {
            official = try await fetchApiRelease(AppUpdate.apiOfficialLatestURL)
        } catch {
            if let status = (error as? CursorAPIError)?.statusCode, status != 404, !AppUpdate.shouldFallbackFromApi(status), status > 0 {
                throw error
            }
            if let status = (error as? CursorAPIError)?.statusCode, status != 404 {
                apiError = error
            }
        }
        do {
            var release = try await fetchApiRelease(AppUpdate.apiLatestReleaseURL)
            if release.commitSha.isEmpty {
                if let sha = try? await getText(AppUpdate.apiLatestRefURL, accept: "application/vnd.github+json", timeout: 8) {
                    let parsed = AppUpdate.parseTagRefSha(sha)
                    if !parsed.isEmpty { release.commitSha = parsed }
                }
            }
            rolling = release
        } catch {
            if apiError == nil, let status = (error as? CursorAPIError)?.statusCode, status != 404 {
                apiError = error
            } else if apiError == nil {
                apiError = error
            }
        }
        if let chosen = AppUpdate.chooseRelease(official: official, rolling: rolling, currentVersion: AppUpdate.productVersion) {
            return chosen
        }
        do {
            do {
                let html = try await getText(AppUpdate.officialLatestPageURL, accept: "text/html", timeout: 20)
                return try AppUpdate.parseReleasePage(html)
            } catch {
                let html = try await getText(AppUpdate.latestReleasePageURL, accept: "text/html", timeout: 20)
                return try AppUpdate.parseReleasePage(html)
            }
        } catch {
            throw CursorAPIError("无法检查更新（\(shortError(apiError ?? error))）。也可打开下载页手动安装")
        }
    }

    private static func fetchApiRelease(_ url: URL) async throws -> AppRelease {
        var release = try await getJSON(url, parse: AppUpdate.parseRelease, timeout: 10)
        if release.assets.isEmpty {
            release.assets = AppUpdate.knownAssets(release.tag)
        }
        return release
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
        try await Task.detached(priority: .userInitiated) {
            try Self.run("/usr/bin/ditto", ["-x", "-k", zip.path, extract.path])
        }.value
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

    @MainActor
    static func confirmPending(store: AppStore) {
        let dir = pendingDirectory(store)
        guard let pending = AppUpdate.readPendingInstall(directory: dir) else { return }
        if let confirmed = AppUpdate.confirmPendingInstall(currentSha: AppUpdate.currentCommitSha, pending: pending) {
            AppUpdate.clearPendingInstall(directory: dir)
            rememberInstalled(store: store, sha: confirmed.sha, assetId: confirmed.assetId)
            return
        }
        if AppUpdate.pendingInstallFailed(currentSha: AppUpdate.currentCommitSha, pending: pending) {
            AppUpdate.clearPendingInstall(directory: dir)
            store.config = ConfigStore.update(from: store.settingsDirectory) { live in
                live.updateLastCheckAt = ""
                live.updateLastError = "上次更新没有替换成功，请再试一次"
            }
        }
    }

    @MainActor
    private static func pendingDirectory(_ store: AppStore) -> URL {
        store.settingsDirectory ?? AppPaths.configDirectory()
    }

    private static func launchHelper(newApp: URL, expectedSha: String) async -> Bool {
        let dest = Bundle.main.bundleURL
        let script = FileManager.default.temporaryDirectory
            .appendingPathComponent("CursorRemain-apply-\(UUID().uuidString).sh")
        let log = AppPaths.logPath().deletingLastPathComponent().appendingPathComponent("CursorRemain-update-helper.log")
        let body = """
        #!/bin/bash
        PID="$1"
        SRC="$2"
        DST="$3"
        EXPECT="$4"
        echo "start $(date -u +%Y-%m-%dT%H:%M:%SZ) pid=$PID dst=$DST expect=$EXPECT"
        for i in $(seq 1 80); do
          if ! kill -0 "$PID" 2>/dev/null; then
            break
          fi
          sleep 0.25
        done
        if kill -0 "$PID" 2>/dev/null; then
          kill -9 "$PID" 2>/dev/null || true
          sleep 0.3
        fi
        sleep 0.4
        apply() {
          TMP="${DST}.updating"
          rm -rf "$TMP"
          /usr/bin/ditto "$SRC" "$TMP" || return 1
          if [ -d "$DST" ]; then
            rm -rf "${DST}.old"
            mv "$DST" "${DST}.old" || rm -rf "$DST" || return 1
          fi
          mv "$TMP" "$DST" || return 1
          xattr -cr "$DST" >/dev/null 2>&1 || true
          rm -rf "${DST}.old"
          if [ -n "$EXPECT" ]; then
            GOT=$(/usr/libexec/PlistBuddy -c 'Print :GitCommit' "$DST/Contents/Info.plist" 2>/dev/null | tr '[:upper:]' '[:lower:]')
            EXP=$(printf '%s' "$EXPECT" | tr '[:upper:]' '[:lower:]')
            case "$GOT" in
              "$EXP"*) ;;
              *)
                case "$EXP" in
                  "$GOT"*) ;;
                  *) echo "sha mismatch got=$GOT expect=$EXP"; return 1 ;;
                esac
                ;;
            esac
          fi
          return 0
        }
        ok=0
        for t in 1 2 3 4 5; do
          if apply; then ok=1; break; fi
          echo "retry $t"
          sleep 0.4
        done
        if [ "$ok" -ne 1 ]; then
          echo "apply failed"
          rm -f "$0"
          exit 1
        fi
        EXE="$DST/Contents/MacOS/CursorRemain"
        if [ ! -x "$EXE" ]; then
          EXE="$DST/Contents/MacOS/CursorTokenTray"
        fi
        if [ -x "$EXE" ]; then
          /usr/bin/nohup "$EXE" >/dev/null 2>&1 &
        else
          /usr/bin/open -n -g "$DST"
        fi
        echo "opened $DST"
        rm -f "$0"
        """
        do {
            AppPaths.ensureDirectory(log.deletingLastPathComponent())
            try body.write(to: script, atomically: true, encoding: .utf8)
            try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: script.path)
            let command = AppUpdate.detachedHelperLaunchCommand(
                script: script.path,
                pid: String(ProcessInfo.processInfo.processIdentifier),
                source: newApp.path,
                destination: dest.path,
                expectedSha: AppUpdate.normalizeSha(expectedSha),
                log: log.path
            )
            let status = try await Task.detached(priority: .userInitiated) { () -> Int32 in
                let proc = Process()
                proc.executableURL = URL(fileURLWithPath: "/bin/bash")
                proc.arguments = ["-c", command]
                proc.standardOutput = FileHandle.nullDevice
                proc.standardError = FileHandle.nullDevice
                try proc.run()
                proc.waitUntilExit()
                return proc.terminationStatus
            }.value
            if status != 0 {
                AppLog.log("无法启动更新脚本: exit \(status)")
                return false
            }
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
