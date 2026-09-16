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

    func testAppendWithAccountIdDoesNotLoadConfig() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("ctt-hist-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        UsageHistory.append(remaining: 12.5, auto: 1, api: 2, ts: 1_700_000_000, accountId: "user_01A", directory: dir)
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
