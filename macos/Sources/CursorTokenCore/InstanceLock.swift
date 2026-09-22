import Darwin
import Foundation

public enum InstanceLock {
    public static func acquire(directory: URL? = nil) -> Bool {
        if let directory {
            return lock(in: directory)
        }
        let dest = AppPaths.configDirectory()
        let legacy = AppPaths.legacyConfigDirectory()
        let destReady = FileManager.default.fileExists(atPath: dest.appendingPathComponent("config.json").path)
        let legacyReady = FileManager.default.fileExists(atPath: legacy.appendingPathComponent("config.json").path)
        if !destReady && legacyReady {
            if !lock(in: legacy) { return false }
            release(directory: legacy)
            _ = AppPaths.tryMigrateLegacyDirectory(from: legacy, to: dest)
        }
        return lock(in: dest)
    }

    /// If a previous CursorRemain / CursorTokenTray still holds the lock (often
    /// an invisible older build), terminate it and take over so a newly opened
    /// .app can show its menu bar extra.
    public static func acquireReplacingStale(directory: URL? = nil) -> Bool {
        if acquire(directory: directory) { return true }
        if replaceStale(in: directory ?? AppPaths.configDirectory()) {
            return acquire(directory: directory)
        }
        if directory == nil {
            let legacy = AppPaths.legacyConfigDirectory()
            if replaceStale(in: legacy) {
                _ = AppPaths.tryMigrateLegacyDirectory(from: legacy, to: AppPaths.configDirectory())
                return acquire(directory: nil)
            }
        }
        return false
    }

    public static func looksLikeOurExecutable(_ path: String) -> Bool {
        let trimmed = path.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.isEmpty { return false }
        let name = URL(fileURLWithPath: trimmed).lastPathComponent
        if name == "CursorRemain" || name == "CursorTokenTray" { return true }
        return trimmed.contains("CursorRemain.app/") || trimmed.contains("CursorTokenTray.app/")
    }

    static func replaceStale(in dir: URL) -> Bool {
        let pidURL = dir.appendingPathComponent("instance.pid")
        guard let text = try? String(contentsOf: pidURL, encoding: .utf8),
              let pid = Int32(text.trimmingCharacters(in: .whitespacesAndNewlines)),
              pid > 1,
              pid != ProcessInfo.processInfo.processIdentifier,
              isOurProcess(pid)
        else { return false }
        AppLog.log("replacing stale instance pid=\(pid)")
        kill(pid, SIGTERM)
        for _ in 0..<30 {
            if kill(pid, 0) != 0 { break }
            usleep(50_000)
        }
        if kill(pid, 0) == 0 {
            kill(pid, SIGKILL)
            usleep(80_000)
        }
        return true
    }

    public static func release(directory: URL? = nil) {
        guard let fd = acquiredFD else { return }
        flock(fd, LOCK_UN)
        close(fd)
        acquiredFD = nil
        let dir = directory ?? AppPaths.configDirectory()
        try? FileManager.default.removeItem(at: dir.appendingPathComponent("instance.pid"))
    }

    static func lock(in dir: URL) -> Bool {
        AppPaths.ensureDirectory(dir)
        let lockURL = dir.appendingPathComponent("instance.lock")
        let pidURL = dir.appendingPathComponent("instance.pid")
        let fd = open(lockURL.path, O_CREAT | O_RDWR, 0o644)
        if fd < 0 { return true }
        if flock(fd, LOCK_EX | LOCK_NB) != 0 {
            close(fd)
            return false
        }
        acquiredFD = fd
        try? "\(ProcessInfo.processInfo.processIdentifier)".write(to: pidURL, atomically: true, encoding: .utf8)
        return true
    }

    private static func isOurProcess(_ pid: pid_t) -> Bool {
        var buf = [CChar](repeating: 0, count: 4096)
        let n = proc_pidpath(pid, &buf, UInt32(buf.count))
        guard n > 0 else { return false }
        return looksLikeOurExecutable(String(cString: buf))
    }

    private static var acquiredFD: Int32?
}
