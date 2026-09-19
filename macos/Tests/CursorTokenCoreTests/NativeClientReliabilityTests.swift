import Foundation
import XCTest
@testable import CursorTokenCore

final class NativeClientReliabilityTests: XCTestCase {
    override func tearDown() {
        CloudSync.testSend = nil
        super.tearDown()
    }

    func testRefreshGenerationIgnoresStaleOrInactiveOutcomes() {
        XCTAssertTrue(RefreshGeneration.shouldApply(outcomeId: "a", activeId: "a", outcomeGeneration: 3, currentGeneration: 3))
        XCTAssertFalse(RefreshGeneration.shouldApply(outcomeId: "a", activeId: "a", outcomeGeneration: 2, currentGeneration: 3))
        XCTAssertFalse(RefreshGeneration.shouldApply(outcomeId: "old", activeId: "a", outcomeGeneration: 3, currentGeneration: 3))
        XCTAssertFalse(RefreshGeneration.shouldApply(outcomeId: "a", activeId: "b", outcomeGeneration: 4, currentGeneration: 4))
    }

    func testBoundedWorkCapsConcurrency() async {
        final class Counter: @unchecked Sendable {
            private var value = 0
            private var peak = 0
            private let lock = NSLock()
            func increment() -> Int {
                lock.lock(); defer { lock.unlock() }
                value += 1
                if value > peak { peak = value }
                return value
            }
            func decrement() {
                lock.lock(); defer { lock.unlock() }
                value -= 1
            }
            var maxValue: Int {
                lock.lock(); defer { lock.unlock() }
                return peak
            }
        }
        let counter = Counter()
        let items = Array(0..<8)
        let results = await RefreshGeneration.mapBounded(items, limit: 2) { i in
            _ = counter.increment()
            try? await Task.sleep(nanoseconds: 30_000_000)
            counter.decrement()
            return i
        }
        XCTAssertEqual(results, items)
        XCTAssertLessThanOrEqual(counter.maxValue, 2)
        XCTAssertEqual(RefreshGeneration.accountRefreshLimit, 2)
    }

    func testNormalizeReportRangeSwapsInvertedDates() {
        let swapped = UsageEvents.normalizeReportRange("2026-09-15", "2026-09-01")
        XCTAssertEqual(swapped.start, "2026-09-01")
        XCTAssertEqual(swapped.end, "2026-09-15")
        XCTAssertTrue(swapped.swapped)
        XCTAssertFalse(UsageEvents.normalizeReportRange("2026-09-01", "2026-09-15").swapped)
        let open = UsageEvents.normalizeReportRange("2026-09-08", "")
        XCTAssertEqual(open.start, "2026-09-08")
        XCTAssertEqual(open.end, "")
        XCTAssertFalse(open.swapped)
        let mid = (UsageEvents.reportDateStartMs("2026-09-08") ?? 0) + 12 * 3600 * 1000
        let ev = UsageEvent(id: "mid", timestampMs: mid, model: "opus", kind: UsageEvents.kindIncluded, tokens: 10)
        let report = UsageEvents.buildReport([ev], filter: UsageReportFilter(startDate: "2026-09-15", endDate: "2026-09-01"))
        XCTAssertEqual(report.eventCount, 1)
        XCTAssertEqual(report.events.first?.id, "mid")
    }

    func testPrioritizeKeepsRelativeOrder() {
        let items = ["b", "a", "c", "d"]
        XCTAssertEqual(RefreshGeneration.prioritize(items) { $0 == "a" }, ["a", "b", "c", "d"])
        XCTAssertEqual(RefreshGeneration.prioritize(items) { _ in false }, items)
    }

    func testFlyoutAndReportCopyMatchAuthState() {
        XCTAssertEqual(StatusText.flyoutSettingsTitle(nil), "设置")
        XCTAssertEqual(StatusText.flyoutSettingsTitle("HTTP 429 Too Many Requests"), "设置")
        XCTAssertEqual(StatusText.flyoutSettingsTitle("Token 已过期或无效，请重新粘贴 WorkosCursorSessionToken"), "粘贴 Token")
        XCTAssertEqual(StatusText.flyoutSettingsTitle("未配置 Token，请打开设置粘贴"), "粘贴 Token")
        XCTAssertEqual(StatusText.formatReportSyncProgress(1), "正在同步本周期明细…")
        XCTAssertEqual(StatusText.formatReportSyncProgress(4), "正在同步本周期明细…第 4 页")
        XCTAssertEqual(CursorAccountPaste.tokenValues("aaa.bbb.ccc\nddd.eee.fff"), ["aaa.bbb.ccc", "ddd.eee.fff"])
        XCTAssertEqual(CursorAccountPaste.tokenValues("name@example.com:secret"), [])
    }

    func testCompareSyncAndTrimStatusCopy() {
        XCTAssertEqual(StatusText.formatCompareSync(ok: 3, failures: [], stamp: "12:00:00"), "已同步 3 个账号  ·  12:00:00")
        let failed = StatusText.formatCompareSync(ok: 2, failures: ["工作号：Token 过期", "临时号：未配置 Token"], stamp: "12:01:00")
        XCTAssertTrue(failed.contains("工作号：Token 过期"))
        XCTAssertTrue(StatusText.formatCompareSync(ok: 1, failures: ["a：1", "b：2", "c：3", "d：4"], stamp: "12:02:00").contains("等4个"))
        XCTAssertEqual(StatusText.formatSyncStatus(lastAt: "", lastError: "登录已过期，请重新登录"), "登录已过期，请重新登录")
        let mixed = StatusText.formatSyncStatus(lastAt: "2026-09-19T04:00:00.000Z", lastError: "用量明细因体积限制裁掉了 12 条最旧记录")
        XCTAssertTrue(mixed.contains("上次同步"))
        XCTAssertTrue(mixed.contains("体积限制"))
        XCTAssertTrue(AccountSync.isTrimNote("用量明细因体积限制裁掉了 3 条最旧记录"))
        XCTAssertEqual(AccountSync.formatTrimNote(0), "")
        XCTAssertEqual(AccountSync.appendTrimNote("已上传到云端", "用量明细因体积限制裁掉了 2 条最旧记录"), "已上传到云端；用量明细因体积限制裁掉了 2 条最旧记录")
    }

    func testAppendWithAccountIdDoesNotLoadConfig() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("ctt-hist-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let ts = Date().timeIntervalSince1970
        UsageHistory.append(remaining: 12.5, auto: 1, api: 2, ts: ts, accountId: "user_01A", directory: dir)
        XCTAssertFalse(FileManager.default.fileExists(atPath: AppPaths.configPath(in: dir).path))
        let points = UsageHistory.loadRecent(days: 7, accountId: "user_01A", directory: dir)
        XCTAssertEqual(points.count, 1)
        XCTAssertEqual(points[0].remaining, 12.5, accuracy: 0.001)
    }

    func testReconcileRetriesPutAfter409ThenSucceeds() {
        var cfg = loggedIn()
        var calls: [String] = []
        CloudSync.testSend = { method, path, _, _ in
            calls.append("\(method) \(path)")
            switch (method, path, calls.count) {
            case ("GET", "/v1/sync", 1):
                return ["revision": 1]
            case ("PUT", "/v1/sync", 2):
                throw CursorAPIError("同步冲突，请重试", statusCode: 409)
            case ("GET", "/v1/sync", 3):
                return ["revision": 2]
            case ("PUT", "/v1/sync", 4):
                return ["revision": 3]
            default:
                throw CursorAPIError("unexpected \(method) \(path) #\(calls.count)")
            }
        }
        #if canImport(CryptoKit)
        let status = CloudSync.reconcile(&cfg)
        XCTAssertTrue(status.ok, status.message)
        XCTAssertTrue(status.pushed)
        XCTAssertEqual(cfg.cloudRevision, 3)
        XCTAssertEqual(calls, ["GET /v1/sync", "PUT /v1/sync", "GET /v1/sync", "PUT /v1/sync"])
        #else
        let status = CloudSync.reconcile(&cfg)
        XCTAssertFalse(status.ok)
        XCTAssertTrue(status.message.contains("无法加密") || status.message.contains("同步"))
        #endif
    }
}

private func loggedIn() -> AppConfig {
    var cfg = AppConfig.default
    cfg.syncEnabled = true
    cfg.syncSecret = "unit-test-secret"
    cfg.cloudEmail = "t@example.com"
    cfg.cloudAccessToken = "access-token"
    cfg.cloudRefreshToken = "refresh-token"
    cfg.syncDeviceId = "device-test"
    return cfg
}
