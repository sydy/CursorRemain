import AppKit
import CursorTokenCore
import Security
import SwiftUI
import UniformTypeIdentifiers

struct SettingsRootView: View {
    @ObservedObject var store: AppStore
    @State private var addOpen = false
    @State private var tab = "account"
    @State private var importing = false
    @State private var tokenText = ""
    @State private var loginProfileId = ""
    @State private var intervalText = "10"
    @State private var planUsdText = "0"
    @State private var actualCnyText = "0"
    @State private var channel = ""
    @State private var cnyRateText = "7.5"
    @State private var thresholdText = "50,20,5"
    @State private var cloudEmail = ""
    @State private var cloudPassword = ""
    @State private var cloudDataPassphrase = ""
    @State private var cloudDataPassphraseConfirm = ""
    @State private var generatedDataPassphrase = ""
    @State private var verificationCode = ""
    @State private var showVerification = false
    @State private var resetCode = ""
    @State private var resetNewPassword = ""
    @State private var showPasswordReset = false
    @State private var syncStatus = ""
    @State private var showChangePassword = false
    @State private var showChangeDataPassphrase = false
    @State private var newDataPassphrase = ""
    @State private var confirmDataPassphrase = ""
    @State private var oldCloudPassword = ""
    @State private var newCloudPassword = ""
    @State private var confirmCloudPassword = ""
    @State private var deletePassword = ""
    @State private var showDeleteConfirm = false
    @State private var hint = ""
    @FocusState private var tokenFocused: Bool
    @State private var fittedBody: CGFloat = 0
    var startImport: Bool = false
    var focusToken: Bool = false

    var body: some View {
        VStack(spacing: 12) {
            SettingsTabBar(selection: $tab)
            Group {
                switch tab {
                case "notify": notifyPage
                case "menu": menuPage
                case "sync": syncPage
                default: accountPage
                }
            }
        }
        .padding(SettingsMetrics.padding)
        .frame(width: SettingsMetrics.width, height: pageHeight)
        .preferredColorScheme(ColorModeAppearance.colorScheme(store.config.colorMode))
        .onPreferenceChange(SettingsBodyHeightKey.self) { height in
            guard height > fittedBody + 1 else { return }
            fittedBody = height
            fitWindow()
        }
        .onAppear {
            reloadFields()
            if focusToken || store.focusToken {
                addOpen = true
                tokenFocused = true
            }
            if startImport || store.pendingCursorImport {
                addOpen = true
                store.pendingCursorImport = false
                Task { await importFrom(prefer: "cursor-app") }
            }
            fitWindow()
        }
        .onChange(of: store.settingsReloadTick) { _ in
            reloadFields()
        }
        .onChange(of: store.focusToken) { focused in
            if focused {
                addOpen = true
                tokenFocused = true
            }
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

    var isTemporaryAccount: Bool {
        AccountValidity.isTemporary(store.config.activeAccount)
    }

    var modeLabel: String {
        switch store.config.trayDisplayMode {
        case "number": return "纯数字"
        case "dot": return "仅色点"
        default: return "圆环百分比"
        }
    }

    func fitWindow() {
        SettingsWindowController.shared.resizeTo(height: pageHeight)
    }

    func fieldCaption(_ title: String) -> some View {
        Text(title)
            .font(.subheadline)
            .foregroundStyle(.secondary)
    }

    func fieldWidth<V: View>(_ view: V, max: CGFloat = 360) -> some View {
        SettingsFieldBox(width: max) { view }
    }

    /// Menu + buttons: hug the longest title. A Picker/MenuPickerStyle inside
    /// ScrollView inherits the field width, leaving a trailing gutter and `>`.
    func settingsMenuPicker<Selection: Hashable>(
        selection: Binding<Selection>,
        current: String,
        options: [(Selection, String)]
    ) -> some View {
        Menu {
            ForEach(options, id: \.0) { value, title in
                Button {
                    selection.wrappedValue = value
                } label: {
                    if value == selection.wrappedValue {
                        Label(title, systemImage: "checkmark")
                    } else {
                        Text(title)
                    }
                }
            }
        } label: {
            SettingsFieldBox {
                HStack(spacing: 6) {
                    Text(current)
                        .lineLimit(1)
                    Image(systemName: "chevron.down")
                        .font(.caption.weight(.semibold))
                        .foregroundStyle(.secondary)
                }
            }
        }
        .menuIndicator(.hidden)
        .buttonStyle(.plain)
        .fixedSize(horizontal: true, vertical: false)
    }

    /// Single field + popover calendar (B / B3). Avoids the system DatePicker
    /// that expands a calendar inline inside ScrollView.
    func settingsDateTimeField(selection: Binding<Date>) -> some View {
        SettingsDateTimeField(selection: selection)
    }

    func settingsDurationField(days: Binding<Int>, hours: Binding<Int>) -> some View {
        SettingsDurationField(days: days, hours: hours)
    }

    func settingsPage<V: View>(spacing: CGFloat, @ViewBuilder content: () -> V) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            ScrollView {
                VStack(alignment: .leading, spacing: spacing) {
                    content()
                }
                .frame(maxWidth: .infinity, alignment: .topLeading)
                .fixedSize(horizontal: false, vertical: true)
                .background(
                    GeometryReader { geo in
                        Color.clear.preference(key: SettingsBodyHeightKey.self, value: geo.size.height)
                    }
                )
            }
            footer
                .padding(.top, SettingsMetrics.footerGap)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }

    var accountPage: some View {
        settingsPage(spacing: 10) {
            fieldCaption("账号")
            accountTable
            HStack {
                Button("添加账号") { addOpen = true }
                    .buttonStyle(.borderedProminent)
                Button("检查状态") { store.requestRefresh() }
                    .disabled(store.config.accounts.isEmpty || !store.accountCheckStatus.isEmpty)
                Button("让 Cursor 使用此账号") { Task { await loginToCursor() } }
                    .disabled(importing || store.config.activeAccount == nil)
                Button("重命名") { rename() }
                    .disabled(store.config.activeAccount == nil)
                Button("删除") { deleteAccount() }
                    .disabled(store.config.activeAccount == nil)
            }
            if !store.accountCheckStatus.isEmpty {
                Text(store.accountCheckStatus)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            let activeLabel = store.config.activeAccount?.displayLabel ?? ""
            Text(activeLabel.isEmpty ? "当前账号配置" : "当前账号配置（\(activeLabel)）")
                .font(.headline)
                .padding(.top, 6)
            fieldCaption("账号类型")
            settingsMenuPicker(
                selection: kindBinding,
                current: isTemporaryAccount ? "临时账号" : "长期账号",
                options: [
                    (AccountValidity.longTerm, "长期账号"),
                    (AccountValidity.temporary, "临时账号"),
                ]
            )
            .disabled(store.config.activeAccount == nil)
            if isTemporaryAccount {
                fieldCaption("开始时间")
                settingsDateTimeField(selection: startBinding)
                fieldCaption("有效时间")
                settingsDurationField(days: daysBinding, hours: hoursBinding)
                Text(endCaption)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            fieldCaption("渠道")
            settingsMenuPicker(
                selection: $channel,
                current: UsageEvents.channelLabel(channel),
                options: [
                    ("", "未标"),
                    (UsageEvents.channelSelfPay, "自费"),
                    (UsageEvents.channelThirdParty, "第三方"),
                ]
            )
            .disabled(store.config.activeAccount == nil)
            fieldCaption("实际成本（人民币）")
            fieldWidth(TextField("0", text: $actualCnyText).textFieldStyle(.plain), max: 160)
                .disabled(store.config.activeAccount == nil)
            Text("仅当前账号。短期号请买价÷天数×30。企业 / 团队额度不是真实支出；填了实际成本则按该成本分摊（含按需），优先于月费，按需不再按官网标价另加。")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
        .sheet(isPresented: $addOpen) {
            addAccountSheet
        }
    }

    var addAccountSheet: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("添加账号").font(.headline)
            Button("登录新账号") { loginNewAccount() }
                .buttonStyle(.borderedProminent)
                .disabled(importing)
            Text("在这个账号单独的窗口里登录，完成后自动保存，不会影响其他账号。")
                .font(.caption)
                .foregroundStyle(.secondary)
            Text("粘贴已有 Token").font(.headline).padding(.top, 4)
            TextEditor(text: $tokenText)
                .font(.system(.body, design: .monospaced))
                .frame(minHeight: 88, maxHeight: 120)
                .overlay(RoundedRectangle(cornerRadius: 6).stroke(Color.secondary.opacity(0.3)))
                .focused($tokenFocused)
            HStack {
                Button("添加") { Task { await addPastedAccounts() } }
                    .disabled(importing)
                Button("从 Cursor 导入") { Task { await importFrom(prefer: "cursor-app") } }
                    .disabled(importing)
                Spacer()
                Button("完成") { addOpen = false }
            }
            Text("每行一个 Token。")
                .font(.caption)
                .foregroundStyle(.secondary)
            Text(store.importStatus.isEmpty ? "已在 Cursor 里登录时，点「从 Cursor 导入」。" : store.importStatus)
                .font(.caption)
                .foregroundStyle(.secondary)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(20)
        .frame(width: 440)
    }

    var notifyPage: some View {
        settingsPage(spacing: 14) {
            Text("刷新与通知").font(.title3.bold())
            fieldCaption("刷新间隔（分钟）")
            fieldWidth(TextField("10", text: $intervalText).textFieldStyle(.plain), max: 96)
            fieldCaption("月费（美元）")
            fieldWidth(TextField("20", text: $planUsdText).textFieldStyle(.plain), max: 96)
            fieldCaption("美元兑人民币")
            fieldWidth(TextField("7.5", text: $cnyRateText).textFieldStyle(.plain), max: 96)
            Text("月费填 0 则按套餐预填：Plus $8 / Premium $30 / Super $100 / Ultra $200，旧套餐 Pro $20 / Pro+ $60。年付请填折合月费。实际成本在「账户」里按账号填写。")
                .font(.caption)
                .foregroundStyle(.secondary)
            fieldCaption("告警阈值，例如 50,20,5")
            fieldWidth(TextField("50,20,5", text: $thresholdText).textFieldStyle(.plain), max: 160)
            Toggle("启用用量通知", isOn: notifyBinding)
            Toggle("启用耗尽风险通知", isOn: exhaustBinding)
        }
    }

    var menuPage: some View {
        settingsPage(spacing: 14) {
            Text("菜单栏与启动").font(.title3.bold())
            fieldCaption("颜色")
            AppearanceSegment(selection: colorBinding)
            fieldCaption("菜单栏图标")
            settingsMenuPicker(
                selection: modeBinding,
                current: modeLabel,
                options: [
                    ("ring", "圆环百分比"),
                    ("number", "纯数字"),
                    ("dot", "仅色点"),
                ]
            )
            Toggle("开机自启（下次登录生效）", isOn: autostartBinding)
            Toggle("自动检查并安装更新", isOn: autoUpdateBinding)
            Text("当前版本  \(AppUpdate.displayVersion())")
                .font(.caption)
                .foregroundStyle(.secondary)
            Button("检查更新") {
                Task { await store.checkForUpdate(manual: true) }
            }
            .disabled(store.updateBusy)
            Text(store.updateStatus.isEmpty ? "对照 GitHub 正式版（v*）。打包版会下载替换后重启；开发运行则打开下载页。更新后若再要钥匙串密码，允许一次即可：密钥会写到本机配置目录，之后不再弹。" : store.updateStatus)
                .font(.caption)
                .foregroundStyle(.secondary)
        }
    }

    var syncPage: some View {
        settingsPage(spacing: 12) {
            Text("云同步").font(.title3.bold())
            Picker("同步方式", selection: storageBinding) {
                Text("服务器保存明细").tag("server")
                Text("本机加密信封").tag("envelope")
            }
            .pickerStyle(.menu)
            if store.config.cloudLoggedIn {
                Text("已登录  \(store.config.cloudEmail)")
                HStack {
                    Button("立即同步") { syncNow() }
                    Button("退出登录") { logoutCloud() }
                    Button("修改密码") { showChangePassword.toggle() }
                    if store.config.syncStorage == "envelope" {
                        Button("更改数据加密口令") { showChangeDataPassphrase.toggle() }
                    }
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
                if showChangeDataPassphrase {
                    SecureField("新数据加密口令（至少 16 位）", text: $newDataPassphrase)
                    SecureField("确认新口令", text: $confirmDataPassphrase)
                    Button("重封云端数据") { changeDataPassphrase() }
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
                SecureField("登录密码（至少 8 位）", text: $cloudPassword)
                if store.config.syncStorage == "envelope" {
                    SecureField("数据加密口令（至少 16 位随机短语）", text: $cloudDataPassphrase)
                    SecureField("确认数据加密口令", text: $cloudDataPassphraseConfirm)
                }
                HStack {
                    Button("登录") { authCloud(register: false) }
                    Button("注册") { authCloud(register: true) }
                    if store.config.syncStorage == "envelope" {
                        Button("生成随机加密口令") { generateDataPassphrase() }
                    }
                    Button("忘记密码") { requestPasswordReset() }
                    Button("导出…") { exportFile() }
                    Button("导入…") { importFile() }
                }
                if !generatedDataPassphrase.isEmpty {
                    Text("请复制并保存到密码管理器；其他设备需要此口令：\n\(generatedDataPassphrase)")
                        .font(.caption.monospaced())
                        .textSelection(.enabled)
                }
                if showVerification {
                    TextField("邮件中的 6 位验证码", text: $verificationCode)
                    Button("验证邮箱并登录") { verifyCloudEmail() }
                    Button("重发验证邮件") { resendVerificationEmail() }
                }
                if showPasswordReset {
                    TextField("邮件中的 6 位重置码", text: $resetCode)
                    SecureField("新登录密码", text: $resetNewPassword)
                    Button("重置登录密码") { confirmPasswordReset() }
                }
            }
            Text(store.config.syncStorage == "envelope"
                ? "登录密码只用于账户认证；同步数据由独立口令在本机加密，服务器只保存密文。登录密码可通过验证邮箱重置；数据加密口令至少 16 位并需自行保存，遗失后无法解密云端数据。请定期导出加密备份。"
                : "服务器直接保存账号、设置和用量明细，登录密码重置后数据仍在。Token 和账号密码对同步服务器可见。选择「本机加密信封」则改回原来的端到端加密。两台设备要用同一种方式。")
                .font(.caption)
                .foregroundStyle(.secondary)
            Text(syncStatus.isEmpty ? " " : syncStatus)
                .font(.caption)
                .foregroundStyle(.secondary)
        }
    }

    var pageHeight: CGFloat {
        let raw = fittedBody + SettingsMetrics.padding * 2 + SettingsMetrics.footerReserve + SettingsMetrics.tabBarReserve
        let cap = (NSScreen.main?.visibleFrame.height ?? 800) - 60
        if fittedBody < 1 { return min(SettingsMetrics.width, cap) }
        return min(max(raw, SettingsMetrics.minHeight), cap)
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

    var accountTable: some View {
        Group {
            if store.config.accounts.isEmpty {
                Text("还没有账号")
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity, minHeight: 72, alignment: .leading)
            } else {
                Table(store.config.accounts, selection: accountSelection) {
                    TableColumn("账号") { account in
                        Text(account.displayLabel)
                            .fontWeight(account.id == store.config.activeAccountId ? .semibold : .regular)
                            .lineLimit(1)
                    }
                    .width(min: 96, ideal: 148)
                    TableColumn("套餐") { account in
                        Text(StatusText.formatAccountPlan(account)).lineLimit(1)
                    }
                    .width(min: 52, ideal: 72)
                    TableColumn("状态") { account in
                        Text(StatusText.formatAccountCheckStatus(account)).lineLimit(1)
                    }
                    .width(min: 72, ideal: 88)
                    TableColumn("更新时间") { account in
                        Text(StatusText.formatAccountCheckTime(account)).lineLimit(1)
                    }
                    .width(min: 88, ideal: 108)
                    TableColumn("类型") { account in
                        Text(StatusText.formatAccountKind(account)).lineLimit(1)
                    }
                    .width(min: 44, ideal: 52)
                }
                .frame(height: accountTableHeight)
            }
        }
        .accessibilityLabel("当前账号")
    }

    var accountTableHeight: CGFloat {
        let rows = CGFloat(max(store.config.accounts.count, 1))
        return min(32 + rows * 28, 32 + 5 * 28)
    }

    var accountSelection: Binding<Set<Account.ID>> {
        Binding(
            get: {
                let id = store.config.activeAccountId
                return id.isEmpty ? [] : [id]
            },
            set: { ids in
                guard let id = ids.first, id != store.config.activeAccountId else { return }
                guard store.config.accounts.contains(where: { $0.id == id }) else { return }
                activeBinding.wrappedValue = id
            }
        )
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

    var colorBinding: Binding<String> {
        Binding(
            get: { store.config.colorMode },
            set: { v in applySetting { $0.colorMode = AppConfig.normalizeColorMode(v) } }
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
        return cloudDataPassphrase.trimmingCharacters(in: .whitespaces)
    }

    func addPastedAccounts() async {
        if importing { return }
        let items = CursorAccountPaste.parse(tokenText)
        let tokens = items.filter { $0.kind == "token" }
        if tokens.isEmpty {
            hint = "这里只接收 Token。要登录，请点「登录新账号」。"
            return
        }
        importing = true
        defer { importing = false }
        var ok = 0
        var fail = 0
        var lastId: String?
        for item in tokens {
            do {
                var cfg = store.config
                let (acc, _) = try cfg.upsertAccount(token: item.token, activate: true)
                store.applyConfig(cfg, refresh: true)
                lastId = acc.id
                ok += 1
            } catch {
                fail += 1
            }
        }
        if let lastId {
            store.switchAccount(lastId)
        }
        tokenText = ""
        let summary = fail == 0 ? "已添加 \(ok) 个账号" : "成功 \(ok) / 失败 \(fail)"
        hint = summary
        store.importStatus = summary
    }

    func loginNewAccount() {
        if importing { return }
        var bytes = [UInt8](repeating: 0, count: 32)
        if SecRandomCopyBytes(kSecRandomDefault, bytes.count, &bytes) != errSecSuccess {
            store.importStatus = "无法打开登录窗口"
            return
        }
        let verifier = Data(bytes).base64EncodedString()
            .replacingOccurrences(of: "+", with: "-")
            .replacingOccurrences(of: "/", with: "_")
            .replacingOccurrences(of: "=", with: "")
        let uuid = UUID().uuidString.lowercased()
        let url = CursorPasswordLogin.loginDeepURL(uuid: uuid, verifier: verifier)
        if url.isEmpty {
            store.importStatus = "无法打开登录窗口"
            return
        }
        if loginProfileId.isEmpty { loginProfileId = UUID().uuidString.lowercased() }
        let profileId = loginProfileId
        Task { await openIsolatedLogin(url, profileId: profileId, pollUUID: uuid, pollVerifier: verifier) }
    }

    func openIsolatedLogin(_ url: String, profileId: String, pollUUID: String, pollVerifier: String) async {
        if importing { return }
        importing = true
        store.importStatus = "请在弹出的窗口里完成登录"
        let token = await PasswordLoginController.shared.run(
            email: "",
            password: "",
            profileId: profileId,
            startURL: url,
            pollUUID: pollUUID,
            pollVerifier: pollVerifier
        )
        importing = false
        guard let token, !token.isEmpty else {
            store.importStatus = "登录未完成"
            return
        }
        LoginProfileStore.bind(email: "", token: token, profileId: profileId)
        loginProfileId = ""
        _ = await storeImportedToken(token)
    }

    func storeImportedToken(_ token: String) async {
        if importing { return }
        importing = true
        defer { importing = false }
        do {
            let snap = try await store.client.fetchUsageSummary(sessionToken: token)
            var cfg = store.config
            let (acc, _) = try cfg.upsertAccount(
                token: token,
                membershipType: snap.membershipType,
                remaining: snap.remainingPercent,
                activate: true
            )
            store.applyConfig(cfg, refresh: true)
            store.switchAccount(acc.id)
            store.importStatus = "已添加账号"
        } catch {
            do {
                var cfg = store.config
                let (acc, _) = try cfg.upsertAccount(token: token, activate: true)
                store.applyConfig(cfg, refresh: true)
                store.switchAccount(acc.id)
                store.importStatus = "已添加账号"
            } catch {
                store.importStatus = "登录成功，但没有写入账号"
            }
        }
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
            alert.messageText = "让 Cursor 使用此账号"
            alert.informativeText = CursorAuth.confirmCloseMessage
            alert.addButton(withTitle: "关闭并写入")
            alert.addButton(withTitle: "取消")
            return alert.runModal() == .alertFirstButtonReturn
        })
        importing = false
        store.importStatus = result.message
        hint = result.ok ? "已写入 Cursor" : result.message
    }

    var storageBinding: Binding<String> {
        Binding(
            get: { store.config.syncStorage == "envelope" ? "envelope" : "server" },
            set: { mode in
                var cfg = store.config
                let previous = cfg.syncStorage
                cfg.syncStorage = mode
                store.applyConfig(cfg, refresh: false)
                let startedActive = cfg.activeAccountId
                DispatchQueue.global(qos: .userInitiated).async {
                    var live = cfg
                    do {
                        try CloudSync.setStorage(&live, mode: mode)
                        let status = live.cloudLoggedIn ? AccountSync.reconcile(&live) : nil
                        DispatchQueue.main.async {
                            if let status {
                                applySyncedConfig(&live, startedActive: startedActive, refresh: status.changed)
                                syncStatus = status.message
                            } else {
                                store.applyConfig(live, refresh: false)
                            }
                        }
                    } catch {
                        DispatchQueue.main.async {
                            var rolled = store.config
                            rolled.syncStorage = previous
                            store.applyConfig(rolled, refresh: false)
                            syncStatus = (error as? CursorAPIError)?.message ?? error.localizedDescription
                        }
                    }
                }
            }
        )
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

    func applySyncedConfig(_ cfg: inout AppConfig, startedActive: String, refresh: Bool) {
        let kept = AccountSync.keepLiveActiveAccount(
            &cfg,
            startedActive: startedActive,
            liveActive: store.config.activeAccountId
        )
        store.applyConfig(cfg, refresh: refresh || kept)
    }

    func authCloud(register: Bool) {
        let email = cloudEmail.trimmingCharacters(in: .whitespaces)
        let password = cloudPassword.trimmingCharacters(in: .whitespaces)
        let dataPassphrase = cloudDataPassphrase.trimmingCharacters(in: .whitespaces)
        let confirmDataPassphrase = cloudDataPassphraseConfirm.trimmingCharacters(in: .whitespaces)
        if email.isEmpty || password.isEmpty {
            syncStatus = "请填写邮箱和密码"
            return
        }
        syncStatus = register ? "正在注册…" : "正在登录…"
        var cfg = store.config
        let startedActive = cfg.activeAccountId
        DispatchQueue.global(qos: .userInitiated).async {
            do {
                let serverMode = cfg.syncStorage != "envelope"
                if register {
                    if !serverMode {
                        guard dataPassphrase.count >= AccountSync.minimumDataPassphraseLength else {
                            throw CursorAPIError("数据加密口令至少 16 位；建议使用与登录密码不同的随机短语")
                        }
                        guard dataPassphrase == confirmDataPassphrase else { throw CursorAPIError("两次输入的数据加密口令不一致") }
                    }
                    try CloudSync.register(email: email, password: password)
                    DispatchQueue.main.async {
                        showVerification = true
                        syncStatus = "验证邮件已发送。输入邮件中的 6 位验证码后验证邮箱。"
                    }
                    return
                }
                let key = serverMode ? "" : (dataPassphrase.isEmpty ? cfg.syncSecret : dataPassphrase)
                if !serverMode && key.isEmpty { throw CursorAPIError("请输入数据加密口令；旧账号可暂用原登录密码") }
                let result = try CloudSync.login(email: email, password: password)
                if result.storage == "server" || result.storage == "envelope" { cfg.syncStorage = result.storage }
                CloudSync.applySession(&cfg, email: result.email.isEmpty ? email : result.email, dataPassphrase: cfg.syncStorage == "server" ? "" : key, access: result.access, refresh: result.refresh)
                DispatchQueue.main.async { syncStatus = "正在从云端导入账号和用量…" }
                let status = AccountSync.reconcile(&cfg)
                DispatchQueue.main.async {
                    applySyncedConfig(&cfg, startedActive: startedActive, refresh: status.changed)
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

    func verifyCloudEmail() {
        let email = cloudEmail.trimmingCharacters(in: .whitespaces)
        let password = cloudPassword.trimmingCharacters(in: .whitespaces)
        let key = cloudDataPassphrase.trimmingCharacters(in: .whitespaces)
        let confirmKey = cloudDataPassphraseConfirm.trimmingCharacters(in: .whitespaces)
        let code = verificationCode.trimmingCharacters(in: .whitespaces)
        let serverMode = store.config.syncStorage != "envelope"
        if !serverMode {
            guard key.count >= AccountSync.minimumDataPassphraseLength else { syncStatus = "数据加密口令至少 16 位"; return }
            guard key == confirmKey else { syncStatus = "两次输入的数据加密口令不一致"; return }
        }
        guard !code.isEmpty else { syncStatus = "请输入验证码"; return }
        var cfg = store.config
        let startedActive = cfg.activeAccountId
        DispatchQueue.global(qos: .userInitiated).async {
            do {
                try CloudSync.verifyEmail(email: email, code: code)
                let result = try CloudSync.login(email: email, password: password)
                if result.storage == "server" || result.storage == "envelope" { cfg.syncStorage = result.storage }
                CloudSync.applySession(&cfg, email: result.email.isEmpty ? email : result.email, dataPassphrase: cfg.syncStorage == "server" ? "" : key, access: result.access, refresh: result.refresh)
                let status = AccountSync.reconcile(&cfg)
                DispatchQueue.main.async {
                    applySyncedConfig(&cfg, startedActive: startedActive, refresh: status.changed)
                    showVerification = false
                    verificationCode = ""
                    cloudPassword = ""
                    cloudDataPassphrase = ""
                    cloudDataPassphraseConfirm = ""
                    syncStatus = status.message
                    hint = status.message
                }
            } catch {
                DispatchQueue.main.async { syncStatus = (error as? CursorAPIError)?.message ?? error.localizedDescription }
            }
        }
    }

    func generateDataPassphrase() {
        let key = AccountSync.generateDataPassphrase()
        cloudDataPassphrase = key
        cloudDataPassphraseConfirm = key
        generatedDataPassphrase = key
    }

    func resendVerificationEmail() {
        let email = cloudEmail.trimmingCharacters(in: .whitespaces)
        let password = cloudPassword.trimmingCharacters(in: .whitespaces)
        DispatchQueue.global(qos: .userInitiated).async {
            do {
                try CloudSync.resendVerification(email: email, password: password)
                DispatchQueue.main.async { syncStatus = "如果账号尚未验证，新的验证码已发送。" }
            } catch {
                DispatchQueue.main.async { syncStatus = (error as? CursorAPIError)?.message ?? error.localizedDescription }
            }
        }
    }

    func requestPasswordReset() {
        let email = cloudEmail.trimmingCharacters(in: .whitespaces)
        guard !email.isEmpty else { syncStatus = "请先填写注册邮箱"; return }
        DispatchQueue.global(qos: .userInitiated).async {
            do {
                try CloudSync.requestPasswordReset(email: email)
                DispatchQueue.main.async {
                    showPasswordReset = true
                    syncStatus = "如果邮箱已验证，重置验证码已发送。"
                }
            } catch {
                DispatchQueue.main.async { syncStatus = (error as? CursorAPIError)?.message ?? error.localizedDescription }
            }
        }
    }

    func confirmPasswordReset() {
        let email = cloudEmail.trimmingCharacters(in: .whitespaces)
        let code = resetCode.trimmingCharacters(in: .whitespaces)
        let password = resetNewPassword
        guard password.count >= 8 else { syncStatus = "登录密码至少 8 位"; return }
        DispatchQueue.global(qos: .userInitiated).async {
            do {
                try CloudSync.confirmPasswordReset(email: email, code: code, newPassword: password)
                DispatchQueue.main.async {
                    showPasswordReset = false
                    resetCode = ""
                    resetNewPassword = ""
                    syncStatus = "登录密码已重置。请重新登录；数据加密口令保持不变。"
                }
            } catch {
                DispatchQueue.main.async { syncStatus = (error as? CursorAPIError)?.message ?? error.localizedDescription }
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
        let startedActive = cfg.activeAccountId
        DispatchQueue.global(qos: .userInitiated).async {
            do {
                try CloudSync.changePassword(&cfg, oldPassword: old, newPassword: next)
                DispatchQueue.main.async {
                    applySyncedConfig(&cfg, startedActive: startedActive, refresh: false)
                    oldCloudPassword = ""
                    newCloudPassword = ""
                    confirmCloudPassword = ""
                    showChangePassword = false
                    syncStatus = cfg.syncStorage == "envelope"
                        ? "登录密码已更新；数据加密口令未改变。请在其他设备用新登录密码登录，并输入原数据加密口令。"
                        : "登录密码已更新。其他设备请用新登录密码登录。"
                }
            } catch {
                DispatchQueue.main.async {
                    syncStatus = (error as? CursorAPIError)?.message ?? error.localizedDescription
                }
            }
        }
    }

    func changeDataPassphrase() {
        let next = newDataPassphrase.trimmingCharacters(in: .whitespaces)
        let confirm = confirmDataPassphrase.trimmingCharacters(in: .whitespaces)
        guard next.count >= AccountSync.minimumDataPassphraseLength else { syncStatus = "数据加密口令至少 16 位"; return }
        guard next == confirm else { syncStatus = "两次输入的加密口令不一致"; return }
        syncStatus = "正在用新口令重封云端数据…"
        var cfg = store.config
        let startedActive = cfg.activeAccountId
        DispatchQueue.global(qos: .userInitiated).async {
            do {
                try CloudSync.changeDataPassphrase(&cfg, newPassphrase: next)
                DispatchQueue.main.async {
                    applySyncedConfig(&cfg, startedActive: startedActive, refresh: false)
                    newDataPassphrase = ""
                    confirmDataPassphrase = ""
                    showChangeDataPassphrase = false
                    syncStatus = "数据已用新口令重封；其他设备请退出云同步后重新登录，并输入新口令。"
                }
            } catch {
                DispatchQueue.main.async { syncStatus = (error as? CursorAPIError)?.message ?? error.localizedDescription }
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
        let startedActive = cfg.activeAccountId
        DispatchQueue.global(qos: .userInitiated).async {
            do {
                try CloudSync.deleteAccount(&cfg, password: password)
                DispatchQueue.main.async {
                    applySyncedConfig(&cfg, startedActive: startedActive, refresh: false)
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
        let startedActive = cfg.activeAccountId
        DispatchQueue.global(qos: .userInitiated).async {
            CloudSync.logout(&cfg)
            DispatchQueue.main.async {
                applySyncedConfig(&cfg, startedActive: startedActive, refresh: false)
                syncStatus = "已退出登录"
            }
        }
    }

    func syncNow() {
        syncStatus = "正在同步…"
        var cfg = store.config
        let startedActive = cfg.activeAccountId
        DispatchQueue.global(qos: .userInitiated).async {
            let status = AccountSync.reconcile(&cfg)
            DispatchQueue.main.async {
                applySyncedConfig(&cfg, startedActive: startedActive, refresh: status.changed)
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
        let startedActive = cfg.activeAccountId
        do {
            let dest = try AccountSync.exportToFile(&cfg, path: url.path, passphrase: exportPassphrase())
            applySyncedConfig(&cfg, startedActive: startedActive, refresh: false)
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
        let startedActive = cfg.activeAccountId
        do {
            try AccountSync.importFromFile(&cfg, path: url.path, passphrase: exportPassphrase())
            applySyncedConfig(&cfg, startedActive: startedActive, refresh: true)
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
}

private struct SettingsTabBar: View {
    @Binding var selection: String

    private let items = [("account", "账户"), ("notify", "通知"), ("menu", "菜单栏"), ("sync", "同步")]

    var body: some View {
        HStack(spacing: 0) {
            ForEach(Array(items.enumerated()), id: \.element.0) { index, item in
                if index > 0 {
                    Rectangle()
                        .fill(Color.primary.opacity(0.16))
                        .frame(width: 1, height: 12)
                }
                let on = selection == item.0
                Button {
                    selection = item.0
                } label: {
                    Text(item.1)
                        .font(.system(size: 13))
                        .foregroundStyle(.primary)
                        .padding(.horizontal, 12)
                        .padding(.vertical, 3)
                        .background(
                            RoundedRectangle(cornerRadius: 6, style: .continuous)
                                .fill(on ? Color.primary.opacity(0.12) : Color.clear)
                        )
                }
                .buttonStyle(.plain)
                .accessibilityLabel(item.1)
                .accessibilityAddTraits(on ? .isSelected : [])
            }
        }
        .padding(3)
        .background(
            RoundedRectangle(cornerRadius: 8, style: .continuous)
                .fill(Color.primary.opacity(0.05))
        )
        .frame(maxWidth: .infinity)
    }
}

private struct AppearanceSegment: View {
    @Binding var selection: String

    private let options: [(id: String, title: String, icon: String)] = [
        ("system", "跟随系统", "display"),
        ("light", "浅色", "sun.max"),
        ("dark", "深色", "moon"),
    ]

    var body: some View {
        HStack(spacing: 0) {
            ForEach(options, id: \.id) { option in
                let on = selection == option.id
                Button {
                    selection = option.id
                } label: {
                    HStack(spacing: 6) {
                        Image(systemName: option.icon)
                            .font(.system(size: 13))
                            .frame(width: 14, height: 14)
                        Text(option.title)
                            .font(.system(size: 13))
                    }
                    .foregroundStyle(.primary)
                    .padding(.horizontal, 12)
                    .frame(height: 26)
                    .background(
                        RoundedRectangle(cornerRadius: 6, style: .continuous)
                            .fill(on ? Color(nsColor: .controlBackgroundColor) : Color.clear)
                            .shadow(color: on ? Color.black.opacity(0.12) : Color.clear, radius: 1, y: 0.5)
                    )
                }
                .buttonStyle(.plain)
                .accessibilityLabel(option.title)
                .accessibilityAddTraits(on ? .isSelected : [])
            }
        }
        .padding(2)
        .background(
            RoundedRectangle(cornerRadius: 8, style: .continuous)
                .fill(Color.primary.opacity(0.06))
        )
        .accessibilityElement(children: .contain)
        .accessibilityLabel("颜色")
        .animation(.easeOut(duration: 0.12), value: selection)
    }
}

extension Account: Identifiable {}

private enum SettingsMetrics {
    static let width: CGFloat = 540
    static let padding: CGFloat = 20
    static let footerGap: CGFloat = 12
    static let footerReserve: CGFloat = 44
    static let tabBarReserve: CGFloat = 40
    static let minHeight: CGFloat = 420
}

private struct SettingsBodyHeightKey: PreferenceKey {
    static var defaultValue: CGFloat = 0
    static func reduce(value: inout CGFloat, nextValue: () -> CGFloat) {
        value = max(value, nextValue())
    }
}

@MainActor
final class SettingsWindowController: NSObject, NSWindowDelegate {
    static let shared = SettingsWindowController()
    private var window: NSWindow?
    private weak var store: AppStore?

    func applyAppearance(_ mode: String) {
        ColorModeAppearance.apply(to: window, mode: mode)
    }

    func resizeTo(height: CGFloat) {
        guard let win = window else { return }
        let chrome = win.frame.height - win.contentLayoutRect.height
        var frame = win.frame
        let newH = height + chrome
        frame.origin.y += frame.height - newH
        frame.size.height = newH
        win.setFrame(frame, display: true, animate: true)
    }

    func show(store: AppStore, focusToken: Bool, startImport: Bool) {
        self.store = store
        MenubarActivation.promoteForWindow()
        AppDelegate.ensureStatusItemVisible()
        if window == nil {
            let win = NSWindow(
                contentRect: NSRect(x: 0, y: 0, width: SettingsMetrics.width, height: SettingsMetrics.minHeight),
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
        // NSWindow always installs an empty contentView; only skip rebuild
        // when the settings hosting view is already there.
        if window?.contentView is NSHostingView<SettingsRootView> {
            if wasHidden {
                store.settingsReloadTick += 1
            }
        } else {
            window?.contentView = NSHostingView(rootView: SettingsRootView(store: store, startImport: startImport, focusToken: focusToken))
            window?.center()
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
