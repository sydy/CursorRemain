import AppKit
import CursorTokenCore
import SwiftUI
import UniformTypeIdentifiers

@MainActor
final class CompareStore: ObservableObject {
    let app: AppStore
    @Published var status = "已加载本地明细。点「同步」按各账号最新周期拉取。"
    @Published var syncing = false
    @Published var report = AccountCompareReport(rows: [], groups: [], holdingDays: UsageEvents.holdingDays)

    init(app: AppStore) {
        self.app = app
    }

    func loadCache() {
        report = buildReport()
        if report.rows.isEmpty {
            status = app.config.accounts.isEmpty
                ? "还没有账号。请先在设置里导入。"
                : "本地还没有明细。点「同步」按各账号最新周期拉取。"
        }
    }

    func reloadFromConfig() {
        if syncing { return }
        loadCache()
    }

    func sync() async {
        if syncing { return }
        if app.config.accounts.isEmpty {
            status = "还没有账号。请先在设置里导入。"
            return
        }
        syncing = true
        defer { syncing = false }
        status = "正在同步各账号最新周期…"
        let client = app.client
        let directory = app.settingsDirectory
        let activeId = app.config.activeAccountId
        let accounts = RefreshGeneration.prioritize(app.config.accounts) { $0.id == activeId }
        let total = accounts.count
        let sink = CompareStatusSink { [weak self] text in
            self?.status = text
        }
        let outcomes = await RefreshGeneration.mapBounded(Array(accounts.enumerated())) { item in
            let (offset, acc) = item
            let name = acc.displayLabel.trimmingCharacters(in: .whitespaces).isEmpty ? acc.id : acc.displayLabel
            let index = offset + 1
            sink.set(StatusText.formatCompareSyncProgress(index: index, total: total, name: name))
            return await compareSyncOne(client: client, account: acc, directory: directory) { page in
                sink.set(StatusText.formatCompareSyncProgress(index: index, total: total, name: name, page: page))
            }
        }
        var ok = 0
        var failures: [String] = []
        for o in outcomes {
            if let error = o.error {
                failures.append(error)
                continue
            }
            ok += 1
            app.persistCompareCycle(accountId: o.accountId, membership: o.membership, start: o.start, end: o.end)
        }
        report = buildReport()
        let stamp: String = {
            let f = DateFormatter()
            f.locale = Locale(identifier: "en_US_POSIX")
            f.timeZone = .current
            f.dateFormat = "HH:mm:ss"
            return f.string(from: Date())
        }()
        status = StatusText.formatCompareSync(ok: ok, failures: failures, stamp: stamp)
    }

    func exportCSV() {
        guard !report.rows.isEmpty else {
            status = StatusText.formatExportEmpty()
            return
        }
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.commaSeparatedText]
        panel.nameFieldStringValue = StatusText.formatExportFilename("cursor-account-compare")
        panel.canCreateDirectories = true
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            try UsageEvents.accountCompareToCSV(report).write(to: url, atomically: true, encoding: .utf8)
            status = "已导出 \(url.path)"
        } catch {
            status = "导出失败：\(error.localizedDescription)"
        }
    }

    func buildReport() -> AccountCompareReport {
        let items = app.config.accounts.map { acc in
            UsageEvents.compareInput(
                from: acc,
                events: UsageEvents.load(accountId: acc.id, teamScope: false, directory: app.settingsDirectory),
                monthlyPlanUsd: app.config.monthlyPlanUsd,
                usdCnyRate: app.config.usdCnyRate
            )
        }
        return UsageEvents.buildAccountCompareReport(items)
    }
}

final class CompareStatusSink: @unchecked Sendable {
    let apply: (String) -> Void

    init(_ apply: @escaping (String) -> Void) {
        self.apply = apply
    }

    func set(_ text: String) {
        if Thread.isMainThread {
            apply(text)
        } else {
            DispatchQueue.main.async { self.apply(text) }
        }
    }
}

struct CompareSyncOutcome: Sendable {
    var accountId: String
    var membership: String?
    var start: String?
    var end: String?
    var error: String?
}

func compareSyncOne(client: CursorClient, account: Account, directory: URL?, onPage: ((Int) -> Void)? = nil) async -> CompareSyncOutcome {
    let name = account.displayLabel.trimmingCharacters(in: .whitespaces).isEmpty ? account.id : account.displayLabel
    if account.tokenDecryptFailed {
        return CompareSyncOutcome(accountId: account.id, error: "\(name)：Token 解不开")
    }
    let token = account.token.trimmingCharacters(in: .whitespaces)
    if token.isEmpty {
        return CompareSyncOutcome(accountId: account.id, error: "\(name)：未配置 Token")
    }
    do {
        var snap = try await client.fetchUsageSummary(sessionToken: token, timeout: 20)
        AccountValidity.applyEndOverride(&snap, account: account)
        _ = try await UsageEvents.sync(
            client: client,
            token: token,
            accountId: account.id,
            usage: snap,
            teamScope: false,
            directory: directory,
            onPage: onPage
        )
        return CompareSyncOutcome(
            accountId: account.id,
            membership: snap.membershipType,
            start: snap.billingCycleStart,
            end: AccountValidity.storedCycleEnd(snap)
        )
    } catch let err as CursorAPIError {
        AppLog.log("compare sync \(account.id): \(err.message)")
        return CompareSyncOutcome(accountId: account.id, error: "\(name)：\(StatusText.shortError(err.message))")
    } catch {
        AppLog.log("compare sync \(account.id): \(error.localizedDescription)")
        return CompareSyncOutcome(accountId: account.id, error: "\(name)：\(StatusText.shortError(error.localizedDescription))")
    }
}

enum CompareLineKind {
    case header, account, category, total
}

struct CompareLine: Identifiable {
    var id: String
    var kind: CompareLineKind
    var name: String
    var membership: String
    var window: String
    var dailyHolding: String
    var paid: String
    var requests: String
    var tokens: String
    var perMillion: String
    var perRequest: String
    var bestUnit: Bool
}

struct CompareRootView: View {
    @ObservedObject var store: CompareStore

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 10) {
                Button("同步") { Task { await store.sync() } }
                    .disabled(store.syncing)
                    .frame(minWidth: 84)
                Button("导出 CSV") { store.exportCSV() }
                    .disabled(store.report.rows.isEmpty)
                    .frame(minWidth: 84)
                Spacer()
            }
            Text(store.status)
                .font(.caption)
                .foregroundStyle(.secondary)
            Text(StatusText.formatCompareHint(mixedWindows: UsageEvents.compareWindowsMixed(store.report.rows)))
                .font(.caption)
                .foregroundStyle(.secondary)
            table
        }
        .padding(16)
        .frame(minWidth: 900, minHeight: 520)
        .preferredColorScheme(ColorModeAppearance.colorScheme(store.app.config.colorMode))
        .onAppear { store.loadCache() }
        .onChange(of: store.app.config.activeAccountId) { _ in store.reloadFromConfig() }
        .onChange(of: store.app.config.monthlyPlanUsd) { _ in store.reloadFromConfig() }
        .onChange(of: store.app.config.usdCnyRate) { _ in store.reloadFromConfig() }
        .onChange(of: store.app.config.accounts.count) { _ in store.reloadFromConfig() }
    }

    var table: some View {
        Table(lines) {
            TableColumn("账号 / 分类") { line in
                nameCell(line)
            }
            .width(min: 168, ideal: 220)
            TableColumn("窗口") { line in
                cell(line.window, line)
            }
            .width(min: 110, ideal: 128)
            TableColumn("日均持有") { line in
                num(line.dailyHolding, line)
            }
            .width(min: 72, ideal: 84)
            TableColumn("实付") { line in
                num(line.paid, line)
            }
            .width(min: 72, ideal: 84)
            TableColumn("请求") { line in
                num(line.requests, line)
            }
            .width(min: 56, ideal: 68)
            TableColumn("Token") { line in
                num(line.tokens, line)
            }
            .width(min: 64, ideal: 80)
            TableColumn("¥/百万") { line in
                num(line.perMillion, line, best: line.bestUnit)
            }
            .width(min: 64, ideal: 76)
            TableColumn("¥/次") { line in
                num(line.perRequest, line)
            }
            .width(min: 56, ideal: 68)
        }
    }

    var lines: [CompareLine] {
        let best = bestPerMillion
        var out: [CompareLine] = []
        for group in store.report.groups {
            out.append(CompareLine(
                id: "head-\(group.channel)",
                kind: .header,
                name: group.channelLabel,
                membership: "",
                window: "",
                dailyHolding: "",
                paid: "",
                requests: "",
                tokens: "",
                perMillion: "",
                perRequest: "",
                bestUnit: false
            ))
            for row in group.rows {
                let name = accountName(row)
                let acc = store.app.config.accounts.first { $0.id == row.accountId }
                let note = UsageEvents.compareRowNote(
                    row,
                    hasToken: acc == nil || !(acc?.token.trimmingCharacters(in: .whitespaces).isEmpty ?? true),
                    lastError: acc?.lastError ?? ""
                )
                var window = "\(row.windowLabel) \(formatDays(row.windowDays))天"
                if !note.isEmpty { window += " · \(note)" }
                out.append(line(
                    id: "acc-\(row.accountId)",
                    kind: .account,
                    name: StatusText.formatCompareAccountName(name, isActive: row.accountId == store.app.config.activeAccountId),
                    membership: UsageParser.formatMembershipType(row.membershipType),
                    window: window,
                    dailyHolding: UsageEvents.formatCNY(row.dailyHoldingCny),
                    paid: UsageEvents.formatCNY(row.totalCny),
                    requests: formatCount(row.eventCount),
                    tokens: UsageParser.formatTokenCount(Double(row.totalTokens)),
                    perMillion: unit(row.cnyPerMillion),
                    perRequest: unit(row.cnyPerRequest),
                    bestUnit: UsageEvents.compareBestEligible(row) && isBest(row.cnyPerMillion, best)
                ))
                out.append(contentsOf: categoryLines(accountId: row.accountId, firstParty: row.firstParty, api: row.api, grok: row.grokBot))
            }
            out.append(line(
                id: "sum-\(group.channel)",
                kind: .total,
                name: "\(group.channelLabel)合计",
                membership: "",
                window: "",
                dailyHolding: UsageEvents.formatCNY(group.dailyHoldingCny),
                paid: UsageEvents.formatCNY(group.totalCny),
                requests: formatCount(group.eventCount),
                tokens: UsageParser.formatTokenCount(Double(group.totalTokens)),
                perMillion: unit(group.cnyPerMillion),
                perRequest: unit(group.cnyPerRequest),
                bestUnit: false
            ))
        }
        return out
    }

    var bestPerMillion: Double? {
        UsageEvents.compareBestPerMillion(store.report.rows)
    }

    func categoryLines(accountId: String, firstParty: AccountCompareCategory, api: AccountCompareCategory, grok: AccountCompareCategory) -> [CompareLine] {
        [
            categoryLine(accountId: accountId, name: "First-party", cat: firstParty),
            categoryLine(accountId: accountId, name: "API", cat: api),
            categoryLine(accountId: accountId, name: "Grok Bot", cat: grok),
        ]
    }

    func categoryLine(accountId: String, name: String, cat: AccountCompareCategory) -> CompareLine {
        line(
            id: "cat-\(accountId)-\(name)",
            kind: .category,
            name: name,
            membership: "",
            window: "",
            dailyHolding: "",
            paid: UsageEvents.formatCNY(cat.cny),
            requests: formatCount(cat.count),
            tokens: cat.tokens == 0 && cat.count == 0 ? "—" : UsageParser.formatTokenCount(Double(cat.tokens)),
            perMillion: unit(cat.cnyPerMillion),
            perRequest: unit(cat.cnyPerRequest),
            bestUnit: false
        )
    }

    func line(
        id: String,
        kind: CompareLineKind,
        name: String,
        membership: String,
        window: String,
        dailyHolding: String,
        paid: String,
        requests: String,
        tokens: String,
        perMillion: String,
        perRequest: String,
        bestUnit: Bool
    ) -> CompareLine {
        CompareLine(
            id: id,
            kind: kind,
            name: name,
            membership: membership,
            window: window,
            dailyHolding: dailyHolding,
            paid: paid,
            requests: requests,
            tokens: tokens,
            perMillion: perMillion,
            perRequest: perRequest,
            bestUnit: bestUnit
        )
    }

    func nameCell(_ line: CompareLine) -> some View {
        HStack(spacing: 6) {
            Text(line.name)
                .font(nameFont(line))
            if !line.membership.isEmpty, line.membership.lowercased() != line.name.lowercased() {
                Text(line.membership)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
        .padding(.leading, line.kind == .category ? 16 : 0)
        .foregroundStyle(line.kind == .category ? Color.secondary : Color.primary)
    }

    func cell(_ text: String, _ line: CompareLine) -> some View {
        Text(text)
            .font(valueFont(line))
            .foregroundStyle(line.kind == .category ? Color.secondary : Color.primary)
    }

    func num(_ text: String, _ line: CompareLine, best: Bool = false) -> some View {
        Text(text)
            .font(valueFont(line))
            .monospacedDigit()
            .foregroundStyle(best ? Color.green : (line.kind == .category ? Color.secondary : Color.primary))
            .frame(maxWidth: .infinity, alignment: .trailing)
    }

    func nameFont(_ line: CompareLine) -> Font {
        switch line.kind {
        case .header, .total: return .headline
        case .account: return .body.weight(.semibold)
        case .category: return .callout
        }
    }

    func valueFont(_ line: CompareLine) -> Font {
        switch line.kind {
        case .header, .total: return .body.weight(.semibold)
        case .account: return .body
        case .category: return .callout
        }
    }

    func accountName(_ row: AccountCompareRow) -> String {
        if let acc = store.app.config.accounts.first(where: { $0.id == row.accountId }) {
            let custom = acc.label.trimmingCharacters(in: .whitespaces)
            if !custom.isEmpty { return custom }
            return compactAccountId(acc.id)
        }
        let custom = row.label.trimmingCharacters(in: .whitespaces)
        if !custom.isEmpty, custom != row.accountId { return custom }
        return compactAccountId(row.accountId)
    }

    func compactAccountId(_ raw: String) -> String {
        let aid = raw.trimmingCharacters(in: .whitespaces)
        if aid.hasPrefix("user_"), aid.count > 18 {
            var body = String(aid.dropFirst(5))
            if body.hasPrefix("01") { body = String(body.dropFirst(2)) }
            return String(body.prefix(5)) + "…" + String(body.suffix(2))
        }
        if aid.count > 14 { return String(aid.prefix(12)) + "…" }
        return aid.isEmpty ? "未命名账号" : aid
    }

    func unit(_ amount: Double?) -> String {
        UsageEvents.formatCnyUnit(amount, suffix: "")
    }

    func formatCount(_ value: Int) -> String {
        let f = NumberFormatter()
        f.numberStyle = .decimal
        f.groupingSeparator = ","
        return f.string(from: NSNumber(value: value)) ?? "\(value)"
    }

    func formatDays(_ days: Double) -> String {
        if abs(days - days.rounded()) < 0.05 {
            return String(format: "%.0f", days.rounded())
        }
        return String(format: "%.2f", days)
    }

    func isBest(_ value: Double?, _ best: Double?) -> Bool {
        guard let value, let best else { return false }
        return abs(value - best) < 1e-9
    }
}

@MainActor
final class CompareWindowController: NSObject, NSWindowDelegate {
    static let shared = CompareWindowController()
    private var window: NSWindow?
    private var store: CompareStore?

    func show(app: AppStore) {
        FlyoutWindowController.shared.close()
        MenubarActivation.promoteForWindow()
        AppDelegate.ensureStatusItemVisible()
        if window == nil {
            let win = NSWindow(
                contentRect: NSRect(x: 0, y: 0, width: 1020, height: 640),
                styleMask: [.titled, .closable, .miniaturizable, .resizable],
                backing: .buffered,
                defer: false
            )
            win.title = "账号对比"
            win.minSize = NSSize(width: 900, height: 480)
            win.isReleasedWhenClosed = false
            win.delegate = self
            window = win
        }
        if store == nil {
            let compareStore = CompareStore(app: app)
            store = compareStore
            window?.contentView = NSHostingView(rootView: CompareRootView(store: compareStore))
            window?.center()
        } else {
            store?.reloadFromConfig()
        }
        window?.makeKeyAndOrderFront(nil)
    }

    func applyAppearance(_ mode: String) {
        ColorModeAppearance.apply(to: window, mode: mode)
    }

    func reloadIfVisible() {
        guard window?.isVisible == true else { return }
        store?.reloadFromConfig()
    }

    func close() {
        dismiss()
    }

    func windowShouldClose(_ sender: NSWindow) -> Bool {
        dismiss()
        return false
    }

    func windowWillClose(_ notification: Notification) {
        MenubarActivation.restoreAfterClosing(notification.object as? NSWindow)
    }

    private func dismiss() {
        window?.orderOut(nil)
        MenubarActivation.restoreNow(excluding: window)
    }
}
