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

    func testReportSpendKpiAndTeamSkipCopy() {
        XCTAssertTrue(StatusText.formatReportSpendKpi(totalCny: 75, planCny: 150, onDemandCny: 37.5, usdCnyRate: 7.5, usesActual: true, windowPlanCny: 75).contains("已分摊"))
        XCTAssertTrue(StatusText.formatReportSpendKpi(totalCny: 75, planCny: 150, onDemandCny: 37.5, usdCnyRate: 7.5, usesActual: true, windowPlanCny: 75).contains("本窗口折算"))
        XCTAssertTrue(StatusText.formatReportSpendKpi(totalCny: 12.5, planCny: 120, onDemandCny: 12.5, usdCnyRate: 7.5, usesActual: false).contains("月费"))
        XCTAssertFalse(StatusText.formatReportSpendKpi(totalCny: 75, planCny: 150, onDemandCny: 0, usdCnyRate: 7.5, usesActual: true, windowPlanCny: 75).contains("预计实付"))
        XCTAssertEqual(
            StatusText.formatReportSyncResult(count: 3, fetched: 0, stamp: "12:00:00", note: UsageEvents.noteTeamPersonal),
            "未能拉取个人明细（团队账号）。请先刷新用量，或把范围切到「全员」。"
        )
        XCTAssertTrue(StatusText.formatReportSyncResult(count: 12, fetched: 0, stamp: "12:00:00").contains("已是最新"))
        XCTAssertTrue(StatusText.formatReportSyncResult(count: 16, fetched: 4, stamp: "12:00:00").contains("新增 4"))
        XCTAssertTrue(StatusText.formatReportSyncResult(count: 0, fetched: 0, stamp: "12:00:00").contains("还没有本周期明细"))
        XCTAssertEqual(StatusText.formatFlyoutError("Token 已过期或无效，请重新粘贴 WorkosCursorSessionToken"), "登录已过期，点下方「粘贴 Token」更新")
        XCTAssertEqual(StatusText.formatFlyoutError("未配置 Token，请打开设置粘贴"), "未配置 Token，点下方「粘贴 Token」导入")
        XCTAssertEqual(StatusText.formatFlyoutError("HTTP 429"), "HTTP 429")
        XCTAssertTrue(StatusText.compareHint.contains("绿色数字"))
        XCTAssertTrue(StatusText.compareHint.contains("已填成本"))
        XCTAssertTrue(StatusText.formatCompareHint(mixedWindows: true).contains("绿色仅供参考"))
        XCTAssertEqual(StatusText.formatCompareHint(mixedWindows: false), StatusText.compareHint)
        XCTAssertEqual(StatusText.formatReportCacheStatus(count: 12, accountLabel: "工作号"), "当前：工作号 · 本地 12 条，正在刷新…")
        XCTAssertEqual(StatusText.formatReportCacheStatus(count: 0), "本地还没有明细，正在同步…")
        XCTAssertEqual(StatusText.formatReportSyncError("Token 已过期或无效，请重新粘贴 WorkosCursorSessionToken"), "登录已过期，请到设置重新粘贴 Token")
        XCTAssertEqual(StatusText.formatReportSyncError("未配置 Token，请打开设置粘贴"), "未配置 Token，请先在设置里导入账号")
        XCTAssertTrue(StatusText.formatReportSyncError("HTTP 429").hasPrefix("同步失败："))
        XCTAssertTrue(StatusText.formatCloudDecryptNote(decryptError: false, syncSecretFailed: true, cloudAccessFailed: false).contains("解不开云同步密钥"))
        XCTAssertTrue(StatusText.formatCloudDecryptNote(decryptError: true, syncSecretFailed: false, cloudAccessFailed: false).contains("重新粘贴 Token"))
        XCTAssertEqual(StatusText.formatCloudDecryptNote(decryptError: false, syncSecretFailed: false, cloudAccessFailed: false), "")
    }

    func testCompareBestSkipsZeroCostRows() {
        let unpaid = AccountCompareRow(windowSource: "fallback", totalCny: 0, totalTokens: 2_000_000)
        let paid = AccountCompareRow(windowSource: "cycle", planCny: 150, windowPlanCny: 75, totalCny: 75, totalTokens: 1_000_000)
        let other = AccountCompareRow(windowSource: "validity", planCny: 150, windowPlanCny: 150, totalCny: 30, totalTokens: 1_000_000)
        XCTAssertFalse(UsageEvents.compareBestEligible(unpaid))
        XCTAssertEqual(UsageEvents.compareRowNote(unpaid), "未填成本")
        XCTAssertEqual(UsageEvents.compareRowNote(paid, hasToken: false), "未配置 Token")
        XCTAssertEqual(UsageEvents.compareRowNote(paid, lastError: "Token 已过期或无效"), "登录已过期")
        XCTAssertEqual(UsageEvents.compareRowNote(paid, hasToken: false, lastError: "Token 解密失败，请重新导入"), "Token 解不开")
        XCTAssertEqual(UsageEvents.compareBestPerMillion([unpaid, paid, other]), 30)
        XCTAssertTrue(UsageEvents.compareWindowsMixed([paid, other]))
        XCTAssertFalse(UsageEvents.compareWindowsMixed([paid, unpaid]))
    }

    func testFlyoutAndReportCopyMatchAuthState() {
        XCTAssertEqual(StatusText.flyoutSettingsTitle(nil), "设置")
        XCTAssertEqual(StatusText.flyoutSettingsTitle("HTTP 429 Too Many Requests"), "设置")
        XCTAssertEqual(StatusText.flyoutSettingsTitle("Token 已过期或无效，请重新粘贴 WorkosCursorSessionToken"), "粘贴 Token")
        XCTAssertEqual(StatusText.flyoutSettingsTitle("未配置 Token，请打开设置粘贴"), "粘贴 Token")
        XCTAssertEqual(StatusText.formatReportSyncProgress(1), "正在同步本周期明细…")
        XCTAssertEqual(StatusText.formatReportSyncProgress(4), "正在同步本周期明细…第 4 页")
        XCTAssertEqual(StatusText.formatReportFilterEmpty(0), "")
        XCTAssertEqual(StatusText.formatReportFilterEmpty(12), "当前筛选无结果（本地共 12 条，可清空筛选）")
        XCTAssertEqual(StatusText.formatCompareSyncProgress(index: 2, total: 5, name: "工作号"), "正在同步 工作号（2/5）…")
        XCTAssertEqual(StatusText.formatCompareSyncProgress(index: 2, total: 5, name: "工作号", page: 3), "正在同步 工作号（2/5）…第 3 页")
        XCTAssertEqual(StatusText.formatCloudSyncNotify(ok: true, message: "登录已过期，请重新登录"), "")
        XCTAssertEqual(StatusText.formatCloudSyncNotify(ok: false, message: "登录已过期，请重新登录"), "登录已过期，请重新登录")
        XCTAssertEqual(StatusText.formatCloudSyncNotify(ok: false, message: "用量明细因体积限制裁掉了 3 条最旧记录"), "")
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
