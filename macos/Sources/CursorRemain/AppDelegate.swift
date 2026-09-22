import AppKit
import CursorTokenCore
import SwiftUI

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private(set) var store: AppStore!
    var statusItem: StatusItemController?
    private var ipcObserver: NSObjectProtocol?

    func applicationDidFinishLaunching(_ notification: Notification) {
        AppLog.log("swift menubar start pid=\(ProcessInfo.processInfo.processIdentifier)")
        let action = InstanceIpc.launchAction(from: ProcessInfo.processInfo.arguments)
        if !InstanceLock.acquire() {
            if let action {
                forwardToRunningInstance(action)
                NSApp.terminate(nil)
                return
            }
            if !InstanceLock.acquireReplacingStale() {
                NSApp.setActivationPolicy(.regular)
                NSApp.activate(ignoringOtherApps: true)
                let alert = NSAlert()
                alert.messageText = "已在后台运行"
                alert.informativeText = "余量已经在菜单栏运行。若看不到图标，请打开「活动监视器」结束「\(AppPaths.displayName)」后再打开本程序。也可点菜单栏「•••」展开隐藏项。"
                alert.runModal()
                NSApp.terminate(nil)
                return
            }
        }
        NSApp.setActivationPolicy(.accessory)
        let store = AppStore()
        self.store = store
        statusItem = StatusItemController(store: store)
        store.start()
        listenForInstanceIpc()
        if let action { applyLaunchAction(action) }
        AppLog.log("swift menubar status item installed")
        Task { @MainActor in
            self.statusItem?.ensureVisible()
        }
    }

    func applicationDidBecomeActive(_ notification: Notification) {
        statusItem?.ensureVisible()
    }

    func applicationDidChangeScreenParameters(_ notification: Notification) {
        statusItem?.ensureVisible()
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        statusItem?.ensureVisible()
        return false
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        MenubarActivation.restoreAfterClosing()
        return false
    }

    func applicationWillTerminate(_ notification: Notification) {
        if let ipcObserver {
            DistributedNotificationCenter.default().removeObserver(ipcObserver)
            self.ipcObserver = nil
        }
        store?.stop()
        InstanceLock.release()
    }

    static func ensureStatusItemVisible() {
        (NSApp.delegate as? AppDelegate)?.statusItem?.ensureVisible()
    }

    private func listenForInstanceIpc() {
        ipcObserver = DistributedNotificationCenter.default().addObserver(
            forName: Notification.Name(InstanceIpc.notificationName),
            object: nil,
            queue: .main
        ) { [weak self] note in
            let raw = (note.object as? String)
                ?? (note.userInfo?[InstanceIpc.actionKey] as? String)
                ?? ""
            guard let action = InstanceIpc.Action(rawValue: raw) else { return }
            Task { @MainActor in
                self?.applyLaunchAction(action)
            }
        }
    }

    private func applyLaunchAction(_ action: InstanceIpc.Action) {
        statusItem?.ensureVisible()
        switch action {
        case .settings:
            store.openSettings()
        case .report:
            store.openReport()
        }
    }

    private func forwardToRunningInstance(_ action: InstanceIpc.Action) {
        DistributedNotificationCenter.default().postNotificationName(
            Notification.Name(InstanceIpc.notificationName),
            object: action.rawValue,
            userInfo: [InstanceIpc.actionKey: action.rawValue],
            deliverImmediately: true
        )
    }
}
