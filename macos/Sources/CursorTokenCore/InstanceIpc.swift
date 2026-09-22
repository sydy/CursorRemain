import Foundation

/// Launch flags shared with the running menu-bar instance.
/// Windows uses `TrayIpc` window messages; macOS posts a distributed notification.
public enum InstanceIpc {
    public static let notificationName = "cn.harker.CursorRemain.open"
    public static let actionKey = "action"

    public enum Action: String, Equatable, Sendable {
        case settings
        case report
    }

    public static func launchAction(from arguments: [String]) -> Action? {
        func has(_ flag: String) -> Bool {
            arguments.contains { $0.compare(flag, options: .caseInsensitive) == .orderedSame }
        }
        if has("--report") { return .report }
        if has("--settings") { return .settings }
        return nil
    }
}
