import AppKit
import CursorTokenCore
import SwiftUI
import UniformTypeIdentifiers

struct SettingsRootView: View {
    @ObservedObject var store: AppStore
    @State private var extraOpen = false
    @State private var importing = false
    @State private var tokenText = ""
    @State private var intervalText = "10"
    @State private var planUsdText = "0"
    @State private var actualCnyText = "0"
    @State private var channel = ""
    @State private var cnyRateText = "7.5"
    @State private var thresholdText = "50,20,5"
    @State private var cloudEmail = ""
    @State private var cloudPassword = ""
    @State private var syncStatus = ""
    @State private var showChangePassword = false
    @State private var oldCloudPassword = ""
    @State private var newCloudPassword = ""
    @State private var confirmCloudPassword = ""
    @State private var deletePassword = ""
    @State private var showDeleteConfirm = false
    @State private var hint = ""
    @FocusState private var tokenFocused: Bool
    var startImport: Bool = false
    var focusToken: Bool = false

    var body: some View {
        TabView {
            accountPage.tabItem { Label("账户", systemImage: "person.circle") }
            notifyPage.tabItem { Label("通知", systemImage: "bell") }
            menuPage.tabItem { Label("菜单栏", systemImage: "menubar.rectangle") }
            syncPage.tabItem { Label("同步", systemImage: "arrow.triangle.2.circlepath") }
        }
        .padding(20)
        .frame(width: 540, height: 680)
        .onAppear {
            reloadFields()
            if focusToken || store.focusToken {
                tokenFocused = true
            }
            if startImport || store.pendingCursorImport {
                store.pendingCursorImport = false
                Task { await importFrom(prefer: "cursor-app") }
            }
        }
        .onChange(of: store.settingsReloadTick) { _ in
            reloadFields()
        }
        .onChange(of: store.focusToken) { focused in
            if focused { tokenFocused = true }
        }
        .onChange(of: store.pendingCursorImport) { pending in
            if pending {
                store.pendingCursorImport = false
                Task { await importFrom(prefer: "cursor-app") }
            }
        }
        .onChange(of: store.config.activeAccountId) { _ in
            actualCnyText = formatDecimal(store.config.activeAccount?.actualCny ?? 0)
            channel = store.config.activeAccount?.channel ?? ""
        }
    }

    var accountPage: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("当前账号").font(.headline)
            Picker("账号", selection: activeBinding) {
                ForEach(store.config.accounts, id: \.id) { acc in
                    Text(acc.caption(isActive: acc.id == store.config.activeAccountId)).tag(acc.id)
                }
            }
            .labelsHidden()
            HStack {
                Button("重命名") { rename() }
                Button("删除") { deleteAccount() }
                Button("登录到 Cursor") { Task { await loginToCursor() } }
                    .disabled(importing)
            }
            Picker("账号类型", selection: kindBinding) {
                Text("长期账号").tag(AccountValidity.longTerm)
                Text("临时账号").tag(AccountValidity.temporary)
            }
            .disabled(store.config.activeAccount == nil)
            if AccountValidity.isTemporary(store.config.activeAccount) {
                DatePicker("开始时间", selection: startBinding, displayedComponents: [.date, .hourAndMinute])
                HStack {
                    Text("有效时间")
                    Stepper(value: daysBinding, in: 0...AccountValidity.maxDays) {
                        Text("\(store.config.activeAccount?.tempValidDays ?? 0) 天")
                    }
                    Stepper(value: hoursBinding, in: 0...AccountValidity.maxHours) {
                        Text("\(store.config.activeAccount?.tempValidHours ?? 0) 小时")
                    }
                }
                Text(endCaption)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            Text("成本与渠道").font(.headline).padding(.top, 4)
            Picker("渠道", selection: $channel) {
                Text("未标").tag("")
                Text("自费").tag(UsageEvents.channelSelfPay)
                Text("第三方").tag(UsageEvents.channelThirdParty)
            }
            .disabled(store.config.activeAccount == nil)
            HStack {
                Text("实际成本（人民币）")
                TextField("0", text: $actualCnyText).frame(width: 72)
            }
            .disabled(store.config.activeAccount == nil)
            Text("仅当前账号。短期号请买价÷天数×30。企业 / 团队额度不是真实支出；填了实际成本则按该成本分摊（含按需），优先于月费，按需不再按官网标价另加。")
                .font(.caption)
                .foregroundStyle(.secondary)
            Text("添加账号（每行一个 Token 或邮箱密码，请勿分享；已保存的不会显示）").font(.headline).padding(.top, 8)
            TextEditor(text: $tokenText)
                .font(.system(.body, design: .monospaced))
                .frame(minHeight: 88, maxHeight: 120)
                .overlay(RoundedRectangle(cornerRadius: 6).stroke(Color.secondary.opacity(0.3)))
                .focused($tokenFocused)
            HStack {
                Button("从 Cursor 导入") { Task { await importFrom(prefer: "cursor-app") } }
                    .disabled(importing)
                Button("添加") { Task { await addPastedAccounts() } }
                    .disabled(importing)
            }
            Text("可粘贴 Token，或 name@example.com:密码、账号：邮箱密码：密码，多行则逐个添加。邮箱密码会打开官方登录页；验证码请在窗口里完成。此会话只能查用量。密码会加密保存并随云同步。")
                .font(.caption)
                .foregroundStyle(.secondary)
            DisclosureGroup("其他导入方式", isExpanded: $extraOpen) {
                HStack {
                    Button("Safari 登录") { Task { await loginAndImport(prefer: "safari") } }
                        .disabled(importing)
                    Button("Firefox 登录") { Task { await loginAndImport(prefer: "firefox") } }
                        .disabled(importing)
                    Button("仅扫描 Cookie") { Task { await importFrom(prefer: nil) } }
                        .disabled(importing)
                }
            }
            if !FullDiskAccess.safariCookiesReadable() {
                HStack(alignment: .top, spacing: 8) {
                    Text("Safari 导入需要「完全磁盘访问权限」。")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                    Button("打开系统设置") { FullDiskAccess.openPrivacySettings() }
                        .font(.caption)
                }
            }
            Text(store.importStatus.isEmpty ? "已登录 Cursor 时可直接导入。浏览器 Cookie 仅作备选，不能写回客户端切号。" : store.importStatus)
                .font(.caption)
                .foregroundStyle(.secondary)
                .frame(maxWidth: .infinity, alignment: .leading)
            Spacer()
            footer
        }
    }

    var notifyPage: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("刷新与通知").font(.title3.bold())
            HStack {
                Text("刷新间隔（分钟）")
                TextField("10", text: $intervalText).frame(width: 72)
            }
            HStack {
                Text("月费（美元）")
                TextField("20", text: $planUsdText).frame(width: 72)
            }
            HStack {
                Text("美元兑人民币")
                TextField("7.5", text: $cnyRateText).frame(width: 72)
            }
            Text("月费填 0 则按套餐预填：Pro $20 / Pro+ $60 / Ultra $200。年付请填折合月费。实际成本在「账户」里按账号填写。")
                .font(.caption)
                .foregroundStyle(.secondary)
            HStack {
                Text("告警阈值，例如 50,20,5")
                TextField("50,20,5", text: $thresholdText).frame(width: 160)
            }
            Toggle("启用用量通知", isOn: notifyBinding)
            Toggle("启用耗尽风险通知", isOn: exhaustBinding)
            Spacer()
            footer
        }
    }

    var menuPage: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("菜单栏与启动").font(.title3.bold())
            Picker("菜单栏图标", selection: modeBinding) {
                Text("圆环百分比").tag("ring")
                Text("纯数字").tag("number")
                Text("仅色点").tag("dot")
            }
            Toggle("开机自启（下次登录生效）", isOn: autostartBinding)
            Text("更新").font(.headline).padding(.top, 8)
            Toggle("自动检查并安装更新", isOn: autoUpdateBinding)
            Text("当前版本  \(AppUpdate.displayVersion())")
                .font(.caption)
                .foregroundStyle(.secondary)
            Button("检查更新") {
                Task { await store.checkForUpdate(manual: true) }
            }
            .disabled(store.updateBusy)
            Text(store.updateStatus.isEmpty ? "对照 GitHub 正式版（v*）。打包版会下载替换后重启；开发运行则打开下载页。若弹出钥匙串授权，选一次「始终允许」即可，之后更新不再要登录密码。" : store.updateStatus)
                .font(.caption)
                .foregroundStyle(.secondary)
            Spacer()
            footer
        }
    }

    var syncPage: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("云同步").font(.title3.bold())
            if store.config.cloudLoggedIn {
                Text("已登录  \(store.config.cloudEmail)")
                HStack {
                    Button("立即同步") { syncNow() }
                    Button("退出登录") { logoutCloud() }
                    Button("修改密码") { showChangePassword.toggle() }
                    Button("注销账号") { showDeleteConfirm.toggle() }
                    Button("导出…") { exportFile() }
                    Button("导入…") { importFile() }
                }
                if showChangePassword {
                    SecureField("当前密码", text: $oldCloudPassword)
                    SecureField("新密码（至少 8 位）", text: $newCloudPassword)
                    SecureField("确认新密码", text: $confirmCloudPassword)
                    Button("确认修改") { changePassword() }
                }
                if showDeleteConfirm {
                    Text("将删除云端账号和加密数据，本机账号不受影响。忘记密码也不能恢复云端数据。")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                    SecureField("输入登录密码确认", text: $deletePassword)
                    Button("确认注销") { deleteCloudAccount() }
                }
            } else {
                TextField("邮箱", text: $cloudEmail)
                SecureField("密码（至少 8 位，也用于加密）", text: $cloudPassword)
                HStack {
                    Button("登录") { authCloud(register: false) }
                    Button("注册") { authCloud(register: true) }
                    Button("导出…") { exportFile() }
                    Button("导入…") { importFile() }
                }
            }
            Text("登录密码就是加密密钥：数据在本机用它封成密文再上传，服务器看不到 Token。忘记密码后云端无法解密，只能靠本机「导出」的备份恢复。改密会先用旧密码解开，再用新密码重封后上传。")
                .font(.caption)
                .foregroundStyle(.secondary)
            Text(syncStatus.isEmpty ? " " : syncStatus)
                .font(.caption)
                .foregroundStyle(.secondary)
            Spacer()
            footer
        }
    }

    var footer: some View {
        HStack {
            Text(store.saveError.isEmpty ? hint : store.saveError)
                .foregroundStyle(store.saveError.isEmpty ? Color.secondary : Color.red)
                .font(.caption)
            Spacer()
            Button("取消") { SettingsWindowController.shared.close() }
            Button("应用") { save(close: false) }
            Button("保存") { save(close: true) }.keyboardShortcut(.defaultAction)
        }
    }

    var activeBinding: Binding<String> {
        Binding(
            get: { store.config.activeAccountId },
            set: { newId in
                persistAccountFields()
                store.switchAccount(newId)
                actualCnyText = formatDecimal(store.config.activeAccount?.actualCny ?? 0)
                channel = store.config.activeAccount?.channel ?? ""
            }
        )
    }

    var kindBinding: Binding<String> {
        Binding(
            get: { store.config.activeAccount?.accountKind ?? AccountValidity.longTerm },
            set: { setValidity(kind: $0, refresh: true) }
        )
    }

    var startBinding: Binding<Date> {
        Binding(
            get: { AccountSync.parseIso(store.config.activeAccount?.tempStartAt) ?? Date() },
            set: { setValidity(start: $0) }
        )
    }

    var daysBinding: Binding<Int> {
        Binding(
            get: { store.config.activeAccount?.tempValidDays ?? 0 },
            set: { setValidity(days: $0) }
        )
    }

    var hoursBinding: Binding<Int> {
        Binding(
            get: { store.config.activeAccount?.tempValidHours ?? 0 },
            set: { setValidity(hours: $0) }
        )
    }

    var endCaption: String {
        guard let acc = store.config.activeAccount,
              let end = AccountValidity.accountEndIso(acc)
        else { return "请设置有效时间（天和小时可组合）" }
        return "结束时间 \(StatusText.formatResetDate(end, includeTime: true))（按开始时间计算，覆盖管理端重置日）"
    }

    func setValidity(kind: String? = nil, start: Date? = nil, days: Int? = nil, hours: Int? = nil, refresh: Bool = false) {
        guard let acc = store.config.activeAccount else { return }
        var c = store.config
        let newKind = kind ?? acc.accountKind
        var newStart = acc.tempStartAt
        if let start { newStart = AccountSync.nowIso(start) }
        var newDays = days ?? acc.tempValidDays
        let newHours = hours ?? acc.tempValidHours
        if AccountValidity.sanitizeKind(newKind) == AccountValidity.temporary {
            if newStart.trimmingCharacters(in: .whitespaces).isEmpty {
                newStart = AccountSync.nowIso()
            }
            if newDays == 0 && newHours == 0 { newDays = 1 }
        }
        _ = c.updateAccountValidity(acc.id, kind: newKind, startAt: newStart, days: newDays, hours: newHours)
        store.applyConfig(c, refresh: refresh)
    }

    var notifyBinding: Binding<Bool> {
        Binding(
            get: { store.config.notifyEnabled },
            set: { v in applySetting { $0.notifyEnabled = v } }
        )
    }

    var exhaustBinding: Binding<Bool> {
        Binding(
            get: { store.config.notifyExhaustionRisk },
            set: { v in applySetting { $0.notifyExhaustionRisk = v } }
        )
    }

    var modeBinding: Binding<String> {
        Binding(
            get: { store.config.trayDisplayMode },
            set: { v in applySetting { $0.trayDisplayMode = v } }
        )
    }

    var autostartBinding: Binding<Bool> {
        Binding(
            get: { store.config.autostartEnabled },
            set: { v in var c = store.config; c.autostartEnabled = v; store.applyConfig(c, refresh: false) }
        )
    }

    var autoUpdateBinding: Binding<Bool> {
        Binding(
            get: { store.config.autoUpdateEnabled },
            set: { v in var c = store.config; c.autoUpdateEnabled = v; store.applyConfig(c, refresh: false) }
        )
    }

    func exportPassphrase() -> String {
        if !store.config.syncSecret.trimmingCharacters(in: .whitespaces).isEmpty {
            return store.config.syncSecret
        }
        return cloudPassword.trimmingCharacters(in: .whitespaces)
    }

    func addPastedAccounts() async {
        if importing { return }
        let items = CursorAccountPaste.parse(tokenText)
        if items.isEmpty {
            hint = "请粘贴 Token 或邮箱密码"
            return
        }
        importing = true
        defer { importing = false }
        var ok = 0
        var fail = 0
        var lastId: String?
        for item in items {
            if item.kind == "token" {
                do {
                    var cfg = store.config
                    let (acc, _) = try cfg.upsertAccount(token: item.token, activate: true)
                    store.applyConfig(cfg, refresh: true)
                    lastId = acc.id
                    ok += 1
                } catch {
                    fail += 1
                }
                continue
            }
            if item.kind == "credentials" {
                store.importStatus = "正在打开登录页…"
                guard let token = await PasswordLoginController.shared.run(email: item.email, password: item.password), !token.isEmpty else {
                    fail += 1
                    continue
                }
                do {
                    let snap = try await store.client.fetchUsageSummary(sessionToken: token)
                    var cfg = store.config
                    let (acc, _) = try cfg.upsertAccount(
                        token: token,
                        membershipType: snap.membershipType,
                        remaining: snap.remainingPercent,
                        email: item.email,
                        password: item.password,
                        activate: true
                    )
                    store.applyConfig(cfg, refresh: true)
                    lastId = acc.id
                    ok += 1
                } catch {
                    do {
                        var cfg = store.config
                        let (acc, _) = try cfg.upsertAccount(
                            token: token,
                            email: item.email,
                            password: item.password,
                            activate: true
                        )
                        store.applyConfig(cfg, refresh: true)
                        lastId = acc.id
                        ok += 1
                    } catch {
                        fail += 1
                    }
                }
                continue
            }
            fail += 1
        }
        if let lastId {
            store.switchAccount(lastId)
        }
        tokenText = ""
        let summary = fail == 0 ? "已添加 \(ok) 个账号" : "成功 \(ok) / 失败 \(fail)"
        hint = summary
        store.importStatus = summary
    }

    func rename() {
        guard let acc = store.config.activeAccount else { return }
        let alert = NSAlert()
        alert.messageText = "重命名账号"
        alert.informativeText = acc.displayLabel
        let field = NSTextField(string: acc.label)
        field.frame = NSRect(x: 0, y: 0, width: 240, height: 24)
        alert.accessoryView = field
        alert.addButton(withTitle: "确定")
        alert.addButton(withTitle: "取消")
        if alert.runModal() == .alertFirstButtonReturn {
            var cfg = store.config
            _ = cfg.renameAccount(acc.id, label: field.stringValue)
            store.applyConfig(cfg, refresh: false)
        }
    }

    func deleteAccount() {
        guard let acc = store.config.activeAccount else { return }
        let alert = NSAlert()
        alert.messageText = "删除账号"
        alert.informativeText = "确定删除「\(acc.displayLabel)」？"
        alert.addButton(withTitle: "删除")
        alert.addButton(withTitle: "取消")
        if alert.runModal() == .alertFirstButtonReturn {
            var cfg = store.config
            _ = cfg.removeAccount(acc.id)
            store.applyConfig(cfg, refresh: true)
        }
    }

    func loginToCursor() async {
        importing = true
        store.importStatus = "正在写入 Cursor…"
        let result = await store.loginToCursor(confirmClose: { running in
            if !running { return true }
            let alert = NSAlert()
            alert.messageText = "登录到 Cursor"
            alert.informativeText = CursorAuth.confirmCloseMessage
            alert.addButton(withTitle: "关闭并写入")
            alert.addButton(withTitle: "取消")
            return alert.runModal() == .alertFirstButtonReturn
        })
        importing = false
        store.importStatus = result.message
        hint = result.ok ? "已写入 Cursor" : result.message
    }

    func applySetting(_ update: (inout AppConfig) -> Void) {
        var c = store.config
        let before = AccountSync.snapshotSettings(c)
        update(&c)
        AccountSync.touchChangedSettings(&c, previous: before)
        store.applyConfig(c, refresh: false)
    }

    func save(close: Bool) {
        var cfg = store.config
        let before = AccountSync.snapshotSettings(cfg)
        if let n = Int(intervalText.trimmingCharacters(in: .whitespaces)), n >= 1 {
            cfg.refreshIntervalMinutes = n
        }
        if let plan = parseDecimal(planUsdText) {
            cfg.monthlyPlanUsd = UsageEvents.clampMonthlyPlanUsd(plan)
        }
        if let actual = parseDecimal(actualCnyText), let acc = cfg.activeAccount {
            _ = cfg.setActualCny(acc.id, actual)
            _ = cfg.setChannel(acc.id, channel)
        }
        if let rate = parseDecimal(cnyRateText) {
            cfg.usdCnyRate = UsageEvents.clampUsdCnyRate(rate)
        }
        cfg.alertThresholds = ConfigStore.parseThresholds(thresholdText)
        var added = 0
        var failed = 0
        for token in CursorAccountPaste.tokenValues(tokenText) {
            do {
                _ = try cfg.upsertAccount(token: token, activate: true)
                added += 1
            } catch {
                failed += 1
            }
        }
        if added > 0 { tokenText = "" }
        AccountSync.touchChangedSettings(&cfg, previous: before)
        store.applyConfig(cfg, refresh: true)
        let saved = StatusText.formatTokenSaveResult(ok: added, fail: failed)
        if close {
            hint = saved
            SettingsWindowController.shared.close()
        } else {
            hint = saved.isEmpty ? "已应用" : saved
        }
    }

    func reloadFields() {
        tokenText = ""
        intervalText = String(store.config.refreshIntervalMinutes)
        let membership = store.config.activeAccount?.membershipType ?? ""
        let plan = store.config.monthlyPlanUsd > 0
            ? store.config.monthlyPlanUsd
            : UsageEvents.defaultMonthlyPlanUsd(membership)
        planUsdText = formatDecimal(plan)
        actualCnyText = formatDecimal(store.config.activeAccount?.actualCny ?? 0)
        channel = store.config.activeAccount?.channel ?? ""
        cnyRateText = formatDecimal(store.config.usdCnyRate)
        thresholdText = store.config.alertThresholds.map(String.init).joined(separator: ",")
        cloudEmail = store.config.cloudEmail
        cloudPassword = ""
        syncStatus = {
            let decrypt = StatusText.formatCloudDecryptNote(
                decryptError: store.config.decryptError,
                syncSecretFailed: store.config.syncSecretDecryptFailed,
                cloudAccessFailed: store.config.cloudAccessDecryptFailed
            )
            let text = StatusText.formatSyncStatus(lastAt: store.config.syncLastAt, lastError: store.config.syncLastError)
            if !decrypt.isEmpty && !text.isEmpty { return decrypt + " " + text }
            if !decrypt.isEmpty { return decrypt }
            if !text.isEmpty { return text }
            return store.config.cloudLoggedIn ? "尚未同步" : ""
        }()
    }

    func persistAccountFields() {
        guard let acc = store.config.activeAccount else { return }
        var cfg = store.config
        if let actual = parseDecimal(actualCnyText) {
            _ = cfg.setActualCny(acc.id, actual)
        }
        _ = cfg.setChannel(acc.id, channel)
        store.applyConfig(cfg, refresh: false)
    }

    func authCloud(register: Bool) {
        let email = cloudEmail.trimmingCharacters(in: .whitespaces)
        let password = cloudPassword.trimmingCharacters(in: .whitespaces)
        if email.isEmpty || password.isEmpty {
            syncStatus = "请填写邮箱和密码"
            return
        }
        syncStatus = register ? "正在注册…" : "正在登录…"
        var cfg = store.config
        DispatchQueue.global(qos: .userInitiated).async {
            do {
                let result = register
                    ? try CloudSync.register(email: email, password: password)
                    : try CloudSync.login(email: email, password: password)
                CloudSync.applySession(&cfg, email: result.email.isEmpty ? email : result.email, password: password, access: result.access, refresh: result.refresh)
                DispatchQueue.main.async { syncStatus = "正在从云端导入账号和用量…" }
                let status = AccountSync.reconcile(&cfg)
                DispatchQueue.main.async {
                    store.applyConfig(cfg, refresh: status.changed)
                    syncStatus = status.message
                    hint = status.message
                    cloudPassword = ""
                }
            } catch {
                DispatchQueue.main.async {
                    syncStatus = (error as? CursorAPIError)?.message ?? error.localizedDescription
                }
            }
        }
    }

    func changePassword() {
        let old = oldCloudPassword.trimmingCharacters(in: .whitespaces)
        let next = newCloudPassword.trimmingCharacters(in: .whitespaces)
        let confirm = confirmCloudPassword.trimmingCharacters(in: .whitespaces)
        if next.count < 8 {
            syncStatus = "密码至少 8 位"
            return
        }
        if next != confirm {
            syncStatus = "两次输入的新密码不一致"
            return
        }
        syncStatus = "正在修改密码…"
        var cfg = store.config
        DispatchQueue.global(qos: .userInitiated).async {
            do {
                try CloudSync.changePassword(&cfg, oldPassword: old, newPassword: next)
                DispatchQueue.main.async {
                    store.applyConfig(cfg, refresh: false)
                    oldCloudPassword = ""
                    newCloudPassword = ""
                    confirmCloudPassword = ""
                    showChangePassword = false
                    syncStatus = "密码已更新，云端数据已用新密码重封。请在其他设备用新密码重新登录云同步。"
                }
            } catch {
                DispatchQueue.main.async {
                    syncStatus = (error as? CursorAPIError)?.message ?? error.localizedDescription
                }
            }
        }
    }

    func deleteCloudAccount() {
        let password = deletePassword.trimmingCharacters(in: .whitespaces)
        if password.isEmpty {
            syncStatus = "请输入密码以确认注销"
            return
        }
        syncStatus = "正在注销…"
        var cfg = store.config
        DispatchQueue.global(qos: .userInitiated).async {
            do {
                try CloudSync.deleteAccount(&cfg, password: password)
                DispatchQueue.main.async {
                    store.applyConfig(cfg, refresh: false)
                    deletePassword = ""
                    showDeleteConfirm = false
                    syncStatus = "云端账号已删除"
                }
            } catch {
                DispatchQueue.main.async {
                    syncStatus = (error as? CursorAPIError)?.message ?? error.localizedDescription
                }
            }
        }
    }

    func logoutCloud() {
        var cfg = store.config
        DispatchQueue.global(qos: .userInitiated).async {
            CloudSync.logout(&cfg)
            DispatchQueue.main.async {
                store.applyConfig(cfg, refresh: false)
                syncStatus = "已退出登录"
            }
        }
    }

    func syncNow() {
        syncStatus = "正在同步…"
        var cfg = store.config
        DispatchQueue.global(qos: .userInitiated).async {
            let status = AccountSync.reconcile(&cfg)
            DispatchQueue.main.async {
                store.applyConfig(cfg, refresh: status.changed)
                syncStatus = status.message
                hint = status.message
            }
        }
    }

    func exportFile() {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = AccountSync.filename
        panel.title = "导出加密账号包"
        var types: [UTType] = [.json]
        if let sync = UTType(filenameExtension: "sync") { types.insert(sync, at: 0) }
        panel.allowedContentTypes = types
        if panel.runModal() != .OK { return }
        guard let url = panel.url else { return }
        var cfg = store.config
        do {
            let dest = try AccountSync.exportToFile(&cfg, path: url.path, passphrase: exportPassphrase())
            store.applyConfig(cfg, refresh: false)
            syncStatus = "已导出到 " + dest
        } catch {
            syncStatus = (error as? CursorAPIError)?.message ?? error.localizedDescription
        }
    }

    func importFile() {
        let panel = NSOpenPanel()
        panel.canChooseFiles = true
        panel.canChooseDirectories = false
        panel.allowsMultipleSelection = false
        panel.title = "导入加密账号包"
        if panel.runModal() != .OK { return }
        guard let url = panel.url else { return }
        var cfg = store.config
        do {
            try AccountSync.importFromFile(&cfg, path: url.path, passphrase: exportPassphrase())
            store.applyConfig(cfg, refresh: true)
            syncStatus = "已从文件合并账号"
            hint = "已导入"
        } catch {
            syncStatus = (error as? CursorAPIError)?.message ?? error.localizedDescription
        }
    }

    func importFrom(prefer: String?) async {
        importing = true
        store.importStatus = "正在导入…"
        let result = await SessionImporter.importAndValidate(
            preferBrowsers: SessionImporter.defaultPreferBrowsers(prefer),
            onlyBrowsers: SessionImporter.onlyBrowsers(for: prefer),
            skipTokens: store.config.existingTokenVariants()
        )
        await MainActor.run {
            importing = false
            store.importStatus = result.message
            if result.ok {
                var cfg = store.config
                _ = try? cfg.upsertAccount(
                    token: result.token,
                    membershipType: result.membershipType,
                    remaining: result.remainingPercent,
                    activate: true
                )
                store.applyConfig(cfg, refresh: true)
                tokenText = ""
                hint = "已导入"
            }
        }
    }

    func loginAndImport(prefer: String) async {
        if importing { return }
        importing = true
        defer { importing = false }
        let apps = SessionImporter.preferredMacAppNames(prefer)
        if let app = apps.first {
            let url = URL(string: "https://cursor.com/dashboard")!
            let config = NSWorkspace.OpenConfiguration()
            if let appURL = applicationURL(named: app) {
                _ = try? await NSWorkspace.shared.open([url], withApplicationAt: appURL, configuration: config)
            } else {
                NSWorkspace.shared.open(url)
            }
        }
        store.importStatus = "请在浏览器登录，正在等待 Cookie…"
        let deadline = Date().addingTimeInterval(180)
        while Date() < deadline {
            let result = await SessionImporter.importAndValidate(
                preferBrowsers: SessionImporter.defaultPreferBrowsers(prefer),
                onlyBrowsers: SessionImporter.onlyBrowsers(for: prefer),
                skipTokens: store.config.existingTokenVariants()
            )
            if result.ok {
                await MainActor.run {
                    var cfg = store.config
                    _ = try? cfg.upsertAccount(token: result.token, membershipType: result.membershipType, remaining: result.remainingPercent, activate: true)
                    store.applyConfig(cfg, refresh: true)
                    tokenText = ""
                    store.importStatus = result.message
                    hint = "已导入"
                }
                return
            }
            try? await Task.sleep(nanoseconds: 2_000_000_000)
        }
        await MainActor.run { store.importStatus = "等待登录超时，请手动粘贴 Token。" }
    }

    func formatDecimal(_ value: Double) -> String {
        let f = NumberFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.minimumFractionDigits = 0
        f.maximumFractionDigits = 4
        f.numberStyle = .decimal
        return f.string(from: NSNumber(value: value)) ?? String(value)
    }

    func parseDecimal(_ text: String) -> Double? {
        let cleaned = text.trimmingCharacters(in: .whitespaces).replacingOccurrences(of: "，", with: ".")
        return Double(cleaned)
    }

    func bundleId(for app: String) -> String {
        switch app {
        case "Safari": return "com.apple.Safari"
        case "Firefox": return "org.mozilla.firefox"
        default: return ""
        }
    }

    func applicationURL(named app: String) -> URL? {
        let id = bundleId(for: app)
        if !id.isEmpty, let url = NSWorkspace.shared.urlForApplication(withBundleIdentifier: id) {
            return url
        }
        let candidates = [
            "/Applications/\(app).app",
            "/System/Cryptexes/App/System/Applications/\(app).app",
            "/System/Applications/\(app).app",
            NSHomeDirectory() + "/Applications/\(app).app",
        ]
        return candidates.map { URL(fileURLWithPath: $0) }.first { FileManager.default.fileExists(atPath: $0.path) }
    }
}

@MainActor
final class SettingsWindowController: NSObject, NSWindowDelegate {
    static let shared = SettingsWindowController()
    private var window: NSWindow?
    private weak var store: AppStore?

    func show(store: AppStore, focusToken: Bool, startImport: Bool) {
        self.store = store
        MenubarActivation.promoteForWindow()
        AppDelegate.ensureStatusItemVisible()
        if window == nil {
            let win = NSWindow(
                contentRect: NSRect(x: 0, y: 0, width: 540, height: 680),
                styleMask: [.titled, .closable, .miniaturizable],
                backing: .buffered,
                defer: false
            )
            win.title = AppPaths.settingsTitle
            win.isReleasedWhenClosed = false
            win.delegate = self
            window = win
        }
        let wasHidden = window?.isVisible != true
        if window?.contentView == nil {
            window?.contentView = NSHostingView(rootView: SettingsRootView(store: store, startImport: startImport, focusToken: focusToken))
            window?.center()
        } else if wasHidden {
            store.settingsReloadTick += 1
        }
        window?.makeKeyAndOrderFront(nil)
        if focusToken {
            DispatchQueue.main.async {
                store.focusToken = true
            }
        }
    }

    func close() {
        dismiss()
    }

    func windowShouldClose(_ sender: NSWindow) -> Bool {
        dismiss()
        return false
    }

    func windowWillClose(_ notification: Notification) {
        store?.settingsVisible = false
        MenubarActivation.restoreAfterClosing(notification.object as? NSWindow)
    }

    private func dismiss() {
        window?.orderOut(nil)
        store?.settingsVisible = false
        MenubarActivation.restoreNow(excluding: window)
    }
}
