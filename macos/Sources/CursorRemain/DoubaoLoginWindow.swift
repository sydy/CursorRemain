import AppKit
import CursorTokenCore
import WebKit

/// 豆包登录窗口：内嵌 doubao.com，用户自己完成扫码/手机号登录，成功后读取 Cookie。
/// 站点数据按账号 id 隔离；没有任何自动填写，也不碰用户输入的密码。
@MainActor
final class DoubaoLoginController: NSObject, WKNavigationDelegate, NSWindowDelegate {
    static let shared = DoubaoLoginController()
    private static var memoryStores: [String: WKWebsiteDataStore] = [:]
    private static let timeoutSeconds = 180

    private var window: NSWindow?
    private var web: WKWebView?
    private var status: NSTextField?
    private var accountId = ""
    private var continuation: CheckedContinuation<String?, Never>?
    private var timer: Timer?
    private var deadline = Date()
    private var nextValidate = Date.distantPast
    private var validating = false
    private var lastCandidate = ""
    private var done = false

    func run(accountId: String) async -> String? {
        if continuation != nil { return nil }
        let ident = accountId.trimmingCharacters(in: .whitespaces)
        guard !ident.isEmpty else { return nil }
        if self.accountId != ident {
            window?.orderOut(nil)
            window = nil
            web = nil
        }
        self.accountId = ident
        done = false
        lastCandidate = ""
        nextValidate = .distantPast
        MenubarActivation.promoteForWindow()
        showWindow()
        deadline = Date().addingTimeInterval(TimeInterval(Self.timeoutSeconds))
        timer = Timer.scheduledTimer(withTimeInterval: 1.5, repeats: true) { [weak self] _ in
            Task { @MainActor in await self?.capture() }
        }
        web?.load(URLRequest(url: URL(string: DoubaoCredentials.loginURL)!))
        setStatus("请在此窗口登录豆包，登录成功后会自动保存…")
        return await withCheckedContinuation { continuation = $0 }
    }

    private func showWindow() {
        if window == nil {
            let win = NSWindow(
                contentRect: NSRect(x: 0, y: 0, width: 1000, height: 750),
                styleMask: [.titled, .closable, .miniaturizable, .resizable],
                backing: .buffered,
                defer: false
            )
            win.minSize = NSSize(width: 800, height: 600)
            win.title = "登录豆包账号"
            win.isReleasedWhenClosed = false
            win.delegate = self
            let content = NSView(frame: win.contentView?.bounds ?? .zero)
            content.autoresizingMask = [.width, .height]
            let config = WKWebViewConfiguration()
            config.websiteDataStore = dataStore(for: accountId)
            let webView = WKWebView(frame: .zero, configuration: config)
            webView.navigationDelegate = self
            webView.translatesAutoresizingMaskIntoConstraints = false
            let statusField = NSTextField(labelWithString: "正在打开豆包…")
            statusField.translatesAutoresizingMaskIntoConstraints = false
            statusField.lineBreakMode = .byTruncatingTail
            let cancel = NSButton(title: "取消", target: self, action: #selector(cancelLogin))
            cancel.translatesAutoresizingMaskIntoConstraints = false
            content.addSubview(webView)
            content.addSubview(statusField)
            content.addSubview(cancel)
            NSLayoutConstraint.activate([
                webView.topAnchor.constraint(equalTo: content.topAnchor),
                webView.leadingAnchor.constraint(equalTo: content.leadingAnchor),
                webView.trailingAnchor.constraint(equalTo: content.trailingAnchor),
                webView.bottomAnchor.constraint(equalTo: statusField.topAnchor, constant: -8),
                statusField.leadingAnchor.constraint(equalTo: content.leadingAnchor, constant: 12),
                statusField.bottomAnchor.constraint(equalTo: content.bottomAnchor, constant: -10),
                statusField.trailingAnchor.constraint(equalTo: cancel.leadingAnchor, constant: -12),
                cancel.trailingAnchor.constraint(equalTo: content.trailingAnchor, constant: -12),
                cancel.centerYAnchor.constraint(equalTo: statusField.centerYAnchor),
            ])
            win.contentView = content
            window = win
            web = webView
            status = statusField
        }
        window?.center()
        window?.makeKeyAndOrderFront(nil)
    }

    private func dataStore(for accountId: String) -> WKWebsiteDataStore {
        let uuidText = DoubaoCredentials.profileUUID(accountId)
        if #available(macOS 14.0, *), let uuid = UUID(uuidString: uuidText) {
            return WKWebsiteDataStore(forIdentifier: uuid)
        }
        if let store = Self.memoryStores[accountId] { return store }
        let store = WKWebsiteDataStore.nonPersistent()
        Self.memoryStores[accountId] = store
        return store
    }

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        guard let host = webView.url?.host?.lowercased() else { return }
        if !host.hasSuffix(DoubaoCredentials.cookieDomainSuffix) {
            setStatus("请回到豆包页面继续登录…")
        }
        Task { await capture() }
    }

    private func capture() async {
        if done { return }
        if Date() >= deadline {
            finish(nil, "等待登录超时，请重试或改用手动粘贴 Cookie。")
            return
        }
        guard let store = web?.configuration.websiteDataStore.httpCookieStore else { return }
        let cookies: [HTTPCookie] = await withCheckedContinuation { cont in
            store.getAllCookies { cont.resume(returning: $0) }
        }
        let pairs = cookies
            .filter { $0.domain.hasSuffix(DoubaoCredentials.cookieDomainSuffix) }
            .map { "\($0.name)=\($0.value)" }
        let header = pairs.joined(separator: "; ")
        guard !header.isEmpty, DoubaoCredentials.looksLoggedIn(header) else { return }
        if header == lastCandidate || validating || Date() < nextValidate { return }
        lastCandidate = header
        validating = true
        nextValidate = Date().addingTimeInterval(4)
        setStatus("已读取登录态，正在确认…")
        let ok = await DoubaoClient.cookieIsLoggedIn(header)
        validating = false
        if done { return }
        if ok {
            finish(header, "已登录")
        } else {
            setStatus("登录态还没生效，请稍等或完成一次额度查询页面刷新…")
        }
    }

    @objc private func cancelLogin() {
        finish(nil, "已取消")
    }

    func windowShouldClose(_ sender: NSWindow) -> Bool {
        if !done { finish(nil, "已取消") }
        return true
    }

    private func finish(_ cookie: String?, _ message: String) {
        if done { return }
        done = true
        timer?.invalidate()
        timer = nil
        setStatus(message)
        window?.orderOut(nil)
        MenubarActivation.restoreNow(excluding: window)
        continuation?.resume(returning: cookie)
        continuation = nil
    }

    private func setStatus(_ text: String) {
        status?.stringValue = text
    }
}
