import Foundation

public struct AppReleaseAsset: Equatable, Sendable {
    public var id: Int64
    public var name: String
    public var url: String
    public var size: Int64
    public var updatedAt: String

    public init(id: Int64 = 0, name: String = "", url: String = "", size: Int64 = 0, updatedAt: String = "") {
        self.id = id
        self.name = name
        self.url = url
        self.size = size
        self.updatedAt = updatedAt
    }
}

public struct AppRelease: Equatable, Sendable {
    public var tag: String
    public var version: String
    public var body: String
    public var commitSha: String
    public var pageUrl: String
    public var publishedAt: String
    public var assets: [AppReleaseAsset]

    public init(
        tag: String = "",
        version: String = "",
        body: String = "",
        commitSha: String = "",
        pageUrl: String = "",
        publishedAt: String = "",
        assets: [AppReleaseAsset] = []
    ) {
        self.tag = tag
        self.version = version
        self.body = body
        self.commitSha = commitSha
        self.pageUrl = pageUrl
        self.publishedAt = publishedAt
        self.assets = assets
    }
}

public struct UpdateDecision: Equatable, Sendable {
    public var upToDate: Bool
    public var available: Bool
    public var message: String
    public var release: AppRelease?
    public var asset: AppReleaseAsset?

    public init(
        upToDate: Bool = false,
        available: Bool = false,
        message: String = "",
        release: AppRelease? = nil,
        asset: AppReleaseAsset? = nil
    ) {
        self.upToDate = upToDate
        self.available = available
        self.message = message
        self.release = release
        self.asset = asset
    }
}

public enum AppUpdate {
    public static let repoOwner = "sydy"
    public static let repoName = "CursorRemain"
    public static let latestTag = "latest"
    public static let windowsAssetName = "CursorRemain-windows.zip"
    public static let windowsLightAssetName = "CursorRemain-windows-light.zip"
    public static let macosAssetName = "CursorRemain-macos.zip"
    public static let legacyWindowsAssetName = "CursorTokenTray-windows.zip"
    public static let legacyMacosAssetName = "CursorTokenTray-macos.zip"
    public static let windowsExeName = "CursorRemain.exe"
    public static let legacyWindowsExeName = "CursorTokenTray.exe"
    public static let macAppName = "CursorRemain.app"
    public static let legacyMacAppName = "CursorTokenTray.app"
    public static let productVersion = "2.1.1"
    public static let maxZipBytes: Int64 = 140 * 1024 * 1024
    public static let maxLightZipBytes: Int64 = 40 * 1024 * 1024
    public static let autoCheckInterval: TimeInterval = 12 * 60 * 60
    public static let startupDelay: TimeInterval = 20

    public static var officialLatestPageURL: URL {
        URL(string: "https://github.com/\(repoOwner)/\(repoName)/releases/latest")!
    }

    public static var latestReleasePageURL: URL {
        URL(string: "https://github.com/\(repoOwner)/\(repoName)/releases/tag/\(latestTag)")!
    }

    public static var apiOfficialLatestURL: URL {
        URL(string: "https://api.github.com/repos/\(repoOwner)/\(repoName)/releases/latest")!
    }

    public static var apiLatestReleaseURL: URL {
        URL(string: "https://api.github.com/repos/\(repoOwner)/\(repoName)/releases/tags/\(latestTag)")!
    }

    public static var apiLatestRefURL: URL {
        URL(string: "https://api.github.com/repos/\(repoOwner)/\(repoName)/git/refs/tags/\(latestTag)")!
    }

    public static var userAgent: String {
        "CursorRemain/\(productVersion) (+https://github.com/\(repoOwner)/\(repoName))"
    }

    public static func assetDownloadURL(_ assetName: String, tag: String? = nil) -> String {
        let releaseTag = (tag ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        let used = releaseTag.isEmpty ? latestTag : releaseTag
        return "https://github.com/\(repoOwner)/\(repoName)/releases/download/\(used)/\(assetName)"
    }

    public static func shouldFallbackFromApi(_ statusCode: Int) -> Bool {
        statusCode == 401 || statusCode == 403 || statusCode == 404 || statusCode == 429 || statusCode >= 500
    }

    public static var platformAssetName: String {
        #if os(Windows)
        windowsAssetName
        #else
        macosAssetName
        #endif
    }

    public static func isWindowsLightAssetName(_ name: String?) -> Bool {
        (name ?? "").caseInsensitiveCompare(windowsLightAssetName) == .orderedSame
    }

    public static func preferredWindowsAssetName(preferLight: Bool) -> String {
        preferLight ? windowsLightAssetName : windowsAssetName
    }

    public static func maxBytesForAsset(_ name: String?) -> Int64 {
        isWindowsLightAssetName(name) ? maxLightZipBytes : maxZipBytes
    }

    public static func normalizeSha(_ raw: String?) -> String {
        var s = (raw ?? "").trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        if s.hasPrefix("sha-") { s = String(s.dropFirst(4)) }
        guard (7...40).contains(s.count) else { return "" }
        return s.unicodeScalars.allSatisfy { CharacterSet(charactersIn: "0123456789abcdef").contains($0) } ? s : ""
    }

    public static func extractSha(_ text: String?) -> String {
        let raw = text ?? ""
        if let match = firstMatch(raw, pattern: #"(?i)\b([0-9a-f]{40})\b"#) {
            return normalizeSha(match)
        }
        if let match = firstMatch(raw, pattern: #"(?i)(?:commit/|`)([0-9a-f]{7,40})(?:`|\b)"#) {
            return normalizeSha(match)
        }
        return ""
    }

    public static func sameSha(_ left: String?, _ right: String?) -> Bool {
        let a = normalizeSha(left)
        let b = normalizeSha(right)
        if a.isEmpty || b.isEmpty { return false }
        let minLen = min(a.count, b.count)
        if minLen < 7 { return a == b }
        return a.hasPrefix(b) || b.hasPrefix(a)
    }

    public static func shaFromInformationalVersion(_ informational: String?) -> String {
        let info = informational ?? ""
        guard let plus = info.lastIndex(of: "+") else { return "" }
        return normalizeSha(String(info[info.index(after: plus)...]))
    }

    public static func versionFromInformational(_ informational: String?) -> String {
        var info = (informational ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        if info.isEmpty { return "" }
        if let plus = info.firstIndex(of: "+") {
            info = String(info[..<plus])
        }
        return normalizeProductVersion(info)
    }

    public static func normalizeProductVersion(_ raw: String?) -> String {
        var value = (raw ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        if value.first == "v" || value.first == "V" { value.removeFirst() }
        return firstMatch(value, pattern: #"^(\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)"#) ?? ""
    }

    public static func extractProductVersion(_ text: String?) -> String {
        let raw = text ?? ""
        if let match = firstMatch(raw, pattern: #"(?i)\bv?(\d+\.\d+\.\d+)\b"#) {
            return match
        }
        return normalizeProductVersion(raw)
    }

    public static func productVersionFromRelease(_ tag: String?, body: String? = nil) -> String {
        let fromTag = normalizeProductVersion(tag)
        if !fromTag.isEmpty { return fromTag }
        let raw = body ?? ""
        if let match = firstMatch(raw, pattern: #"(?i)(?:\*\*)?版本(?:\*\*)?\s*[:：]\s*`?v?(\d+\.\d+\.\d+)"#) {
            return match
        }
        return extractProductVersion(raw)
    }

    public static func compareProductVersions(_ left: String?, _ right: String?) -> Int {
        guard let a = parseProductVersion(left), let b = parseProductVersion(right) else { return 0 }
        if a[0] != b[0] { return a[0] < b[0] ? -1 : 1 }
        if a[1] != b[1] { return a[1] < b[1] ? -1 : 1 }
        if a[2] != b[2] { return a[2] < b[2] ? -1 : 1 }
        return 0
    }

    public static func parseProductVersion(_ raw: String?) -> [Int]? {
        let version = normalizeProductVersion(raw)
        if version.isEmpty { return nil }
        let core = version.split(separator: "-", maxSplits: 1, omittingEmptySubsequences: false).first.map(String.init) ?? version
        let bits = core.split(separator: ".")
        guard bits.count == 3 else { return nil }
        var parts: [Int] = []
        for bit in bits {
            guard let n = Int(bit), n >= 0 else { return nil }
            parts.append(n)
        }
        return parts
    }

    public static func formatReleaseLabel(version: String?, sha: String?) -> String {
        let ver = normalizeProductVersion(version)
        let short = shortSha(sha)
        if !ver.isEmpty && !short.isEmpty && short != latestTag {
            return "\(ver) (\(short))"
        }
        if !ver.isEmpty { return ver }
        return short.isEmpty ? latestTag : short
    }

    public static func chooseRelease(official: AppRelease?, rolling: AppRelease?, currentVersion: String? = nil) -> AppRelease? {
        if official == nil { return rolling }
        if rolling == nil { return official }
        let current = normalizeProductVersion(currentVersion ?? productVersion)
        var officialVer = normalizeProductVersion(official?.version)
        if officialVer.isEmpty {
            officialVer = productVersionFromRelease(official?.tag, body: official?.body)
        }
        if !current.isEmpty, !officialVer.isEmpty, compareProductVersions(officialVer, current) > 0 {
            return official
        }
        return rolling
    }

    public static var currentCommitSha: String {
        for key in ["GitCommit", "SourceRevision"] {
            if let raw = Bundle.main.object(forInfoDictionaryKey: key) as? String {
                let sha = normalizeSha(raw)
                if !sha.isEmpty { return sha }
            }
        }
        return ""
    }

    public static func displayVersion(_ sha: String? = nil) -> String {
        var value = normalizeSha(sha ?? currentCommitSha)
        if value.count > 7 { value = String(value.prefix(7)) }
        return value.isEmpty ? productVersion : "\(productVersion) (\(value))"
    }

    public static func isAllowedDownloadURL(_ raw: String?) -> Bool {
        guard let raw, let url = URL(string: raw), let host = url.host?.lowercased() else { return false }
        guard url.scheme?.lowercased() == "https" else { return false }
        return host == "github.com"
            || host.hasSuffix(".github.com")
            || host == "githubusercontent.com"
            || host.hasSuffix(".githubusercontent.com")
    }

    public static func looksPackagedWindows(_ processPath: String?) -> Bool {
        let path = (processPath ?? "").trimmingCharacters(in: .whitespaces)
        if path.isEmpty { return false }
        let normalized = path.replacingOccurrences(of: "/", with: "\\")
        let name = normalized.split(separator: "\\").last.map(String.init) ?? normalized
        guard isOurWindowsExe(name) else { return false }
        let lower = normalized.lowercased()
        if lower.contains("\\bin\\") { return false }
        if lower.contains("\\obj\\") { return false }
        if lower.contains("\\.build\\") { return false }
        return true
    }

    public static func looksPackagedMacos(_ bundleOrExePath: String?) -> Bool {
        let path = (bundleOrExePath ?? "").trimmingCharacters(in: .whitespaces).replacingOccurrences(of: "\\", with: "/")
        if path.isEmpty { return false }
        let lower = path.lowercased()
        if lower.contains("/.build/") || lower.contains("/deriveddata/") { return false }
        if lower.hasSuffix(".app") || lower.contains(".app/contents/") {
            return isOurMacApp((appBundlePath(path) as NSString).lastPathComponent)
        }
        return false
    }

    public static func appBundlePath(_ path: String) -> String {
        let value = path.replacingOccurrences(of: "\\", with: "/").trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        let withSlash = "/" + value.trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        if let range = withSlash.range(of: ".app/", options: .caseInsensitive) {
            return String(withSlash[..<range.upperBound].dropLast())
        }
        if withSlash.lowercased().hasSuffix(".app") { return withSlash }
        return path
    }

    public static func canSelfUpdate(processPath: String? = nil, macosBundlePath: String? = nil) -> Bool {
        #if os(Windows)
        return looksPackagedWindows(processPath)
        #elseif os(macOS)
        let path = macosBundlePath ?? processPath ?? Bundle.main.bundlePath
        return looksPackagedMacos(path)
        #else
        return false
        #endif
    }

    public static func parseRelease(_ json: String) throws -> AppRelease {
        guard let data = json.data(using: .utf8),
              let obj = try JSONSerialization.jsonObject(with: data) as? [String: Any]
        else { throw CursorAPIError("更新信息格式异常") }
        return parseRelease(obj)
    }

    public static func parseRelease(_ raw: [String: Any]) -> AppRelease {
        let tag = stringValue(raw["tag_name"])
        let body = stringValue(raw["body"])
        var page = stringValue(raw["html_url"])
        if page.isEmpty {
            page = !tag.isEmpty && tag != latestTag
                ? "https://github.com/\(repoOwner)/\(repoName)/releases/tag/\(tag)"
                : latestReleasePageURL.absoluteString
        }
        var sha = extractSha(body)
        if sha.isEmpty { sha = normalizeSha(stringValue(raw["target_commitish"])) }
        var assets: [AppReleaseAsset] = []
        if let rows = raw["assets"] as? [[String: Any]] {
            for item in rows {
                let name = stringValue(item["name"])
                let url = stringValue(item["browser_download_url"])
                if name.isEmpty || url.isEmpty { continue }
                assets.append(AppReleaseAsset(
                    id: int64Value(item["id"]),
                    name: name,
                    url: url,
                    size: int64Value(item["size"]),
                    updatedAt: stringValue(item["updated_at"])
                ))
            }
        }
        return AppRelease(
            tag: tag.isEmpty ? latestTag : tag,
            version: productVersionFromRelease(tag, body: body),
            body: body,
            commitSha: sha,
            pageUrl: page,
            publishedAt: stringValue(raw["published_at"]),
            assets: assets
        )
    }

    public static func knownAssets(_ tag: String? = nil) -> [AppReleaseAsset] {
        [
            AppReleaseAsset(name: windowsAssetName, url: assetDownloadURL(windowsAssetName, tag: tag)),
            AppReleaseAsset(name: macosAssetName, url: assetDownloadURL(macosAssetName, tag: tag)),
        ]
    }

    public static func parseReleasePage(_ html: String) throws -> AppRelease {
        var sha = extractSha(html)
        if sha.isEmpty, let match = firstMatch(html, pattern: #"(?i)/commit/([0-9a-f]{7,40})"#) {
            sha = normalizeSha(match)
        }
        if sha.isEmpty { throw CursorAPIError("发布页里找不到提交哈希") }
        return AppRelease(
            tag: latestTag,
            version: productVersionFromRelease("", body: html),
            body: html,
            commitSha: sha,
            pageUrl: latestReleasePageURL.absoluteString,
            assets: knownAssets()
        )
    }

    public static func httpStatusMessage(_ statusCode: Int) -> String {
        switch statusCode {
        case 401, 403: return "GitHub 接口拒绝访问（403）"
        case 404: return "找不到正式版或 Latest 发布"
        case 429: return "GitHub 请求过于频繁，请稍后重试"
        default: return "GitHub \(statusCode)"
        }
    }

    public static func parseTagRefSha(_ json: String) -> String {
        guard let data = json.data(using: .utf8),
              let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
        else { return "" }
        if let nested = obj["object"] as? [String: Any] {
            return normalizeSha(stringValue(nested["sha"]))
        }
        return normalizeSha(stringValue(obj["sha"]))
    }

    public static func findAsset(_ release: AppRelease, name: String) -> AppReleaseAsset? {
        release.assets.first { $0.name.caseInsensitiveCompare(name) == .orderedSame }
    }

    public static func assetNameCandidates(_ preferred: String) -> [String] {
        if preferred.caseInsensitiveCompare(windowsLightAssetName) == .orderedSame
            || preferred.caseInsensitiveCompare(windowsAssetName) == .orderedSame
            || preferred.caseInsensitiveCompare(legacyWindowsAssetName) == .orderedSame {
            if preferred.caseInsensitiveCompare(windowsLightAssetName) == .orderedSame {
                return [windowsLightAssetName, windowsAssetName, legacyWindowsAssetName]
            }
            return [windowsAssetName, legacyWindowsAssetName]
        }
        if preferred.caseInsensitiveCompare(macosAssetName) == .orderedSame
            || preferred.caseInsensitiveCompare(legacyMacosAssetName) == .orderedSame {
            return [macosAssetName, legacyMacosAssetName]
        }
        return [preferred]
    }

    public static func findPreferredAsset(_ release: AppRelease, name: String) -> AppReleaseAsset? {
        for candidate in assetNameCandidates(name) {
            guard let asset = findAsset(release, name: candidate) else { continue }
            if !isAllowedDownloadURL(asset.url) { continue }
            if asset.size > maxBytesForAsset(candidate) { continue }
            return asset
        }
        return nil
    }

    public static func isOurWindowsExe(_ fileName: String?) -> Bool {
        let name = (fileName ?? "").trimmingCharacters(in: .whitespaces)
        return name.caseInsensitiveCompare(windowsExeName) == .orderedSame
            || name.caseInsensitiveCompare(legacyWindowsExeName) == .orderedSame
    }

    public static func isOurMacApp(_ fileName: String?) -> Bool {
        let name = (fileName ?? "").trimmingCharacters(in: .whitespaces)
        return name.caseInsensitiveCompare(macAppName) == .orderedSame
            || name.caseInsensitiveCompare(legacyMacAppName) == .orderedSame
    }

    public static func evaluate(
        release: AppRelease,
        assetName: String,
        currentSha: String,
        installedSha: String = "",
        installedAssetId: Int64 = 0,
        currentVersion: String? = nil
    ) -> UpdateDecision {
        guard let asset = findPreferredAsset(release, name: assetName) else {
            return UpdateDecision(message: "最新发布没有本平台安装包")
        }
        if !isAllowedDownloadURL(asset.url) {
            return UpdateDecision(message: "更新地址无效")
        }
        if asset.size > maxBytesForAsset(asset.name) {
            return UpdateDecision(message: "安装包过大，已取消更新")
        }
        var remoteVersion = normalizeProductVersion(release.version)
        if remoteVersion.isEmpty {
            remoteVersion = productVersionFromRelease(release.tag, body: release.body)
        }
        let localVersion = normalizeProductVersion(currentVersion ?? productVersion)
        if !remoteVersion.isEmpty, !localVersion.isEmpty {
            let cmp = compareProductVersions(remoteVersion, localVersion)
            if cmp < 0 {
                return UpdateDecision(upToDate: true, message: "已是最新版本", release: release, asset: asset)
            }
            if cmp > 0 {
                return available(release, asset)
            }
        }
        var localSha = normalizeSha(currentSha)
        if localSha.isEmpty { localSha = normalizeSha(installedSha) }
        let remoteSha = normalizeSha(release.commitSha)
        if !remoteSha.isEmpty, sameSha(localSha, remoteSha) {
            return UpdateDecision(upToDate: true, message: "已是最新版本", release: release, asset: asset)
        }
        if remoteSha.isEmpty, installedAssetId > 0, installedAssetId == asset.id {
            return UpdateDecision(upToDate: true, message: "已是最新版本", release: release, asset: asset)
        }
        if !remoteSha.isEmpty, !localSha.isEmpty, !sameSha(localSha, remoteSha) {
            return available(release, asset)
        }
        if localSha.isEmpty {
            return available(release, asset)
        }
        if !remoteSha.isEmpty, !sameSha(localSha, remoteSha) {
            return available(release, asset)
        }
        return UpdateDecision(upToDate: true, message: "已是最新版本", release: release, asset: asset)
    }

    public struct RememberedInstall: Equatable, Sendable {
        public var sha: String
        public var assetId: Int64
        public init(sha: String, assetId: Int64) {
            self.sha = sha
            self.assetId = assetId
        }
    }

    /// Persist the installed SHA only after the replace helper actually started.
    /// A failed launch must keep the previous SHA so the same release can be retried.
    public static func rememberedInstallAfterHelper(sha: String, assetId: Int64, helperStarted: Bool) -> RememberedInstall? {
        helperStarted ? RememberedInstall(sha: normalizeSha(sha), assetId: assetId) : nil
    }

    public static func shortSha(_ sha: String?) -> String {
        var value = normalizeSha(sha)
        if value.isEmpty { return latestTag }
        if value.count > 7 { value = String(value.prefix(7)) }
        return value
    }

    public static func shouldAutoCheck(lastCheckAt: String, now: Date = Date(), manual: Bool) -> Bool {
        if manual { return true }
        guard let last = parseIso(lastCheckAt) else { return true }
        return now.timeIntervalSince(last) >= autoCheckInterval
    }

    public static func parseIso(_ raw: String?) -> Date? {
        let s = (raw ?? "").trimmingCharacters(in: .whitespaces)
        if s.isEmpty { return nil }
        let iso = ISO8601DateFormatter()
        iso.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let d = iso.date(from: s) { return d }
        iso.formatOptions = [.withInternetDateTime]
        return iso.date(from: s)
    }

    public static func nowIso(_ date: Date = Date()) -> String {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime]
        return f.string(from: date)
    }

    static func available(_ release: AppRelease, _ asset: AppReleaseAsset) -> UpdateDecision {
        let version = release.version.isEmpty ? productVersionFromRelease(release.tag, body: release.body) : release.version
        return UpdateDecision(
            available: true,
            message: "发现新版本 \(formatReleaseLabel(version: version, sha: release.commitSha))",
            release: release,
            asset: asset
        )
    }

    static func firstMatch(_ text: String, pattern: String) -> String? {
        guard let regex = try? NSRegularExpression(pattern: pattern) else { return nil }
        let range = NSRange(text.startIndex..<text.endIndex, in: text)
        guard let match = regex.firstMatch(in: text, range: range), match.numberOfRanges > 1,
              let swiftRange = Range(match.range(at: 1), in: text)
        else { return nil }
        return String(text[swiftRange])
    }

    static func stringValue(_ raw: Any?) -> String {
        (raw as? String) ?? ""
    }

    static func int64Value(_ raw: Any?) -> Int64 {
        if let n = raw as? NSNumber { return n.int64Value }
        if let i = raw as? Int64 { return i }
        if let i = raw as? Int { return Int64(i) }
        if let s = raw as? String, let v = Int64(s) { return v }
        return 0
    }
}
