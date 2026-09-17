import AppKit
import CursorTokenCore
import WebKit

@MainActor
final class PasswordLoginController: NSObject, WKNavigationDelegate, NSWindowDelegate {
    static let shared = PasswordLoginController()

    private var window: NSWindow?
    private var web: WKWebView?
    private var status: NSTextField?
    private var email = ""
    private var password = ""
    private var continuation: CheckedContinuation<String?, Never>?
    private var timer: Timer?
    private var deadline = Date()
    private var done = false

    func run(email: String, password: String) async -> String? {
        if continuation != nil { return nil }
        self.email = email
        self.password = password
        done = false
        MenubarActivation.promoteForWindow()
        showWindow()
        await resetSession()
        deadline = Date().addingTimeInterval(TimeInterval(CursorPasswordLogin.timeoutSeconds))
        timer = Timer.scheduledTimer(withTimeInterval: 1.5, repeats: true) { [weak self] _ in
            Task { @MainActor in await self?.tick() }
        }
        web?.load(URLRequest(url: URL(string: CursorPasswordLogin.loginURL)!))
        setStatus("正在自动填写，验证码请在窗口里完成…")
        return await withCheckedContinuation { continuation = $0 }
    }

    private func showWindow() {
        if window == nil {
            let win = NSWindow(
                contentRect: NSRect(x: 0, y: 0, width: 880, height: 680),
                styleMask: [.titled, .closable, .miniaturizable, .resizable],
                backing: .buffered,
                defer: false
            )
            win.title = "登录 Cursor 账号"
            win.isReleasedWhenClosed = false
            win.delegate = self
            let content = NSView(frame: win.contentView?.bounds ?? .zero)
            content.autoresizingMask = [.width, .height]
            let config = WKWebViewConfiguration()
            let webView = WKWebView(frame: .zero, configuration: config)
            webView.navigationDelegate = self
            webView.translatesAutoresizingMaskIntoConstraints = false
            let statusField = NSTextField(labelWithString: "正在打开官方登录页…")
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

    private func resetSession() async {
        guard let store = web?.configuration.websiteDataStore else { return }
        await withCheckedContinuation { (cont: CheckedContinuation<Void, Never>) in
            store.removeData(ofTypes: [WKWebsiteDataTypeCookies], modifiedSince: .distantPast) {
                cont.resume()
            }
        }
    }

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        Task { await inject(); await capture() }
    }

    private func tick() async {
        if done { return }
        if Date() >= deadline {
            finish(nil, "等待登录超时，请完成验证码后重试，或改用手动粘贴。")
            return
        }
        await inject()
        await capture()
    }

    private func inject() async {
        guard !done, let web else { return }
        let script = CursorPasswordLogin.autofillScript(email: email, password: password)
        if let state = try? await web.evaluateJavaScript(script) as? String, state == "need-user" {
            setStatus("请在窗口里完成验证码或邮箱验证码…")
        }
    }

    private func capture() async {
        guard !done, let store = web?.configuration.websiteDataStore.httpCookieStore else { return }
        let cookies: [HTTPCookie] = await withCheckedContinuation { cont in
            store.getAllCookies { cont.resume(returning: $0) }
        }
        let pairs = cookies.map { ($0.name, $0.value) }
        guard let token = CursorPasswordLogin.tokenFromCookies(pairs), !token.isEmpty else { return }
        finish(token, "已获取 Token")
    }

    @objc private func cancelLogin() {
        finish(nil, "已取消")
    }

    func windowShouldClose(_ sender: NSWindow) -> Bool {
        if !done { finish(nil, "已取消") }
        return true
    }

    private func finish(_ token: String?, _ message: String) {
        if done { return }
        done = true
        timer?.invalidate()
        timer = nil
        setStatus(message)
        window?.orderOut(nil)
        MenubarActivation.restoreNow(excluding: window)
        continuation?.resume(returning: token)
        continuation = nil
    }

    private func setStatus(_ text: String) {
        status?.stringValue = text
    }
}
