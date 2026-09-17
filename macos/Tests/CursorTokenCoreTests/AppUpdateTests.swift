import Foundation
import XCTest
@testable import CursorTokenCore

final class AppUpdateTests: XCTestCase {
    func testFixtureCases() throws {
        let root = try XCTUnwrap(try Fixtures.json("update_release_cases.json") as? [String: Any])

        for row in root["extract_sha"] as! [[String: Any]] {
            XCTAssertEqual(AppUpdate.extractSha(row["text"] as? String), row["sha"] as? String, row["name"] as? String ?? "?")
        }
        for row in root["same_sha"] as! [[String: Any]] {
            XCTAssertEqual(
                AppUpdate.sameSha(row["left"] as? String, row["right"] as? String),
                row["same"] as? Bool,
                "\(row["left"] ?? "") vs \(row["right"] ?? "")"
            )
        }
        for row in root["normalize_sha"] as! [[String: Any]] {
            XCTAssertEqual(AppUpdate.normalizeSha(row["input"] as? String), row["output"] as? String)
        }
        for row in root["allowed_urls"] as! [[String: Any]] {
            XCTAssertEqual(AppUpdate.isAllowedDownloadURL(row["url"] as? String), row["ok"] as? Bool, row["url"] as? String ?? "")
        }
        for row in root["packaged_windows"] as! [[String: Any]] {
            XCTAssertEqual(AppUpdate.looksPackagedWindows(row["path"] as? String), row["ok"] as? Bool, row["path"] as? String ?? "")
        }
        for row in root["packaged_macos"] as! [[String: Any]] {
            XCTAssertEqual(AppUpdate.looksPackagedMacos(row["path"] as? String), row["ok"] as? Bool, row["path"] as? String ?? "")
        }
        for row in root["parse_release"] as! [[String: Any]] {
            let parsed = AppUpdate.parseRelease(row["json"] as! [String: Any])
            let exp = row["expected"] as! [String: Any]
            XCTAssertEqual(parsed.tag, exp["tag"] as? String)
            if let version = exp["version"] as? String {
                XCTAssertEqual(parsed.version, version)
            }
            XCTAssertEqual(parsed.commitSha, exp["sha"] as? String)
            XCTAssertEqual(parsed.pageUrl, exp["page_url"] as? String)
            XCTAssertEqual(AppUpdate.findAsset(parsed, name: AppUpdate.windowsAssetName)?.url, exp["windows_url"] as? String)
            XCTAssertEqual(AppUpdate.findAsset(parsed, name: AppUpdate.macosAssetName)?.url, exp["macos_url"] as? String)
            XCTAssertEqual(AppUpdate.findAsset(parsed, name: AppUpdate.windowsAssetName)?.id, int64(exp["windows_asset_id"]))
            XCTAssertEqual(AppUpdate.findAsset(parsed, name: AppUpdate.macosAssetName)?.id, int64(exp["macos_asset_id"]))
        }
        for row in root["parse_release_page"] as! [[String: Any]] {
            let parsed = try AppUpdate.parseReleasePage(row["html"] as? String ?? "")
            XCTAssertEqual(parsed.commitSha, row["sha"] as? String, row["name"] as? String ?? "?")
            XCTAssertEqual(AppUpdate.findAsset(parsed, name: AppUpdate.windowsAssetName)?.url, row["windows_url"] as? String)
            XCTAssertEqual(AppUpdate.findAsset(parsed, name: AppUpdate.macosAssetName)?.url, row["macos_url"] as? String)
        }
        for row in root["parse_tag_ref"] as! [[String: Any]] {
            let data = try JSONSerialization.data(withJSONObject: row["json"] as Any)
            let json = String(data: data, encoding: .utf8)!
            XCTAssertEqual(AppUpdate.parseTagRefSha(json), row["sha"] as? String)
        }

        let sample = AppUpdate.parseRelease((root["parse_release"] as! [[String: Any]])[0]["json"] as! [String: Any])
        for row in root["evaluate"] as! [[String: Any]] {
            var release = sample
            if row["clear_release_sha"] as? Bool == true {
                release.commitSha = ""
            }
            if let tag = row["tag"] as? String {
                release.tag = tag
                if let version = row["version"] as? String { release.version = version }
            }
            let asset = (row["asset"] as? String) == "macos" ? AppUpdate.macosAssetName : AppUpdate.windowsAssetName
            let currentVersion = row["current_version"] as? String ?? AppUpdate.productVersion
            let decision = AppUpdate.evaluate(
                release: release,
                assetName: asset,
                currentSha: row["current_sha"] as? String ?? "",
                installedSha: row["installed_sha"] as? String ?? "",
                installedAssetId: int64(row["installed_asset_id"]),
                currentVersion: currentVersion
            )
            XCTAssertEqual(decision.available, row["available"] as? Bool, row["name"] as? String ?? "?")
            XCTAssertEqual(decision.upToDate, row["up_to_date"] as? Bool, row["name"] as? String ?? "?")
        }

        for row in root["extract_version"] as! [[String: Any]] {
            XCTAssertEqual(
                AppUpdate.productVersionFromRelease(row["tag"] as? String, body: row["body"] as? String),
                row["version"] as? String
            )
        }
        for row in root["compare_versions"] as! [[String: Any]] {
            XCTAssertEqual(
                AppUpdate.compareProductVersions(row["left"] as? String, row["right"] as? String),
                Int(int64(row["cmp"]))
            )
        }
        for row in root["choose_release"] as! [[String: Any]] {
            let officialTag = row["official_tag"] as? String ?? ""
            let official: AppRelease? = officialTag.isEmpty ? nil : AppRelease(
                tag: officialTag,
                version: row["official_version"] as? String ?? "",
                assets: sample.assets
            )
            let rolling = AppRelease(tag: row["rolling_tag"] as? String ?? "latest", assets: sample.assets)
            let chosen = AppUpdate.chooseRelease(
                official: official,
                rolling: rolling,
                currentVersion: row["current_version"] as? String
            )
            let choice = (official != nil && chosen?.tag == official?.tag) ? "official" : "rolling"
            XCTAssertEqual(choice, row["choice"] as? String, row["name"] as? String ?? "?")
        }
    }

    func testPrefersNewAssetThenFallsBackToLegacy() {
        let release = AppRelease(
            tag: "latest",
            commitSha: "518192b000000000000000000000000000000000",
            assets: [
                AppReleaseAsset(
                    id: 7,
                    name: AppUpdate.legacyWindowsAssetName,
                    url: AppUpdate.assetDownloadURL(AppUpdate.legacyWindowsAssetName)
                )
            ]
        )
        let asset = AppUpdate.findPreferredAsset(release, name: AppUpdate.windowsAssetName)
        XCTAssertEqual(asset?.name, AppUpdate.legacyWindowsAssetName)
        let decision = AppUpdate.evaluate(
            release: release,
            assetName: AppUpdate.windowsAssetName,
            currentSha: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
        )
        XCTAssertTrue(decision.available)
        XCTAssertEqual(decision.asset?.id, 7)
    }

    func testDisplayVersionAndInformationalSha() {
        XCTAssertEqual(AppUpdate.displayVersion(""), AppUpdate.productVersion)
        XCTAssertEqual(AppUpdate.displayVersion("518192b000000000000000000000000000000000"), "\(AppUpdate.productVersion) (518192b)")
        XCTAssertEqual(AppUpdate.shaFromInformationalVersion("\(AppUpdate.productVersion)+518192b"), "518192b")
        XCTAssertEqual(AppUpdate.shaFromInformationalVersion(AppUpdate.productVersion), "")
        XCTAssertEqual(AppUpdate.normalizeProductVersion("v\(AppUpdate.productVersion)"), AppUpdate.productVersion)
        XCTAssertTrue(AppUpdate.shouldFallbackFromApi(403))
        XCTAssertTrue(AppUpdate.shouldFallbackFromApi(429))
        XCTAssertFalse(AppUpdate.shouldFallbackFromApi(400))
    }

    func testShouldAutoCheckRespectsInterval() {
        let now = Date(timeIntervalSince1970: 1_784_000_000)
        XCTAssertTrue(AppUpdate.shouldAutoCheck(lastCheckAt: "", now: now, manual: false))
        XCTAssertTrue(AppUpdate.shouldAutoCheck(lastCheckAt: AppUpdate.nowIso(now.addingTimeInterval(-13 * 3600)), now: now, manual: false))
        XCTAssertFalse(AppUpdate.shouldAutoCheck(lastCheckAt: AppUpdate.nowIso(now.addingTimeInterval(-3600)), now: now, manual: false))
        XCTAssertTrue(AppUpdate.shouldAutoCheck(lastCheckAt: AppUpdate.nowIso(now.addingTimeInterval(-3600)), now: now, manual: true))
    }

    func testRememberedInstallAfterHelperKeepsOldShaOnFailure() {
        XCTAssertNil(AppUpdate.rememberedInstallAfterHelper(
            sha: "518192b000000000000000000000000000000000",
            assetId: 99,
            helperStarted: false
        ))
        let remembered = AppUpdate.rememberedInstallAfterHelper(
            sha: "518192B000000000000000000000000000000000",
            assetId: 99,
            helperStarted: true
        )
        XCTAssertEqual(remembered?.sha, "518192b000000000000000000000000000000000")
        XCTAssertEqual(remembered?.assetId, 99)
    }

    func testConfigRoundtripsAutoUpdateFields() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("ctt-update-cfg-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        var cfg = AppConfig.default
        cfg.autoUpdateEnabled = false
        cfg.updateLastCheckAt = "2026-09-14T03:20:00Z"
        cfg.updateLastError = "x"
        cfg.updateInstalledSha = "518192b"
        cfg.updateInstalledAssetId = 101
        XCTAssertTrue(ConfigStore.save(cfg, to: dir))
        let loaded = ConfigStore.load(from: dir)
        XCTAssertFalse(loaded.autoUpdateEnabled)
        XCTAssertEqual(loaded.updateLastCheckAt, "2026-09-14T03:20:00Z")
        XCTAssertEqual(loaded.updateLastError, "x")
        XCTAssertEqual(loaded.updateInstalledSha, "518192b")
        XCTAssertEqual(loaded.updateInstalledAssetId, 101)
        let fresh = ConfigStore.normalize([:])
        XCTAssertTrue(fresh.autoUpdateEnabled)
    }

    private func int64(_ raw: Any?) -> Int64 {
        if let n = raw as? NSNumber { return n.int64Value }
        if let i = raw as? Int { return Int64(i) }
        if let s = raw as? String, let v = Int64(s) { return v }
        return 0
    }
}
