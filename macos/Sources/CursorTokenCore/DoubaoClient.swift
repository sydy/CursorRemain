import Foundation

/// 豆包额度客户端（方案 A：普通 HTTP 请求）。
///
/// 接口没有公开文档：HTTP 状态码恒为 200，成功与否只看响应体的 `code`。
public struct DoubaoClient: Sendable {
    public static let quotaURL = "https://www.doubao.com/alice/commerce/sale/subscription/quota/summary/"
    public static let profileURL = "https://www.doubao.com/chat/"
    public static let requestBody: [String: Any] = ["product_line": "membership"]
    static let browserUserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"

    public var session: URLSession
    public var timeout: TimeInterval

    public init(session: URLSession = .shared, timeout: TimeInterval = 20) {
        self.session = session
        self.timeout = timeout
    }

    public func fetchQuota(cookie rawCookie: String, timeout: TimeInterval? = nil) async throws -> UsageSnapshot {
        let cookie = DoubaoCredentials.normalize(rawCookie)
        if cookie.isEmpty { throw CursorAPIError("未配置豆包 Cookie", statusCode: 401) }
        let payload = try await requestQuota(cookie: cookie, timeout: timeout ?? self.timeout)
        let result = UsageParser.parseDoubaoQuota(payload)
        if let snapshot = result.snapshot { return snapshot }
        throw CursorAPIError(
            result.error ?? "查询失败：接口返回格式异常",
            statusCode: result.authError ? 401 : nil
        )
    }

    /// 取账号真实用户名：`/chat` 页面的 SSR 数据里带着账号昵称。
    /// 只在登录后或账号还没有名字时调用，不跟着每次额度刷新重复拉。
    public func fetchAccountName(cookie rawCookie: String, timeout: TimeInterval? = nil) async throws -> String {
        let cookie = DoubaoCredentials.normalize(rawCookie)
        if cookie.isEmpty { throw CursorAPIError("未配置豆包 Cookie", statusCode: 401) }
        guard let url = URL(string: Self.profileURL) else {
            throw CursorAPIError("接口地址无效")
        }
        var request = URLRequest(url: url, timeoutInterval: timeout ?? self.timeout)
        request.httpMethod = "GET"
        request.setValue("text/html,application/xhtml+xml", forHTTPHeaderField: "Accept")
        request.setValue("https://www.doubao.com", forHTTPHeaderField: "Origin")
        request.setValue("https://www.doubao.com/chat/", forHTTPHeaderField: "Referer")
        request.setValue(Self.browserUserAgent, forHTTPHeaderField: "User-Agent")
        request.setValue(cookie, forHTTPHeaderField: "Cookie")

        let data: Data
        let response: URLResponse
        do {
            (data, response) = try await session.data(for: request)
        } catch {
            throw CursorAPIError("网络错误: \(error.localizedDescription)")
        }
        let status = (response as? HTTPURLResponse)?.statusCode ?? 0
        if status == 401 || status == 403 {
            throw CursorAPIError(DoubaoCredentials.authMessage, statusCode: 401)
        }
        if status >= 400 {
            throw CursorAPIError("HTTP \(status)", statusCode: status)
        }
        guard let html = String(data: data, encoding: .utf8), !html.isEmpty else { return "" }
        return UsageParser.doubaoAccountName(html: html)
    }

    /// 只做登录态探测：`code` 不是 710012001 就认为已登录。不解析额度。
    public static func cookieIsLoggedIn(_ cookie: String, session: URLSession = .shared) async -> Bool {
        let client = DoubaoClient(session: session, timeout: 12)
        guard let payload = try? await client.requestQuota(cookie: cookie, timeout: 12) else { return false }
        let result = UsageParser.parseDoubaoQuota(payload)
        return !result.authError
    }

    func requestQuota(cookie: String, timeout: TimeInterval) async throws -> JSONValue {
        guard let url = URL(string: Self.quotaURL) else {
            throw CursorAPIError("接口地址无效")
        }
        var request = URLRequest(url: url, timeoutInterval: timeout)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("https://www.doubao.com", forHTTPHeaderField: "Origin")
        request.setValue("https://www.doubao.com/chat/", forHTTPHeaderField: "Referer")
        request.setValue("Mozilla/5.0 CursorRemain/1.0", forHTTPHeaderField: "User-Agent")
        request.setValue(cookie, forHTTPHeaderField: "Cookie")
        request.httpBody = try JSONSerialization.data(withJSONObject: Self.requestBody)

        var lastError: CursorAPIError?
        for attempt in 0..<3 {
            let data: Data
            let response: URLResponse
            do {
                (data, response) = try await session.data(for: request)
            } catch {
                lastError = CursorAPIError("网络错误: \(error.localizedDescription)")
                if attempt == 2 { throw lastError! }
                try? await Task.sleep(nanoseconds: UInt64(250 << attempt) * 1_000_000)
                continue
            }
            let status = (response as? HTTPURLResponse)?.statusCode ?? 0
            if status >= 500 || status == 429 {
                lastError = CursorAPIError("HTTP \(status)", statusCode: status)
                if attempt == 2 { throw lastError! }
                try? await Task.sleep(nanoseconds: UInt64(250 << attempt) * 1_000_000)
                continue
            }
            if status >= 400 {
                let detail = String(data: data, encoding: .utf8)?.prefix(200) ?? ""
                let safe = detail.unicodeScalars.map { $0.isASCII ? Character($0) : "?" }.map(String.init).joined()
                throw CursorAPIError(safe.isEmpty ? "HTTP \(status)" : "HTTP \(status): \(safe)", statusCode: status)
            }
            if data.isEmpty { return JSONValue([:]) }
            do {
                return try JSONValue.parseObject(data)
            } catch {
                throw CursorAPIError("接口返回非 JSON")
            }
        }
        throw lastError ?? CursorAPIError("网络错误")
    }
}
