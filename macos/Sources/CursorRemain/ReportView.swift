import AppKit
import CursorTokenCore
import SwiftUI
import UniformTypeIdentifiers

@MainActor
final class ReportStore: ObservableObject {
    let app: AppStore
    @Published var teamScope = false
    @Published var kind = ""
    @Published var category = ""
    @Published var model = ""
    @Published var cloud = ""
    @Published var chartHourly = false
    @Published var hiddenChartModels: Set<String> = []
    @Published var status = ""
    @Published var syncing = false
    @Published var events: [UsageEvent] = []
    @Published var modelNames: [String] = []
    @Published var startEnabled = false
    @Published var endEnabled = false
    @Published var startDate = Date()
    @Published var endDate = Date()
    private var loadedAccountId = ""

    init(app: AppStore) {
        self.app = app
        loadedAccountId = app.config.activeAccountId
        let acc = app.config.activeAccount
        let start = UsageEvents.sanitizeReportDate(acc?.reportStartDate)
        let end = UsageEvents.sanitizeReportDate(acc?.reportEndDate)
        startEnabled = !start.isEmpty
        endEnabled = !end.isEmpty
        if let value = UsageEvents.reportDateValue(start) { startDate = value }
        if let value = UsageEvents.reportDateValue(end) { endDate = value }
    }

    var isTeam: Bool { app.usage?.isTeamAccount == true }

    var filter: UsageReportFilter {
        let rawStart = startEnabled ? UsageEvents.reportDateString(from: startDate) : ""
        let rawEnd = endEnabled ? UsageEvents.reportDateString(from: endDate) : ""
        let normalized = UsageEvents.normalizeReportRange(rawStart, rawEnd)
        return UsageReportFilter(
            kind: kind,
            category: category,
            model: model,
            headless: cloud == "local" ? false : cloud == "cloud" ? true : nil,
            owningUser: "",
            startDate: normalized.start,
            endDate: normalized.end
        )
    }

    func persistDates() {
        let accountId = app.config.activeAccountId
        guard !accountId.isEmpty else { return }
        let rawStart = startEnabled ? UsageEvents.reportDateString(from: startDate) : ""
        let rawEnd = endEnabled ? UsageEvents.reportDateString(from: endDate) : ""
        let normalized = UsageEvents.normalizeReportRange(rawStart, rawEnd)
        if normalized.swapped {
            if let value = UsageEvents.reportDateValue(normalized.start) { startDate = value }
            if let value = UsageEvents.reportDateValue(normalized.end) { endDate = value }
        }
        app.persistReportRange(
            accountId: accountId,
            start: normalized.start,
            end: normalized.end
        )
    }

    var report: UsageReport {
        UsageEvents.buildReport(events, filter: filter, spend: spend, allocation: allocation)
    }

    var spend: CnySpendSettings {
        app.config.spendSettings(membership: app.usage?.membershipType)
    }

    var allocation: ReportAllocationWindow {
        ReportAllocationWindow.from(account: app.config.activeAccount, usage: app.usage)
    }

    var chartSeries: UsageChartSeries {
        UsageEvents.buildChart(report.events, hourly: chartHourly, hiddenModels: hiddenChartModels)
    }

    func toggleChartModel(_ name: String) {
        if hiddenChartModels.contains(name) {
            hiddenChartModels.remove(name)
        } else {
            hiddenChartModels.insert(name)
        }
    }

    func loadCache() {
        let accountId = app.config.activeAccountId
        events = UsageEvents.load(accountId: accountId, teamScope: teamScope, directory: app.settingsDirectory)
        refreshModelNames()
    }

    func reloadForCurrentAccount() {
        let accountId = app.config.activeAccountId
        if accountId != loadedAccountId {
            resetFilters()
            loadedAccountId = accountId
        }
        let acc = app.config.activeAccount
        let start = UsageEvents.sanitizeReportDate(acc?.reportStartDate)
        let end = UsageEvents.sanitizeReportDate(acc?.reportEndDate)
        startEnabled = !start.isEmpty
        endEnabled = !end.isEmpty
        if let value = UsageEvents.reportDateValue(start) { startDate = value }
        if let value = UsageEvents.reportDateValue(end) { endDate = value }
        loadCache()
    }

    func resetFilters() {
        kind = ""
        category = ""
        model = ""
        cloud = ""
        teamScope = false
        hiddenChartModels = []
    }

    func sync() async {
        if syncing { return }
        let token = app.config.activeAccount?.token ?? app.config.sessionToken
        let accountId = app.config.activeAccountId
        if token.trimmingCharacters(in: .whitespaces).isEmpty {
            status = StatusText.formatReportSyncResult(count: 0, fetched: 0, stamp: "", hasToken: false)
            return
        }
        syncing = true
        loadCache()
        status = StatusText.formatReportCacheStatus(count: events.count, accountLabel: app.config.activeAccount?.displayLabel)
        defer { syncing = false }
        do {
            let result = try await UsageEvents.sync(
                client: app.client,
                token: token,
                accountId: accountId,
                usage: app.usage,
                teamScope: teamScope,
                directory: app.settingsDirectory,
                onPage: { page in
                    Task { @MainActor in
                        self.status = StatusText.formatReportSyncProgress(page)
                    }
                }
            )
            events = result.events
            refreshModelNames()
            let stamp: String = {
                let f = DateFormatter()
                f.locale = Locale(identifier: "en_US_POSIX")
                f.timeZone = .current
                f.dateFormat = "HH:mm:ss"
                return f.string(from: Date())
            }()
            status = StatusText.formatReportSyncResult(
                count: events.count,
                fetched: result.fetched,
                stamp: stamp,
                truncated: result.truncated,
                totalAvailable: result.totalAvailable,
                note: result.note
            )
        } catch let err as CursorAPIError {
            refreshModelNames()
            status = StatusText.formatReportSyncError(err.message)
        } catch {
            refreshModelNames()
            status = StatusText.formatReportSyncError(error.localizedDescription)
        }
    }

    func exportCSV() {
        let rows = report.events
        guard !rows.isEmpty else { return }
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.commaSeparatedText]
        panel.nameFieldStringValue = StatusText.formatExportFilename(
            "cursor-usage",
            label: app.config.activeAccount?.displayLabel
        )
        panel.canCreateDirectories = true
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            try UsageEvents.toCSV(rows, spend: spend, allocationBase: events, allocation: allocation).write(to: url, atomically: true, encoding: .utf8)
            status = "已导出 \(url.path)"
        } catch {
            status = "导出失败：\(error.localizedDescription)"
        }
    }

    func refreshModelNames() {
        let names = Array(Set(events.map(\.model).filter { !$0.isEmpty })).sorted()
        modelNames = names
        if !model.isEmpty && !names.contains(model) {
            model = ""
        }
    }
}

struct ReportRootView: View {
    @ObservedObject var store: ReportStore

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            filters
            Text(store.status)
                .font(.caption)
                .foregroundStyle(.secondary)
            Text(kpiText)
                .font(.subheadline)
            UsageChartView(
                series: store.chartSeries,
                hiddenModels: store.hiddenChartModels,
                hourly: store.chartHourly,
                onHourlyChange: { store.chartHourly = $0 },
                onToggleModel: { store.toggleChartModel($0) }
            )
            models
            details
        }
        .padding(16)
        .frame(minWidth: 920, minHeight: 620)
        .onChange(of: store.teamScope) { _ in
            Task { await store.sync() }
        }
        .onChange(of: store.app.config.activeAccountId) { _ in
            store.reloadForCurrentAccount()
            Task { await store.sync() }
        }
        .onChange(of: store.startEnabled) { _ in store.persistDates() }
        .onChange(of: store.endEnabled) { _ in store.persistDates() }
        .onChange(of: store.startDate) { _ in store.persistDates() }
        .onChange(of: store.endDate) { _ in store.persistDates() }
    }

    var filters: some View {
        VStack(alignment: .leading, spacing: 8) {
            filterPickers
            dateFilters
        }
    }

    var filterPickers: some View {
        HStack(spacing: 10) {
            if store.isTeam {
                Picker("范围", selection: $store.teamScope) {
                    Text("仅自己").tag(false)
                    Text("全员").tag(true)
                }
                .frame(width: 140)
            }
            Picker("类型", selection: $store.kind) {
                Text("全部类型").tag("")
                Text("套餐内").tag(UsageEvents.kindIncluded)
                Text("免费").tag(UsageEvents.kindFree)
                Text("按需").tag(UsageEvents.kindOnDemand)
            }
            .frame(width: 140)
            Picker("额度", selection: $store.category) {
                Text("全部额度").tag("")
                Text("First-party").tag(UsageEvents.categoryFirstParty)
                Text("API").tag(UsageEvents.categoryAPI)
                Text("Grok Bot").tag(UsageEvents.categoryGrokBot)
            }
            .frame(width: 150)
            Picker("模型", selection: $store.model) {
                Text("全部模型").tag("")
                ForEach(store.modelNames, id: \.self) { name in
                    Text(name).tag(name)
                }
            }
            .frame(minWidth: 180)
            Picker("来源", selection: $store.cloud) {
                Text("全部来源").tag("")
                Text("本机").tag("local")
                Text("云端 Agent").tag("cloud")
            }
            .frame(width: 150)
            Button("同步") { Task { await store.sync() } }
                .disabled(store.syncing)
            Button("导出 CSV") { store.exportCSV() }
                .disabled(store.report.events.isEmpty)
            Spacer()
        }
    }

    var dateFilters: some View {
        HStack(spacing: 10) {
            Toggle("开始日期", isOn: $store.startEnabled)
            DatePicker("", selection: $store.startDate, displayedComponents: .date)
                .labelsHidden()
                .disabled(!store.startEnabled)
                .environment(\.timeZone, TimeZone.current)
            Toggle("结束日期", isOn: $store.endEnabled)
            DatePicker("", selection: $store.endDate, displayedComponents: .date)
                .labelsHidden()
                .disabled(!store.endEnabled)
                .environment(\.timeZone, TimeZone.current)
            Spacer()
        }
    }

    var kpiText: String {
        let report = store.report
        var mix = "套餐内 \(report.includedCount) · 免费 \(report.freeCount) · 按需 \(report.onDemandCount)"
        mix += "    First-party \(report.firstPartyCount) · API \(report.apiCount) · Grok Bot \(report.grokBotCount)"
        if report.headlessCount > 0 { mix += " · 云端 \(report.headlessCount)" }
        let cost = report.hasCost ? "    费用 \(UsageParser.formatUSDCents(report.totalCents))" : ""
        var text = "请求 \(report.eventCount)    Token \(UsageParser.formatTokenCount(Double(report.totalTokens)))    \(mix)\(cost)"
        if let usage = store.app.usage, usage.showsAmount {
            text += "    企业额度（展示） \(UsageParser.formatSpendRange(used: usage.usedCents, limit: usage.limitCents))"
        }
        text += StatusText.formatReportSpendKpi(
            totalCny: report.totalCny,
            planCny: report.planCny,
            onDemandCny: report.onDemandCny,
            usdCnyRate: report.usdCnyRate,
            usesActual: report.usesActualCny,
            windowPlanCny: report.windowPlanCny
        )
        return text
    }

    var models: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text("按模型").font(.caption).foregroundStyle(.secondary)
            Table(store.report.models) {
                TableColumn("模型") { row in Text(row.name) }
                TableColumn("Token") { row in Text(UsageParser.formatTokenCount(Double(row.tokens))) }
                TableColumn("费用") { row in Text(row.cents > 0 ? UsageParser.formatUSDCents(row.cents) : "—") }
                TableColumn("实付") { row in Text(row.cny > 0 ? UsageEvents.formatCNY(row.cny) : "—") }
                TableColumn("次数") { row in Text(String(row.count)) }
                TableColumn("云端") { row in Text(row.headlessCount > 0 ? String(row.headlessCount) : "—") }
            }
            .frame(minHeight: 120, maxHeight: 160)
        }
    }

    var details: some View {
        VStack(alignment: .leading, spacing: 4) {
            let empty = StatusText.formatReportFilterEmpty(store.events.count)
            Text(store.events.isEmpty || !store.report.events.isEmpty || empty.isEmpty ? "明细" : "明细 · \(empty)")
                .font(.caption)
                .foregroundStyle(.secondary)
            Table(store.report.events) {
                TableColumn("日期 (本地时间)") { ev in Text(UsageEvents.formatTime(ev.timestampMs)) }
                TableColumn("用户") { ev in Text(ev.userEmail) }
                TableColumn("类型") { ev in Text(UsageEvents.kindLabel(ev.kind)) }
                TableColumn("模型") { ev in Text(ev.model) }
                TableColumn("Token") { ev in Text(UsageParser.formatTokenCount(Double(ev.tokens))) }
                TableColumn("费用") { ev in Text(UsageEvents.formatCost(ev)) }
                TableColumn("实付") { ev in Text(UsageEvents.formatEventCny(ev)) }
                TableColumn("云端") { ev in Text(ev.isHeadless ? "是" : "否") }
            }
        }
    }
}

extension DailyUsageRow: Identifiable {
    public var id: String { date }
}

extension ModelUsageRow: Identifiable {
    public var id: String { name }
}

extension UsageEvent: Identifiable {}

@MainActor
final class ReportWindowController: NSObject, NSWindowDelegate {
    static let shared = ReportWindowController()
    private var window: NSWindow?
    private var store: ReportStore?

    func show(app: AppStore) {
        FlyoutWindowController.shared.close()
        MenubarActivation.promoteForWindow()
        AppDelegate.ensureStatusItemVisible()
        if window == nil {
            let win = NSWindow(
                contentRect: NSRect(x: 0, y: 0, width: 960, height: 680),
                styleMask: [.titled, .closable, .miniaturizable, .resizable],
                backing: .buffered,
                defer: false
            )
            win.title = "用量报表"
            win.minSize = NSSize(width: 880, height: 580)
            win.isReleasedWhenClosed = false
            win.delegate = self
            window = win
        }
        if store == nil {
            let reportStore = ReportStore(app: app)
            store = reportStore
            window?.contentView = NSHostingView(rootView: ReportRootView(store: reportStore))
            window?.center()
        } else {
            store?.reloadForCurrentAccount()
        }
        window?.makeKeyAndOrderFront(nil)
        Task { await store?.sync() }
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
