import AppKit
import Combine
import CursorTokenCore
import Foundation
import SwiftUI
import UserNotifications

@MainActor
final class AppStore: ObservableObject {
    @Published var config: AppConfig
    @Published var usage: UsageSnapshot?
    @Published var errorMessage: String?
    @Published var updatedAt: String?
    /// 豆包账号独立的一套状态：Cursor 的 `usage` 只对应 Cursor 当前账号。
    @Published var doubaoUsage: UsageSnapshot?
    @Published var doubaoError: String?
    @Published var doubaoUpdatedAt: String?
    /// 当前豆包账号下一次允许拉取的时间，设置页显示「下次刷新」。
    @Published var doubaoNextRefreshAt: Date?
    @Published var doubaoLoginBusy = false
    @Published var settingsVisible = false
    @Published var flyoutVisible = false
    @Published var importStatus = ""
    @Published var accountCheckStatus = ""
    @Published var saveError = ""
    @Published var updateStatus = ""
    @Published var updateBusy = false
    @Published var focusToken = false
    @Published var pendingCursorImport = false
    @Published var settingsReloadTick = 0
    @Published var historyPoints: [HistoryPoint] = []
    @Published var dailyAvgBurn: Double?

    let client = CursorClient()
    let doubaoClient = DoubaoClient()
    var settingsDirectory: URL?

    private var refreshTask: Task<Void, Never>?
    private var updateTask: Task<Void, Never>?
    private var waitTask: Task<Void, Never>?
    private var refreshNow = false
    private var refreshGeneration = 0
    private var reconcileRunning = false
    private var reconcileAgain = false
    private var lastCloudNotify = ""
    /// 豆包额度官方约 5 分钟更新一次：每个账号至少隔 5 分钟再拉，手动刷新不受限。
    private var doubaoNextFetch: [String: Date] = [:]
    /// 真实用户名只拉一次；失败后不跟着每轮额度刷新反复拉整页 HTML。
    private var doubaoNameTried: Set<String> = []

    init(directory: URL? = nil) {
        settingsDirectory = directory
        config = ConfigStore.load(from: directory)
        ColorModeAppearance.apply(config.colorMode)
    }

    func start() {
        AppUpdater.confirmPending(store: self)
        LoginItem.apply(config.autostartEnabled)
        applyAppearance()
        if let acc = config.activeAccount {
            UsageHistory.adoptLegacyHistory(accountId: acc.id, directory: settingsDirectory ?? AppPaths.configDirectory())
        }
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound]) { _, _ in }
        reloadHistory()
        loopRefresh()
        loopUpdates()
        enqueueReconcile(refresh: false)
        if !config.hasAnyAccount {
            Task { @MainActor in
                try? await Task.sleep(nanoseconds: 300_000_000)
                self.openSettings(focusToken: true)
            }
        }
    }

    func stop() {
        refreshTask?.cancel()
        refreshTask = nil
        updateTask?.cancel()
        updateTask = nil
        waitTask?.cancel()
        waitTask = nil
        InstanceLock.release(directory: settingsDirectory)
    }

    func requestRefresh() {
        refreshGeneration += 1
        refreshNow = true
        accountCheckStatus = config.cursorAccounts.isEmpty ? "" : "正在检查账号状态…"
        waitTask?.cancel()
    }

    func openDashboard() {
        if let url = URL(string: UsageParser.dashboardURL(for: usage)) {
            NSWorkspace.shared.open(url)
        }
    }

    func openSettings(focusToken: Bool = false, startImport: Bool = false) {
        self.focusToken = focusToken
        if startImport { pendingCursorImport = true }
        settingsVisible = true
        SettingsWindowController.shared.show(store: self, focusToken: focusToken, startImport: startImport)
    }

    func openReport() {
        FlyoutWindowController.shared.close()
        ReportWindowController.shared.show(app: self)
    }

    func openCompare() {
        FlyoutWindowController.shared.close()
        CompareWindowController.shared.show(app: self)
    }

    func persistReportRange(accountId: String, start: String, end: String) {
        config = ConfigStore.update(from: settingsDirectory) { live in
            _ = live.setReportRange(accountId, start: start, end: end)
        }
    }

    func persistCompareCycle(accountId: String, membership: String?, start: String?, end: String?) {
        config = ConfigStore.update(from: settingsDirectory) { live in
            live.applySnapshot(to: accountId, membershipType: membership, billingCycleStart: start, billingCycleEnd: end)
        }
    }

    func applyConfig(_ cfg: AppConfig, refresh: Bool) {
        let prevToken = config.sessionToken
        let prevActive = config.activeAccountId
        let prevAuto = config.autostartEnabled
        let prevColor = config.colorMode
        config = cfg
        ColorModeAppearance.apply(cfg.colorMode)
        if !ConfigStore.save(cfg, to: settingsDirectory) {
            saveError = "无法写入配置（文件忙碌或加密失败），请稍后再试。"
            notify("保存失败", saveError)
        } else {
            saveError = ""
        }
        if prevAuto != cfg.autostartEnabled {
            LoginItem.apply(cfg.autostartEnabled)
        }
        if prevColor != cfg.colorMode {
            applyAppearance()
        }
        if refresh || prevToken != cfg.sessionToken || prevActive != cfg.activeAccountId {
            requestRefresh()
        }
        enqueueReconcile(refresh: false)
        CompareWindowController.shared.reloadIfVisible()
        ReportWindowController.shared.reloadIfVisible()
        objectWillChange.send()
    }

    /// 只切外观：不写同步标记、不重算报表、不重建整棵视图。
    /// 外观改成延后一轮 runloop 应用——在按钮的鼠标事件里立刻改 NSAppearance
    /// 会重绘整窗并吞掉抬起事件，表现就是「要点两下」。
    func applyColorMode(_ mode: String) {
        let normalized = AppConfig.normalizeColorMode(mode)
        guard normalized != config.colorMode else { return }
        config.colorMode = normalized
        if !ConfigStore.save(config, to: settingsDirectory) {
            saveError = "无法写入配置（文件忙碌或加密失败），请稍后再试。"
        } else {
            saveError = ""
        }
        DispatchQueue.main.async {
            self.applyAppearance()
        }
    }

    func applyAppearance() {
        ColorModeAppearance.apply(config.colorMode)
        SettingsWindowController.shared.applyAppearance(config.colorMode)
        FlyoutWindowController.shared.applyAppearance(config.colorMode)
        ReportWindowController.shared.applyAppearance(config.colorMode)
        CompareWindowController.shared.applyAppearance(config.colorMode)
    }

    func copySummary() {
        let text = StatusText.formatSummary(usage, errorMessage: errorMessage, updatedAt: updatedAt, accountLabel: config.activeAccount?.displayLabel)
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(text, forType: .string)
    }

    func reloadHistory() {
        let aid = config.activeAccount?.id
        let points = UsageHistory.loadRecent(days: 7, accountId: aid, directory: settingsDirectory)
        historyPoints = points
        dailyAvgBurn = UsageHistory.dailyAvgBurn(points: points)
    }

    func switchAccount(_ id: String) {
        var cfg = config
        guard cfg.setActiveAccount(id) else { return }
        usage = nil
        errorMessage = nil
        updatedAt = nil
        applyConfig(cfg, refresh: true)
        reloadHistory()
    }

    /// 只切换浮窗展示的豆包账号，不动 Cursor 的当前账号。
    func switchDoubaoAccount(_ id: String) {
        var cfg = config
        guard cfg.setActiveDoubaoAccount(id) else { return }
        doubaoUsage = nil
        doubaoError = nil
        doubaoUpdatedAt = nil
        doubaoNextRefreshAt = nil
        applyConfig(cfg, refresh: true)
    }

    func removeDoubaoAccount(_ id: String) {
        var cfg = config
        guard cfg.removeAccount(id) else { return }
        doubaoUsage = nil
        doubaoError = nil
        doubaoUpdatedAt = nil
        doubaoNextRefreshAt = nil
        applyConfig(cfg, refresh: false)
    }

    func loginToCursor(confirmClose: (Bool) -> Bool) async -> CursorAuthApplyResult {
        guard let acc = config.activeAccount else {
            return CursorAuthApplyResult(ok: false, message: "请先选择账号")
        }
        if acc.tokenDecryptFailed || acc.token.trimmingCharacters(in: .whitespaces).isEmpty {
            return CursorAuthApplyResult(ok: false, message: "当前账号没有可用 Token")
        }
        let preview = CursorAuth.buildValues(
            token: acc.token,
            email: CursorAuth.looksLikeEmail(acc.label) ? acc.label : nil,
            membershipType: acc.membershipType,
            displayName: acc.displayLabel
        )
        if preview.values == nil {
            return CursorAuthApplyResult(ok: false, message: preview.error)
        }
        let target = CursorAuth.resolveTarget()
        let running = target.map { CursorAuth.isRunning($0) } ?? false
        if !confirmClose(running) {
            return CursorAuthApplyResult(ok: false, message: "已取消")
        }
        let token = acc.token
        let email = CursorAuth.looksLikeEmail(acc.label) ? acc.label : nil
        let membership = acc.membershipType
        let display = acc.displayLabel
        return await Task.detached(priority: .userInitiated) {
            CursorAuth.apply(
                token: token,
                email: email,
                membershipType: membership,
                displayName: display,
                closeIfRunning: true,
                relaunch: true
            )
        }.value
    }

    func loopUpdates() {
        updateTask = Task { [weak self] in
            try? await Task.sleep(nanoseconds: UInt64(AppUpdate.startupDelay * 1_000_000_000))
            while let self, !Task.isCancelled {
                await self.checkForUpdate(manual: false)
                try? await Task.sleep(nanoseconds: UInt64(AppUpdate.autoCheckInterval * 1_000_000_000))
            }
        }
    }

    @discardableResult
    func checkForUpdate(manual: Bool) async -> String {
        if updateBusy { return updateStatus.isEmpty ? "正在检查更新…" : updateStatus }
        updateBusy = true
        if manual { updateStatus = "正在检查更新…" }
        let confirm: ((String) -> Bool)? = manual
            ? { [weak self] message in self?.confirmUpdate(message) ?? false }
            : nil
        let status = await AppUpdater.run(
            store: self,
            manual: manual,
            confirmApply: confirm,
            onProgress: { [weak self] progress in
                Task { @MainActor [weak self] in
                    self?.updateStatus = progress
                }
            }
        )
        updateStatus = status
        updateBusy = false
        if !manual && status.contains("发现新版本") {
            notify("自动更新", status)
        }
        return status
    }

    func confirmUpdate(_ message: String) -> Bool {
        let alert = NSAlert()
        alert.messageText = "安装更新"
        alert.informativeText = message + "。安装后会自动重启。"
        alert.addButton(withTitle: "安装并重启")
        alert.addButton(withTitle: "取消")
        return alert.runModal() == .alertFirstButtonReturn
    }

    func loopRefresh() {
        refreshTask = Task { [weak self] in
            while let self, !Task.isCancelled {
                await self.refreshAll()
                if Task.isCancelled { break }
                if self.refreshNow {
                    self.refreshNow = false
                    continue
                }
                let minutes = max(1, self.config.refreshIntervalMinutes)
                let seconds = UInt64(max(60, minutes * 60))
                let waiter = Task {
                    do {
                        try await Task.sleep(nanoseconds: seconds * 1_000_000_000)
                    } catch {
                        if !Task.isCancelled {
                            AppLog.log("刷新等待被打断: \(error.localizedDescription)")
                        }
                    }
                }
                self.waitTask = waiter
                await waiter.value
                self.waitTask = nil
                self.refreshNow = false
            }
        }
    }

    func enqueueReconcile(refresh: Bool) {
        Task { await runReconcile(refresh: refresh) }
    }

    private func runReconcile(refresh: Bool) async {
        if reconcileRunning {
            reconcileAgain = true
            return
        }
        reconcileRunning = true
        defer { reconcileRunning = false }
        repeat {
            reconcileAgain = false
            let changed = await reconcileAccounts()
            if refresh || changed { requestRefresh() }
        } while reconcileAgain
    }

    @discardableResult
    func reconcileAccounts() async -> Bool {
        guard config.syncEnabled else { return false }
        let snapshot = config
        let startedActive = snapshot.activeAccountId
        let (next, status) = await Task.detached(priority: .utility) {
            var copy = snapshot
            let status = CloudSync.reconcile(&copy)
            return (copy, status)
        }.value
        if status.ok || status.changed {
            let liveActive = config.activeAccountId
            let diverged = AccountSync.adoptReconciled(&config, next)
            let kept = AccountSync.keepLiveActiveAccount(
                &config,
                startedActive: startedActive,
                liveActive: liveActive
            )
            if diverged { reconcileAgain = true }
            if !ConfigStore.save(config, to: settingsDirectory) {
                AppLog.log("云同步结果写入配置失败")
            }
            notifyCloudSync(status)
            objectWillChange.send()
            return status.changed || kept || diverged
        }
        config.syncLastError = status.message
        // 服务端判定登录凭证已失效：后台副本里 clearSession 的结果必须落回真实配置，
        // 否则顶部仍按旧 token 显示「已登录」，和下面的过期提示自相矛盾。
        if status.authExpired && config.cloudLoggedIn {
            CloudSync.clearSession(&config)
            config.syncLastError = status.message
            if !ConfigStore.save(config, to: settingsDirectory) {
                AppLog.log("云同步会话清理写入配置失败")
            }
        }
        notifyCloudSync(status)
        objectWillChange.send()
        return false
    }

    func notifyCloudSync(_ status: SyncStatus) {
        let body = StatusText.formatCloudSyncNotify(ok: status.ok, message: status.message)
        if body.isEmpty {
            lastCloudNotify = ""
            return
        }
        if body == lastCloudNotify { return }
        lastCloudNotify = body
        notify("云同步失败", body)
    }

    func refreshAll() async {
        refreshGeneration += 1
        let generation = refreshGeneration
        let forced = refreshNow
        let targets = config.accounts.map {
            RefreshTarget(id: $0.id, token: $0.token, provider: $0.provider, decryptFailed: $0.tokenDecryptFailed)
        }
        if targets.isEmpty {
            usage = nil
            errorMessage = "未配置 Token，请打开设置粘贴"
            updatedAt = nil
            historyPoints = []
            dailyAvgBurn = nil
            accountCheckStatus = ""
            doubaoUsage = nil
            doubaoError = nil
            doubaoUpdatedAt = nil
            return
        }
        let activeId = config.activeAccountId
        let cursorTargets = targets.filter { $0.provider != AccountProvider.doubao }
        let doubaoTargets = targets.filter { $0.provider == AccountProvider.doubao }.filter {
            forced || Self.doubaoFetchAllowed(doubaoNextFetch[$0.id])
        }
        let ordered = cursorTargets.sorted { a, b in
            (a.id == activeId ? 0 : 1) < (b.id == activeId ? 0 : 1)
        } + doubaoTargets.sorted { a, b in
            (a.id == config.activeDoubaoAccount?.id ? 0 : 1) < (b.id == config.activeDoubaoAccount?.id ? 0 : 1)
        }
        let client = self.client
        let doubaoClient = self.doubaoClient
        var outcomes: [Outcome] = await RefreshGeneration.mapBounded(ordered) { acc in
            await Self.fetchOne(client: client, doubaoClient: doubaoClient, account: acc)
        }
        for o in outcomes {
            applyActiveOutcome(o, generation: generation)
        }
        for o in outcomes {
            if let snap = o.snap {
                UsageHistory.append(
                    remaining: snap.remainingPercent,
                    auto: snap.autoPercentUsed,
                    api: snap.apiPercentUsed,
                    accountId: o.id,
                    directory: settingsDirectory
                )
            }
        }
        for o in outcomes where o.provider == AccountProvider.doubao {
            doubaoNextFetch[o.id] = Self.nextDoubaoFetch(config.refreshIntervalMinutes)
        }

        var notices: [(String, String)] = []
        let cfg = ConfigStore.update(from: settingsDirectory) { live in
            for o in outcomes {
                guard let idx = live.accounts.firstIndex(where: { $0.id == o.id }) else { continue }
                if live.accounts[idx].isDoubao {
                    applyDoubaoOutcomeToConfig(&live, index: idx, outcome: o, notices: &notices)
                    continue
                }
                if let snap = o.snap {
                    var adjusted = snap
                    AccountValidity.applyEndOverride(&adjusted, account: live.accounts[idx])
                    live.applySnapshot(
                        to: o.id,
                        membershipType: adjusted.membershipType,
                        remaining: adjusted.remainingPercent,
                        error: "",
                        updatedAt: o.stamp,
                        billingCycleStart: adjusted.billingCycleStart,
                        billingCycleEnd: AccountValidity.storedCycleEnd(adjusted)
                    )
                    live.accounts[idx].authErrorNotified = false
                    var account = live.accounts[idx]
                    let found = AlertLogic.evaluate(config: live, account: &account, snapshot: adjusted)
                    live.accounts[idx] = account
                    for n in found { notices.append((n.title, n.body)) }
                    if let oi = outcomes.firstIndex(where: { $0.id == o.id }) {
                        outcomes[oi].snap = adjusted
                    }
                } else if let err = o.error {
                    live.applySnapshot(to: o.id, error: err, updatedAt: o.stamp)
                    if o.authError, !live.accounts[idx].authErrorNotified {
                        live.accounts[idx].authErrorNotified = true
                        if live.notifyEnabled {
                            let name = live.accounts[idx].displayLabel
                            notices.append(("Token 需要更新", name.isEmpty ? err : "账号「\(name)」：\(err)"))
                        }
                    }
                }
            }
            live.syncLegacyFields()
        }
        config = cfg
        // 老账号（添加时没有名字/头像）在下一轮刷新补一次真实昵称与头像。
        for doubao in cfg.doubaoAccounts where doubao.label.trimmingCharacters(in: .whitespaces).isEmpty || doubao.avatarURL.isEmpty {
            refreshDoubaoIdentity(accountId: doubao.id)
        }
        if let active = outcomes.first(where: { $0.id == cfg.activeAccountId }) {
            applyActiveOutcome(active, generation: generation)
        } else if cfg.cursorAccounts.isEmpty {
            usage = nil
            errorMessage = cfg.hasAnyAccount ? "未添加 Cursor 账号" : "未配置 Token，请打开设置粘贴"
            updatedAt = nil
            reloadHistory()
        } else {
            reloadHistory()
        }
        if let doubao = outcomes.first(where: { $0.id == cfg.activeDoubaoAccount?.id }) {
            applyDoubaoOutcome(doubao)
            doubaoNextRefreshAt = Self.nextDoubaoFetch(config.refreshIntervalMinutes)
        } else if cfg.activeDoubaoAccount == nil {
            doubaoUsage = nil
            doubaoError = nil
            doubaoUpdatedAt = nil
            doubaoNextRefreshAt = nil
        }
        for n in notices { notify(n.0, n.1) }
        if !refreshNow { accountCheckStatus = "" }
        enqueueReconcile(refresh: false)
    }

    private static func nextDoubaoFetch(_ refreshMinutes: Int) -> Date {
        let seconds = max(DoubaoCredentials.maxFetchIntervalSeconds, TimeInterval(max(1, refreshMinutes) * 60))
        return Date().addingTimeInterval(seconds)
    }

    private static func doubaoFetchAllowed(_ next: Date?) -> Bool {
        guard let next else { return true }
        return Date() >= next
    }

    private func applyDoubaoOutcome(_ o: Outcome) {
        if let snap = o.snap {
            doubaoUsage = snap
            doubaoError = nil
        } else if let err = o.error {
            doubaoUsage = nil
            doubaoError = err
        }
        doubaoUpdatedAt = o.stamp
    }

    private func applyDoubaoOutcomeToConfig(
        _ live: inout AppConfig,
        index: Int,
        outcome: Outcome,
        notices: inout [(String, String)]
    ) {
        let id = live.accounts[index].id
        if let snap = outcome.snap {
            live.applySnapshot(
                to: id,
                membershipType: snap.membershipType,
                remaining: snap.remainingPercent,
                error: "",
                updatedAt: outcome.stamp
            )
            live.accounts[index].authErrorNotified = false
            var account = live.accounts[index]
            // 豆包拿不到周期起止时间，本期对它关闭「耗尽风险」预警。
            let found = AlertLogic.evaluate(config: live, account: &account, snapshot: snap, exhaustionRisk: false)
            live.accounts[index] = account
            for n in found { notices.append((n.title, n.body)) }
        } else if let err = outcome.error {
            live.applySnapshot(to: id, error: err, updatedAt: outcome.stamp)
            if outcome.authError, !live.accounts[index].authErrorNotified {
                live.accounts[index].authErrorNotified = true
                if live.notifyEnabled {
                    notices.append(("豆包登录已失效", "账号「\(live.accounts[index].displayLabel)」：\(err)"))
                }
            }
        }
    }

    /// 设置页添加/重新登录豆包账号后落盘，并立即刷新浮窗状态。
    @discardableResult
    func saveDoubaoAccount(cookie: String, existingId: String? = nil, membershipType: String? = nil) -> String {
        var cfg = config
        do {
            _ = try cfg.upsertDoubaoAccount(cookie: cookie, membershipType: membershipType, existingId: existingId)
        } catch {
            return error.localizedDescription
        }
        doubaoUsage = nil
        doubaoError = nil
        doubaoUpdatedAt = nil
        doubaoNextRefreshAt = nil
        doubaoNameTried.removeAll()
        applyConfig(cfg, refresh: true)
        refreshDoubaoIdentity(accountId: existingId ?? cfg.activeDoubaoAccountId, force: true)
        return ""
    }

    /// 豆包账号真实用户名/头像来自 `/chat` 页面的 SSR 数据：登录后或账号还缺名字、头像时补一次。
    func refreshDoubaoIdentity(accountId: String? = nil, force: Bool = false) {
        let targetId = (accountId ?? config.activeDoubaoAccountId).trimmingCharacters(in: .whitespaces)
        guard let idx = config.accounts.firstIndex(where: { $0.id == targetId && $0.isDoubao }) else { return }
        if !force && doubaoNameTried.contains(targetId) { return }
        let known = config.accounts[idx]
        let hasName = !known.label.trimmingCharacters(in: .whitespaces).isEmpty
        if !force && hasName && !known.avatarURL.isEmpty { return }
        doubaoNameTried.insert(targetId)
        let token = known.token
        guard !token.isEmpty, !known.tokenDecryptFailed else { return }
        Task { @MainActor in
            let identity = (try? await self.doubaoClient.fetchAccountIdentity(cookie: token, timeout: 20))
                ?? DoubaoIdentity()
            let name = identity.name.trimmingCharacters(in: .whitespaces)
            let avatar = Account.sanitizeAvatarURL(identity.avatarURL)
            guard !name.isEmpty || !avatar.isEmpty else { return }
            guard let current = self.config.accounts.first(where: { $0.id == targetId }) else { return }
            let nameChanged = !name.isEmpty && current.label.trimmingCharacters(in: .whitespaces) != name
            let avatarChanged = !avatar.isEmpty && current.avatarURL != avatar
            guard nameChanged || avatarChanged else { return }
            let cfg = ConfigStore.update(from: self.settingsDirectory) { live in
                guard let i = live.accounts.firstIndex(where: { $0.id == targetId && $0.isDoubao }) else { return }
                if nameChanged { live.accounts[i].label = name }
                if avatarChanged { live.accounts[i].avatarURL = avatar }
            }
            self.config = cfg
            self.objectWillChange.send()
        }
    }

    /// 浮窗失效状态下的「一键重新登录」：直接开内嵌登录窗口，成功后原地换 Cookie 并刷新。
    func reloginActiveDoubao() {
        guard !doubaoLoginBusy, let account = config.activeDoubaoAccount else { return }
        doubaoLoginBusy = true
        Task { @MainActor in
            defer { doubaoLoginBusy = false }
            let cookie = await DoubaoLoginController.shared.run(accountId: account.id)
            guard let cookie, !cookie.isEmpty else { return }
            let message = saveDoubaoAccount(cookie: cookie, existingId: account.id)
            if !message.isEmpty {
                doubaoError = message
            } else {
                requestRefresh()
            }
        }
    }

    /// 在系统浏览器打开豆包订阅页（网页侧栏有「豆包订阅」入口）。
    func openDoubaoSubscription() {
        if let url = URL(string: DoubaoCredentials.loginURL) {
            NSWorkspace.shared.open(url)
        }
    }

    private func applyActiveOutcome(_ o: Outcome, generation: Int) {
        guard o.provider != AccountProvider.doubao else { return }
        guard RefreshGeneration.shouldApply(
            outcomeId: o.id,
            activeId: config.activeAccountId,
            outcomeGeneration: generation,
            currentGeneration: refreshGeneration
        ) else { return }
        if let snap = o.snap {
            usage = snap
            errorMessage = nil
            updatedAt = o.stamp
        } else if let err = o.error {
            usage = nil
            errorMessage = err
            updatedAt = o.stamp
        }
        reloadHistory()
    }

    private struct RefreshTarget: Sendable {
        var id: String
        var token: String
        var provider: String
        var decryptFailed: Bool
    }

    private struct Outcome: @unchecked Sendable {
        var id: String
        var provider: String
        var snap: UsageSnapshot?
        var error: String?
        var authError: Bool
        var stamp: String
    }

    nonisolated private static func fetchOne(client: CursorClient, doubaoClient: DoubaoClient, account: RefreshTarget) async -> Outcome {
        let stamp = AccountSync.nowIso()
        if account.decryptFailed {
            return Outcome(id: account.id, provider: account.provider, snap: nil, error: TokenProtector.decryptFailedMessage, authError: false, stamp: stamp)
        }
        if account.token.trimmingCharacters(in: .whitespaces).isEmpty {
            let message = account.provider == AccountProvider.doubao ? "未配置豆包 Cookie，请重新登录" : "未配置 Token，请打开设置粘贴"
            return Outcome(id: account.id, provider: account.provider, snap: nil, error: message, authError: false, stamp: stamp)
        }
        if account.provider == AccountProvider.doubao {
            do {
                let snap = try await doubaoClient.fetchQuota(cookie: account.token, timeout: 20)
                return Outcome(id: account.id, provider: account.provider, snap: snap, error: nil, authError: false, stamp: stamp)
            } catch let err as CursorAPIError {
                return Outcome(id: account.id, provider: account.provider, snap: nil, error: err.message, authError: err.isAuthError, stamp: stamp)
            } catch {
                return Outcome(id: account.id, provider: account.provider, snap: nil, error: "刷新失败：\(error.localizedDescription)", authError: false, stamp: stamp)
            }
        }
        do {
            let snap = try await client.fetchUsageSummary(sessionToken: account.token, timeout: 20)
            return Outcome(id: account.id, provider: account.provider, snap: snap, error: nil, authError: false, stamp: stamp)
        } catch let err as CursorAPIError {
            return Outcome(id: account.id, provider: account.provider, snap: nil, error: err.message, authError: err.isAuthError, stamp: stamp)
        } catch {
            return Outcome(id: account.id, provider: account.provider, snap: nil, error: "刷新失败：\(error.localizedDescription)", authError: false, stamp: stamp)
        }
    }

    func notify(_ title: String, _ body: String) {
        let content = UNMutableNotificationContent()
        content.title = title
        content.body = body
        let req = UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: nil)
        UNUserNotificationCenter.current().add(req)
    }
}

enum ColorModeAppearance {
    static func apply(_ mode: String) {
        let appearance = nsAppearance(mode)
        if NSApp.appearance !== appearance {
            NSApp.appearance = appearance
        }
    }

    static func nsAppearance(_ mode: String) -> NSAppearance? {
        switch AppConfig.normalizeColorMode(mode) {
        case "light": return NSAppearance(named: .aqua)
        case "dark": return NSAppearance(named: .darkAqua)
        default: return nil
        }
    }

    static func colorScheme(_ mode: String) -> ColorScheme? {
        switch AppConfig.normalizeColorMode(mode) {
        case "light": return .light
        case "dark": return .dark
        default: return nil
        }
    }

    static func apply(to window: NSWindow?, mode: String) {
        window?.appearance = nsAppearance(mode)
        // 外观切换后主动触发一次重绘，避免窗口底色/自绘按钮停在旧主题。
        window?.contentView?.needsDisplay = true
    }
}
