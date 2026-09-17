import Foundation
import XCTest
@testable import CursorTokenCore

final class AppPathsTests: XCTestCase {
    func testMigratesLegacyDirectoryWhenDestMissing() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("cremain-mig-\(UUID().uuidString)", isDirectory: true)
        let source = root.appendingPathComponent("CursorTokenTray", isDirectory: true)
        let dest = root.appendingPathComponent("CursorRemain", isDirectory: true)
        try FileManager.default.createDirectory(at: source, withIntermediateDirectories: true)
        try "{\"refresh_interval_minutes\":9}".write(to: source.appendingPathComponent("config.json"), atomically: true, encoding: .utf8)
        try "x\n".write(to: source.appendingPathComponent("usage_history.jsonl"), atomically: true, encoding: .utf8)
        defer { try? FileManager.default.removeItem(at: root) }

        XCTAssertTrue(AppPaths.tryMigrateLegacyDirectory(from: source, to: dest))
        XCTAssertTrue(FileManager.default.fileExists(atPath: dest.appendingPathComponent("config.json").path))
        XCTAssertTrue(FileManager.default.fileExists(atPath: dest.appendingPathComponent("usage_history.jsonl").path))
        XCTAssertFalse(FileManager.default.fileExists(atPath: source.path))
        XCTAssertFalse(AppPaths.tryMigrateLegacyDirectory(from: source, to: dest))
    }

    func testLeavesDestAloneWhenItAlreadyHasConfig() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("cremain-keep-\(UUID().uuidString)", isDirectory: true)
        let source = root.appendingPathComponent("CursorTokenTray", isDirectory: true)
        let dest = root.appendingPathComponent("CursorRemain", isDirectory: true)
        try FileManager.default.createDirectory(at: source, withIntermediateDirectories: true)
        try FileManager.default.createDirectory(at: dest, withIntermediateDirectories: true)
        try "{\"from\":\"old\"}".write(to: source.appendingPathComponent("config.json"), atomically: true, encoding: .utf8)
        try "{\"from\":\"new\"}".write(to: dest.appendingPathComponent("config.json"), atomically: true, encoding: .utf8)
        defer { try? FileManager.default.removeItem(at: root) }

        XCTAssertFalse(AppPaths.tryMigrateLegacyDirectory(from: source, to: dest))
        let kept = try String(contentsOf: dest.appendingPathComponent("config.json"), encoding: .utf8)
        XCTAssertEqual(kept, "{\"from\":\"new\"}")
        XCTAssertTrue(FileManager.default.fileExists(atPath: source.appendingPathComponent("config.json").path))
    }

    func testConfigDirectoryUnderHomeMigrates() throws {
        let home = FileManager.default.temporaryDirectory.appendingPathComponent("cremain-home-\(UUID().uuidString)", isDirectory: true)
        let legacy = home.appendingPathComponent("Library/Application Support/CursorTokenTray", isDirectory: true)
        try FileManager.default.createDirectory(at: legacy, withIntermediateDirectories: true)
        try "{\"ok\":true}".write(to: legacy.appendingPathComponent("config.json"), atomically: true, encoding: .utf8)
        defer { try? FileManager.default.removeItem(at: home) }

        let dest = AppPaths.prepareDefaultDirectory(home: home)
        XCTAssertEqual(dest.lastPathComponent, "CursorRemain")
        XCTAssertTrue(FileManager.default.fileExists(atPath: dest.appendingPathComponent("config.json").path))
        XCTAssertEqual(AppPaths.appSupportName, "CursorRemain")
        XCTAssertEqual(AppPaths.displayName, "Cursor 余量")
    }
}
