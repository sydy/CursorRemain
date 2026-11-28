import AppKit
import Combine
import CursorTokenCore
import SwiftUI

enum FlyoutLayout {
    static let width: CGFloat = 500
    static let height: CGFloat = 280
    static let size = CGSize(width: width, height: height)
    static let cornerRadius: CGFloat = 16
    static let padding: CGFloat = 16
    static let columnGap: CGFloat = 16
    static let leftWidth: CGFloat = 176
    static let ringSize: CGFloat = 148
    static let ringLine: CGFloat = 10
    static let cardRadius: CGFloat = 10
    static let cardPadding: CGFloat = 10
    static let cardGap: CGFloat = 8
    static let barHeight: CGFloat = 5
    static let toolButtonHeight: CGFloat = 28
    static let toolButtonGap: CGFloat = 6
    static let doubaoBlockGap: CGFloat = 8
    static let doubaoTitleHeight: CGFloat = 18
    static let doubaoRowHeight: CGFloat = 28

    // 已添加豆包账号后的上下叠放：圆环 92，两张卡各自按内容收高，不再撑成一样高。
    static let stackedRingSize: CGFloat = 92
    static let stackedRingLine: CGFloat = 7
    static let stackedCardGap: CGFloat = 10
    static let stackedRingGap: CGFloat = 14
    static let stackedToolbarGap: CGFloat = 8
    static let stackedPlanHeight: CGFloat = 28
    static let stackedLineHeight: CGFloat = 14
    static let stackedRowGap: CGFloat = 6
    static let stackedPillGap: CGFloat = 6
    static let stackedPillHeight: CGFloat = 18
    static let stackedMeterGap: CGFloat = 4
    static let stackedInfoGap: CGFloat = 3

    /// Cursor 圆环加下方状态胶囊。豆包圆环没有胶囊。
    static var stackedRingColumn: CGFloat { stackedRingSize + stackedPillGap + stackedPillHeight }

    /// 右侧文字列的设计高度，和叠放卡片里的 VStack 一一对应。
    static func stackedCursorText(meters: Int, prepaid: Bool, amount: Bool, infoLines: Int, error: Bool, updated: Bool) -> CGFloat {
        var parts: [CGFloat] = []
        if error {
            parts.append(stackedPlanHeight * 2)
            if updated { parts.append(stackedLineHeight) }
            parts.append(stackedLineHeight)
        } else {
            parts.append(stackedPlanHeight)
            if prepaid { parts.append(stackedLineHeight) }
            if amount { parts.append(stackedLineHeight + stackedMeterGap + barHeight) }
            if meters > 0 {
                let row = stackedLineHeight + stackedMeterGap + barHeight
                parts.append(CGFloat(meters) * row + CGFloat(meters - 1) * stackedMeterGap)
            }
            let lines = max(0, infoLines)
            if lines > 0 {
                parts.append(CGFloat(lines) * stackedLineHeight + CGFloat(lines - 1) * stackedInfoGap)
            }
            parts.append(stackedLineHeight)
        }
        return parts.reduce(0, +) + CGFloat(max(0, parts.count - 1)) * stackedRowGap
    }

    static func stackedCursorInner(meters: Int, prepaid: Bool, amount: Bool, infoLines: Int, error: Bool, updated: Bool) -> CGFloat {
        max(stackedRingColumn, stackedCursorText(meters: meters, prepaid: prepaid, amount: amount, infoLines: infoLines, error: error, updated: updated))
    }

    static func stackedDoubaoText(rows: Int, error: Bool) -> CGFloat {
        let body: CGFloat
        if error {
            body = stackedPlanHeight * 2
        } else if rows <= 0 {
            body = doubaoRowHeight
        } else {
            body = CGFloat(rows) * doubaoRowHeight + CGFloat(max(0, rows - 1)) * stackedRowGap
        }
        return doubaoTitleHeight + stackedRowGap + body
    }

    static func stackedDoubaoInner(rows: Int, error: Bool) -> CGFloat {
        max(stackedRingSize, stackedDoubaoText(rows: rows, error: error))
    }

    static func stackedCard(inner: CGFloat) -> CGFloat { cardPadding * 2 + inner }

    static func panelHeight(cursorInner: CGFloat, doubaoInner: CGFloat) -> CGFloat {
        padding * 2
            + stackedCard(inner: cursorInner)
            + stackedCardGap
            + stackedCard(inner: doubaoInner)
            + stackedToolbarGap
            + toolButtonHeight
    }

    static func cursorMeters(_ usage: UsageSnapshot) -> Int {
        guard !usage.isXSubscription else { return 0 }
        guard usage.autoPercentUsed != nil || usage.apiPercentUsed != nil || usage.showsGrokBot else { return 0 }
        var count = 0
        if usage.autoPercentUsed != nil { count += 1 }
        if usage.apiPercentUsed != nil { count += 1 }
        if usage.grokBotPercentUsed != nil { count += 1 }
        return count
    }

    /// 趋势和预计各占一行；有 Token 或重置日时再加一行。
    static func cursorInfoLines(_ usage: UsageSnapshot) -> Int {
        let meta = (!usage.isXSubscription && (usage.totalTokens ?? 0) > 0)
            || (usage.isXSubscription && usage.usageResetAt != nil)
            || (!usage.isXSubscription && usage.billingCycleEnd != nil)
        return meta ? 3 : 2
    }

    static func stackedCursorInner(usage: UsageSnapshot?, updated: Bool) -> CGFloat {
        guard let usage else {
            return stackedCursorInner(meters: 0, prepaid: false, amount: false, infoLines: 0, error: true, updated: updated)
        }
        return stackedCursorInner(
            meters: cursorMeters(usage),
            prepaid: usage.isXSubscription && usage.prepaidRemainingCents != nil,
            amount: usage.showsAmount,
            infoLines: cursorInfoLines(usage),
            error: false,
            updated: false
        )
    }
}

struct FlyoutView: View {
    @ObservedObject var store: AppStore
    @Environment(\.colorScheme) private var colorScheme

    var showsDoubao: Bool { store.config.activeDoubaoAccount != nil }
    var doubaoRows: [UsageWindow] { store.doubaoUsage?.windows ?? [] }
    var cursorInner: CGFloat {
        FlyoutLayout.stackedCursorInner(usage: store.usage, updated: store.updatedAt != nil)
    }

    var doubaoInner: CGFloat {
        FlyoutLayout.stackedDoubaoInner(rows: doubaoRows.count, error: store.doubaoError != nil)
    }

    var contentHeight: CGFloat {
        guard showsDoubao else { return FlyoutLayout.height }
        return FlyoutLayout.panelHeight(cursorInner: cursorInner, doubaoInner: doubaoInner)
    }

    var body: some View {
        Group {
            if showsDoubao {
                stackedBody
            } else {
                compactBody
            }
        }
        .padding(FlyoutLayout.padding)
        .frame(width: FlyoutLayout.width, height: contentHeight, alignment: .top)
        .background {
            RoundedRectangle(cornerRadius: FlyoutLayout.cornerRadius, style: .continuous)
                .fill(panelFill)
        }
        .clipShape(RoundedRectangle(cornerRadius: FlyoutLayout.cornerRadius, style: .continuous))
        .overlay(
            RoundedRectangle(cornerRadius: FlyoutLayout.cornerRadius, style: .continuous)
                .strokeBorder(Color.primary.opacity(colorScheme == .light ? 0.16 : 0.12), lineWidth: 0.5)
        )
    }

    /// 没有豆包账号：保持原来的 500×280 左右分栏。
    var compactBody: some View {
        VStack(spacing: 0) {
            HStack(alignment: .top, spacing: FlyoutLayout.columnGap) {
                leftColumn
                rightColumn
            }
            .frame(maxHeight: .infinity, alignment: .top)
            toolBar
        }
    }

    /// 有豆包账号：上下两张卡片按各自内容收高，短的一侧相对圆环垂直居中。
    var stackedBody: some View {
        VStack(spacing: 0) {
            stackedCard(height: FlyoutLayout.stackedCard(inner: cursorInner)) {
                HStack(alignment: .center, spacing: FlyoutLayout.stackedRingGap) {
                    cursorRingColumn
                    stackedCursorRight
                }
            }
            stackedCard(height: FlyoutLayout.stackedCard(inner: doubaoInner)) {
                HStack(alignment: .center, spacing: FlyoutLayout.stackedRingGap) {
                    doubaoRingColumn
                    stackedDoubaoRight
                }
            }
            .padding(.top, FlyoutLayout.stackedCardGap)
            toolBar
                .padding(.top, FlyoutLayout.stackedToolbarGap)
        }
    }

    /// Light material turns into a gray wash and the cards disappear into it.
    var panelFill: AnyShapeStyle {
        if colorScheme == .light {
            return AnyShapeStyle(Color(nsColor: .windowBackgroundColor))
        }
        return AnyShapeStyle(.ultraThinMaterial)
    }

    var remaining: Double? { store.usage?.remainingPercent }
    var isError: Bool { store.usage == nil && store.errorMessage != nil }
    var isUnlimited: Bool { store.usage?.isUnlimited == true }

    var leftColumn: some View {
        VStack(spacing: 10) {
            RemainingGauge(
                remaining: remaining,
                error: isError,
                unlimited: isUnlimited,
                color: gaugeColor
            ) {
                pill
            }
            if store.usage != nil, !isError {
                Text(planCaption)
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)
                    .lineLimit(2)
            } else {
                Text(StatusText.formatFlyoutError(store.errorMessage))
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)
                    .lineLimit(3)
                    .frame(maxWidth: FlyoutLayout.leftWidth, alignment: .center)
            }
            Button(UsageParser.dashboardLinkLabel(store.usage)) { store.openDashboard() }
                .buttonStyle(.plain)
                .foregroundStyle(Color.accentColor)
                .font(.caption)
            Spacer(minLength: 0)
        }
        .frame(width: FlyoutLayout.leftWidth)
    }

    var planCaption: String {
        guard let usage = store.usage else { return "" }
        return StatusText.formatPlanCaption(usage.membershipType, accountLabel: store.config.activeAccount?.displayLabel)
    }

    var gaugeColor: Color { RemainingTone.color(remaining: remaining, error: isError, unlimited: isUnlimited) }

    var pill: some View {
        let text = StatusText.statusPillText(remaining, error: isError)
        return Text(text)
            .font(.caption)
            .padding(.horizontal, 10)
            .padding(.vertical, 3)
            .background(gaugeColor.opacity(0.25), in: Capsule())
    }

    // MARK: - 叠放模式（已添加豆包账号）

    /// 叠放卡片：高度按内容给定，内容在卡片内垂直居中。
    func stackedCard<Content: View>(height: CGFloat, @ViewBuilder _ content: () -> Content) -> some View {
        content()
            .padding(FlyoutLayout.cardPadding)
            .frame(maxWidth: .infinity, alignment: .leading)
            .frame(height: height, alignment: .center)
            .background(cardFill, in: RoundedRectangle(cornerRadius: FlyoutLayout.cardRadius, style: .continuous))
            .overlay(
                RoundedRectangle(cornerRadius: FlyoutLayout.cardRadius, style: .continuous)
                    .strokeBorder(Color.primary.opacity(colorScheme == .light ? 0.08 : 0), lineWidth: 0.5)
            )
    }

    /// Cursor 圆环 + 圆环下方的状态胶囊。
    var cursorRingColumn: some View {
        VStack(spacing: FlyoutLayout.stackedPillGap) {
            RemainingGauge(
                remaining: remaining,
                error: isError,
                unlimited: isUnlimited,
                color: gaugeColor,
                size: FlyoutLayout.stackedRingSize,
                lineWidth: FlyoutLayout.stackedRingLine
            ) { EmptyView() }
            pillChip
        }
        .frame(width: FlyoutLayout.stackedRingSize)
    }

    var pillChip: some View {
        Text(StatusText.statusPillText(remaining, error: isError))
            .font(.system(size: 10))
            .lineLimit(1)
            .padding(.horizontal, 8)
            .frame(height: FlyoutLayout.stackedPillHeight)
            .background(gaugeColor.opacity(0.25), in: Capsule())
    }

    var stackedCursorRight: some View {
        VStack(alignment: .leading, spacing: FlyoutLayout.stackedRowGap) {
            if let usage = store.usage, !isError {
                Text(planCaption)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .lineLimit(2)
                    .frame(height: FlyoutLayout.stackedPlanHeight, alignment: .topLeading)
                if usage.isXSubscription, let prepaid = usage.prepaidRemainingCents {
                    labeled("预付余额", UsageParser.formatUSDCents(prepaid))
                        .frame(height: FlyoutLayout.stackedLineHeight)
                }
                if usage.showsAmount {
                    VStack(alignment: .leading, spacing: 4) {
                        labeled("金额", UsageParser.formatSpendRange(used: usage.usedCents, limit: usage.limitCents))
                            .frame(height: FlyoutLayout.stackedLineHeight)
                        if let used = usage.usedCents, let limit = usage.limitCents, limit > 0 {
                            bar(used / limit, color: Color(red: 48 / 255, green: 209 / 255, blue: 88 / 255))
                        }
                    }
                }
                if !usage.isXSubscription && (usage.autoPercentUsed != nil || usage.apiPercentUsed != nil || usage.showsGrokBot) {
                    VStack(alignment: .leading, spacing: 4) {
                        if let auto = usage.autoPercentUsed {
                            stackedMeter("First-party", auto, color: Color(red: 50 / 255, green: 180 / 255, blue: 170 / 255))
                        }
                        if let api = usage.apiPercentUsed {
                            stackedMeter("API", api, color: Color(red: 142 / 255, green: 142 / 255, blue: 147 / 255))
                        }
                        if let grok = usage.grokBotPercentUsed {
                            stackedMeter("Grok Bot", grok, color: Color(red: 99 / 255, green: 102 / 255, blue: 241 / 255))
                        }
                    }
                }
                stackedInfoBlock(usage)
                dashboardLink
            } else {
                Text(StatusText.formatFlyoutError(store.errorMessage))
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .lineLimit(3)
                    .frame(height: FlyoutLayout.stackedPlanHeight * 2, alignment: .topLeading)
                if let updated = store.updatedAt {
                    Text("更新  \(updated)")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                        .frame(height: FlyoutLayout.stackedLineHeight, alignment: .leading)
                }
                dashboardLink
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    var dashboardLink: some View {
        Button(UsageParser.dashboardLinkLabel(store.usage)) { store.openDashboard() }
            .buttonStyle(.plain)
            .foregroundStyle(Color.accentColor)
            .font(.caption)
            .frame(height: FlyoutLayout.stackedLineHeight, alignment: .leading)
    }

    /// 三条额度进度条在叠放卡里并排成一段，每条都独立着色。
    func stackedMeter(_ title: String, _ percent: Double, color: Color) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 6) {
                Circle().fill(color).frame(width: 6, height: 6)
                Text(title).font(.caption).foregroundStyle(.secondary)
                Spacer()
                Text(String(format: "%.1f%%", percent)).font(.caption.monospacedDigit())
            }
            .frame(height: FlyoutLayout.stackedLineHeight)
            bar(percent / 100, color: color)
        }
    }

    func stackedInfoBlock(_ usage: UsageSnapshot) -> some View {
        let meta = infoMeta(usage)
        return VStack(alignment: .leading, spacing: 3) {
            if !meta.isEmpty {
                Text(meta)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
                    .frame(height: FlyoutLayout.stackedLineHeight, alignment: .leading)
            }
            Text(trendText)
                .font(.caption)
                .foregroundStyle(.secondary)
                .lineLimit(1)
                .frame(height: FlyoutLayout.stackedLineHeight, alignment: .leading)
            Text(StatusText.formatEstimateCaption(usage))
                .font(.caption)
                .foregroundStyle(estimateColor(usage))
                .lineLimit(1)
                .frame(height: FlyoutLayout.stackedLineHeight, alignment: .leading)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    /// 豆包圆环取主套餐窗口的剩余百分比（1 个月 > 套餐 > 7 天 > 5 小时），配色与 Cursor 共用 RemainingTone。
    var doubaoHeadlineWindow: UsageWindow? {
        for key in ["1m", "package", "7d", "5h"] {
            if let window = doubaoRows.first(where: { $0.key == key }) { return window }
        }
        return doubaoRows.first
    }

    var doubaoHeadlineRemaining: Double? {
        guard let window = doubaoHeadlineWindow else { return nil }
        return window.unlimited ? 100 : max(0, min(100, 100 - window.usedPercent))
    }

    var doubaoRingColumn: some View {
        VStack(spacing: 0) {
            RemainingGauge(
                remaining: doubaoHeadlineRemaining,
                error: store.doubaoError != nil,
                unlimited: doubaoHeadlineWindow?.unlimited == true,
                color: doubaoRingColor,
                size: FlyoutLayout.stackedRingSize,
                lineWidth: FlyoutLayout.stackedRingLine
            ) { EmptyView() }
        }
        .frame(width: FlyoutLayout.stackedRingSize)
    }

    var doubaoRingColor: Color {
        RemainingTone.color(
            remaining: doubaoHeadlineRemaining,
            error: store.doubaoError != nil,
            unlimited: doubaoHeadlineWindow?.unlimited == true
        )
    }

    var stackedDoubaoRight: some View {
        VStack(alignment: .leading, spacing: FlyoutLayout.stackedRowGap) {
            doubaoHeaderRow
            if let error = store.doubaoError {
                Text(error)
                    .font(.caption2)
                    .foregroundStyle(.red)
                    .lineLimit(3)
                    .frame(height: FlyoutLayout.stackedPlanHeight * 2, alignment: .topLeading)
            } else if doubaoRows.isEmpty {
                Text("等待刷新…")
                    .font(.caption2)
                    .foregroundStyle(.secondary)
                    .frame(height: FlyoutLayout.doubaoRowHeight, alignment: .leading)
            } else {
                VStack(alignment: .leading, spacing: FlyoutLayout.stackedRowGap) {
                    ForEach(doubaoRows, id: \.key) { window in
                        doubaoRow(window)
                    }
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    var doubaoHeaderRow: some View {
        HStack(spacing: 6) {
            Text("豆包").font(.caption).fontWeight(.semibold)
            if let membership = store.config.activeDoubaoAccount?.membershipType, !membership.isEmpty {
                Text("· \(membership)")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            if let updated = store.doubaoUpdatedAt {
                Text("· 更新 \(shortClock(updated))")
                    .font(.caption2)
                    .foregroundStyle(.tertiary)
            }
            Spacer(minLength: 0)
            if store.doubaoError != nil {
                Button(store.doubaoLoginBusy ? "正在登录…" : "重新登录") {
                    FlyoutWindowController.shared.close()
                    store.reloginActiveDoubao()
                }
                .font(.caption2)
                .buttonStyle(.link)
                .disabled(store.doubaoLoginBusy)
            } else {
                Text(store.config.activeDoubaoAccount?.displayLabel ?? "")
                    .font(.caption2)
                    .foregroundStyle(.tertiary)
                    .lineLimit(1)
            }
        }
        .frame(height: FlyoutLayout.doubaoTitleHeight)
    }

    var rightColumn: some View {
        VStack(alignment: .leading, spacing: FlyoutLayout.cardGap) {
            if let usage = store.usage, !isError {
                if usage.isXSubscription, let prepaid = usage.prepaidRemainingCents {
                    card {
                        labeled("预付余额", UsageParser.formatUSDCents(prepaid))
                    }
                }
                if usage.showsAmount {
                    card {
                        labeled("金额", UsageParser.formatSpendRange(used: usage.usedCents, limit: usage.limitCents))
                        if let used = usage.usedCents, let limit = usage.limitCents, limit > 0 {
                            bar(used / limit, color: Color(red: 48 / 255, green: 209 / 255, blue: 88 / 255))
                        }
                    }
                }
                if !usage.isXSubscription && (usage.autoPercentUsed != nil || usage.apiPercentUsed != nil || usage.showsGrokBot) {
                    card {
                        if let auto = usage.autoPercentUsed {
                            meterRow("First-party", auto, color: Color(red: 50 / 255, green: 180 / 255, blue: 170 / 255))
                        }
                        if let api = usage.apiPercentUsed {
                            meterRow("API", api, color: Color(red: 142 / 255, green: 142 / 255, blue: 147 / 255))
                        }
                        if let grok = usage.grokBotPercentUsed {
                            meterRow("Grok Bot", grok, color: Color(red: 99 / 255, green: 102 / 255, blue: 241 / 255))
                        }
                    }
                }
                card {
                    let meta = infoMeta(usage)
                    if !meta.isEmpty {
                        Text(meta)
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                    Text(trendText)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                    Text(StatusText.formatEstimateCaption(usage))
                        .font(.caption)
                        .foregroundStyle(estimateColor(usage))
                }
            } else if let updated = store.updatedAt {
                Text("更新  \(updated)").font(.caption).foregroundStyle(.secondary)
            }
            Spacer(minLength: 0)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    var toolBar: some View {
        HStack(spacing: FlyoutLayout.toolButtonGap) {
            Spacer(minLength: 0)
            toolButton("复制", "doc.on.doc") { store.copySummary() }
            toolButton("刷新", "arrow.clockwise") { store.requestRefresh() }
            toolButton("报表", "chart.bar") {
                FlyoutWindowController.shared.close()
                store.openReport()
            }
            toolButton("对比", "tablecells") {
                FlyoutWindowController.shared.close()
                store.openCompare()
            }
            toolButton(StatusText.flyoutSettingsTitle(store.errorMessage), "gearshape") {
                FlyoutWindowController.shared.close()
                store.openSettings(focusToken: false)
            }
        }
        .padding(.top, 4)
        .frame(minHeight: FlyoutLayout.toolButtonHeight)
    }

    func estimateColor(_ usage: UsageSnapshot) -> Color {
        let text = StatusText.formatEstimateCaption(usage)
        if text.contains("撑到") || text.contains("可撑过") { return Color(red: 48 / 255, green: 209 / 255, blue: 88 / 255) }
        if text.contains("耗尽") || text.contains("紧张") { return Color(red: 231 / 255, green: 76 / 255, blue: 60 / 255) }
        return .secondary
    }

    /// 豆包卡片右侧：只展窗口行，不复用 Cursor 的「本期 / 按需 / API」三行。
    func doubaoRow(_ window: UsageWindow) -> some View {
        let remaining = window.unlimited ? 100 : 100 - window.usedPercent
        let tone = RemainingTone.color(remaining: remaining, error: false)
        return VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 6) {
                Circle().fill(tone).frame(width: 6, height: 6)
                Text(doubaoWindowLabel(window)).font(.caption).foregroundStyle(.secondary)
                if window.isCurrentPeriod {
                    Text(doubaoResetCaption(window))
                        .font(.caption2)
                        .foregroundStyle(.tertiary)
                } else if window.key == "incentive", let reset = window.resetAt, !reset.isEmpty {
                    Text(doubaoExpiryCaption(reset))
                        .font(.caption2)
                        .foregroundStyle(.tertiary)
                }
                Spacer(minLength: 0)
                Text(doubaoWindowValue(window))
                    .font(.caption.monospacedDigit())
            }
            bar(window.unlimited ? 1 : window.usedPercent / 100, color: tone)
        }
        .frame(height: FlyoutLayout.doubaoRowHeight, alignment: .top)
    }

    /// 活动赠送额度标注条目数与剩余，会员三档保持原标签。
    func doubaoWindowLabel(_ window: UsageWindow) -> String {
        guard let count = window.count, count > 1 else { return window.label }
        return "\(window.label)（\(count) 项）"
    }

    func doubaoWindowValue(_ window: UsageWindow) -> String {
        let text = UsageParser.doubaoWindowText(window)
        guard window.key == "incentive", !window.unlimited, !window.exhausted else { return text }
        let remaining = max(0, min(100, 100 - window.usedPercent))
        return text + " · 剩 " + percentText(remaining)
    }

    func percentText(_ value: Double) -> String {
        let rounded = (value * 10).rounded() / 10
        return rounded == rounded.rounded() ? "\(Int(rounded))%" : String(format: "%.1f%%", rounded)
    }

    func doubaoResetCaption(_ window: UsageWindow) -> String {
        let text = UsageParser.doubaoResetText(window)
        return text.isEmpty ? "开始使用后计时" : text
    }

    /// 活动赠送额度的到期日：同年只显示「11-22 到期」，跨年带年份。
    func doubaoExpiryCaption(_ iso: String) -> String {
        guard let date = AccountSync.parseIso(iso) else { return "" }
        let formatter = DateFormatter()
        let sameYear = Calendar.current.component(.year, from: date) == Calendar.current.component(.year, from: Date())
        formatter.dateFormat = sameYear ? "M-d" : "yyyy-M-d"
        return "· \(formatter.string(from: date)) 到期"
    }

    func shortClock(_ iso: String) -> String {
        guard let date = AccountSync.parseIso(iso) else { return "" }
        let formatter = DateFormatter()
        formatter.dateFormat = "HH:mm"
        return formatter.string(from: date)
    }

    func card<Content: View>(@ViewBuilder _ content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            content()
        }
        .padding(FlyoutLayout.cardPadding)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(cardFill, in: RoundedRectangle(cornerRadius: FlyoutLayout.cardRadius, style: .continuous))
        .overlay(
            RoundedRectangle(cornerRadius: FlyoutLayout.cardRadius, style: .continuous)
                .strokeBorder(Color.primary.opacity(colorScheme == .light ? 0.08 : 0), lineWidth: 0.5)
        )
    }

    var cardFill: Color {
        colorScheme == .light ? Color(nsColor: .textBackgroundColor) : Color.primary.opacity(0.06)
    }

    func labeled(_ title: String, _ value: String) -> some View {
        HStack {
            Text(title).font(.caption).foregroundStyle(.secondary)
            Spacer()
            Text(value).font(.caption.monospacedDigit()).fontWeight(.medium)
        }
    }

    func meterRow(_ title: String, _ percent: Double, color: Color) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 6) {
                Circle().fill(color).frame(width: 6, height: 6)
                Text(title).font(.caption).foregroundStyle(.secondary)
                Spacer()
                Text(String(format: "%.1f%%", percent)).font(.caption.monospacedDigit())
            }
            bar(percent / 100, color: color)
        }
    }

    func bar(_ fraction: Double, color: Color) -> some View {
        GeometryReader { geo in
            ZStack(alignment: .leading) {
                Capsule().fill(Color.secondary.opacity(0.2))
                Capsule()
                    .fill(color)
                    .frame(width: geo.size.width * min(1, max(0, fraction)))
            }
        }
        .frame(height: FlyoutLayout.barHeight)
    }

    func toolButton(_ title: String, _ systemImage: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            HStack(spacing: 4) {
                Image(systemName: systemImage)
                Text(title)
                    .lineLimit(1)
                    .fixedSize()
            }
            .font(.caption)
            .foregroundStyle(.secondary)
            .padding(.horizontal, 8)
            .padding(.vertical, 5)
            .background(Color.primary.opacity(colorScheme == .light ? 0.06 : 0.08), in: Capsule())
        }
        .buttonStyle(.plain)
        .fixedSize()
        .layoutPriority(1)
        .help(title)
    }

    func infoMeta(_ usage: UsageSnapshot) -> String {
        var parts: [String] = []
        if !usage.isXSubscription, let tokens = usage.totalTokens, tokens > 0 {
            parts.append("Token \(UsageParser.formatTokenCount(Double(tokens)))")
        }
        if usage.isXSubscription, let reset = usage.usageResetAt {
            parts.append("重置 \(StatusText.formatResetDate(reset))")
        } else if !usage.isXSubscription, let end = usage.billingCycleEnd {
            parts.append("\(StatusText.cycleEndLabel(usage)) \(StatusText.formatResetDate(end, includeTime: usage.billingCycleEndOverridden))")
        }
        return parts.joined(separator: " · ")
    }

    var trendText: String {
        let points = store.historyPoints
        return SparklineGeometry.trendSummary(
            points: points,
            dailyAvg: store.dailyAvgBurn,
            current: remaining ?? points.last?.remaining,
            nowTs: Date().timeIntervalSince1970
        )
    }
}

enum RemainingTone {
    static func color(remaining: Double?, error: Bool, unlimited: Bool = false) -> Color {
        if error { return Color(nsColor: .systemGray) }
        if unlimited { return Color(red: 48 / 255, green: 209 / 255, blue: 88 / 255) }
        guard let remaining else { return Color(nsColor: .systemGray) }
        if remaining < 20 { return Color(red: 231 / 255, green: 76 / 255, blue: 60 / 255) }
        if remaining < 50 { return Color(red: 241 / 255, green: 196 / 255, blue: 15 / 255) }
        return Color(red: 46 / 255, green: 204 / 255, blue: 113 / 255)
    }
}

struct RemainingGauge<Pill: View>: View {
    var remaining: Double?
    var error: Bool
    var unlimited: Bool
    var color: Color
    var size: CGFloat = FlyoutLayout.ringSize
    var lineWidth: CGFloat = FlyoutLayout.ringLine
    @Environment(\.colorScheme) private var colorScheme
    @ViewBuilder var pill: () -> Pill

    /// 148 是原始尺寸；叠放模式 92 时字号与留白同比缩小，两个圆环才会「一样大」。
    var scale: CGFloat { size / FlyoutLayout.ringSize }
    var numberSize: CGFloat { max(15, 28 * scale) }
    var captionSize: CGFloat { max(8, 10 * scale) }

    var progress: CGFloat {
        if error { return 0 }
        if unlimited { return 1 }
        return CGFloat(min(1, max(0, (remaining ?? 0) / 100)))
    }

    var body: some View {
        ZStack {
            Circle()
                .stroke(Color.primary.opacity(colorScheme == .light ? 0.16 : 0.12), lineWidth: lineWidth)
            Circle()
                .trim(from: 0, to: progress)
                .stroke(color, style: StrokeStyle(lineWidth: lineWidth, lineCap: .round))
                .rotationEffect(.degrees(-90))
                .animation(.easeInOut(duration: 0.35), value: progress)
            VStack(spacing: 4 * scale) {
                Text("剩余")
                    .font(.system(size: captionSize))
                    .foregroundStyle(.secondary)
                if unlimited {
                    Text("不限量").font(.system(size: max(12, 22 * scale), weight: .bold, design: .rounded))
                } else if let remaining, !error {
                    HStack(alignment: .firstTextBaseline, spacing: 2) {
                        Text(String(format: "%.1f", remaining))
                            .font(.system(size: numberSize, weight: .bold, design: .rounded))
                            .monospacedDigit()
                        Text("%").font(.system(size: max(9, 13 * scale), weight: .semibold)).foregroundStyle(.secondary)
                    }
                } else {
                    Text(error ? "—" : "…")
                        .font(.system(size: numberSize, weight: .bold, design: .rounded))
                        .foregroundStyle(.secondary)
                }
                pill()
            }
        }
        .frame(width: size, height: size)
    }
}

@MainActor
final class FlyoutWindowController: NSObject, NSWindowDelegate {
    static let shared = FlyoutWindowController()
    private var window: NSPanel?
    private var monitor: Any?
    private var keyMonitor: Any?
    private weak var store: AppStore?
    private weak var statusButton: NSStatusBarButton?
    private var renderedHeight: CGFloat = 0

    /// 豆包区块可能让浮窗变高：窗口尺寸与 SwiftUI 根视图必须一起更新。
    func contentSize(store: AppStore) -> NSSize {
        guard store.config.activeDoubaoAccount != nil else { return FlyoutLayout.size }
        let cursor = FlyoutLayout.stackedCursorInner(usage: store.usage, updated: store.updatedAt != nil)
        let doubao = FlyoutLayout.stackedDoubaoInner(rows: store.doubaoUsage?.windows.count ?? 0, error: store.doubaoError != nil)
        return CGSize(width: FlyoutLayout.width, height: FlyoutLayout.panelHeight(cursorInner: cursor, doubaoInner: doubao))
    }

    func applyAppearance(_ mode: String) {
        ColorModeAppearance.apply(to: window, mode: mode)
    }

    func toggle(store: AppStore, statusButton: NSStatusBarButton?) {
        if window?.isVisible == true {
            // 多次点击只打开，不关闭
            update(store: store)
            position(statusButton: statusButton)
            window?.makeKeyAndOrderFront(nil)
            return
        }
        show(store: store, statusButton: statusButton)
    }

    func show(store: AppStore, statusButton: NSStatusBarButton?) {
        self.store = store
        self.statusButton = statusButton
        store.flyoutVisible = true
        if NSApp.isHidden {
            NSApp.unhideWithoutActivation()
        }
        let size = contentSize(store: store)
        renderedHeight = size.height
        if window == nil {
            let panel = NSPanel(
                contentRect: NSRect(origin: .zero, size: size),
                styleMask: [.borderless, .nonactivatingPanel],
                backing: .buffered,
                defer: false
            )
            panel.isFloatingPanel = true
            panel.level = .floating
            panel.isOpaque = false
            panel.backgroundColor = .clear
            panel.hasShadow = true
            panel.hidesOnDeactivate = false
            panel.delegate = self
            panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
            ColorModeAppearance.apply(to: panel, mode: store.config.colorMode)
            window = panel
        }
        window?.setContentSize(size)
        window?.contentView = hostedView(store: store)
        position(statusButton: statusButton)
        window?.makeKeyAndOrderFront(nil)
        installMonitor()
    }

    func update(store: AppStore) {
        self.store = store
        guard let window, window.isVisible else { return }
        let size = contentSize(store: store)
        if abs(size.height - renderedHeight) > 0.5 {
            renderedHeight = size.height
            window.setContentSize(size)
            window.contentView = hostedView(store: store)
            position(statusButton: statusButton)
        }
    }

    func hostedView(store: AppStore) -> NSView {
        let root = FlyoutView(store: store)
            .clipShape(RoundedRectangle(cornerRadius: FlyoutLayout.cornerRadius, style: .continuous))
        return NSHostingView(rootView: root)
    }

    func close() {
        store?.flyoutVisible = false
        window?.orderOut(nil)
        removeMonitor()
    }

    func windowDidResignKey(_ notification: Notification) { close() }

    func position(statusButton: NSStatusBarButton?) {
        guard let window else { return }
        let size = store.map(contentSize(store:)) ?? FlyoutLayout.size
        var icon = NSRect(x: 0, y: 0, width: 22, height: 22)
        if let button = statusButton, let win = button.window {
            icon = win.convertToScreen(button.convert(button.bounds, to: nil))
        }
        let screen = NSScreen.main?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1440, height: 900)
        let origin = popupOrigin(icon: icon, popup: size, visible: screen)
        window.setFrame(NSRect(origin: origin, size: size), display: true)
    }

    func popupOrigin(icon: NSRect, popup: NSSize, visible: NSRect, gap: CGFloat = 10, margin: CGFloat = 8) -> NSPoint {
        let left = visible.minX + margin
        let right = visible.maxX - margin
        let bottom = visible.minY + margin
        let top = visible.maxY
        var x = icon.maxX - popup.width
        if x < left { x = left }
        if x + popup.width > right { x = right - popup.width }
        if x < left { x = left }
        var y = top - gap - popup.height
        if y + popup.height > icon.minY - 2 {
            y = icon.minY - gap - popup.height
        }
        if y < bottom { y = bottom }
        return NSPoint(x: x, y: y)
    }

    func installMonitor() {
        removeMonitor()
        monitor = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown]) { [weak self] _ in
            let loc = NSEvent.mouseLocation
            Task { @MainActor in
                guard let self, let window = self.window, window.isVisible else { return }
                if !window.frame.contains(loc) {
                    self.close()
                }
            }
        }
        keyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            if event.keyCode == 53 {
                Task { @MainActor in self?.close() }
                return nil
            }
            return event
        }
    }

    func removeMonitor() {
        if let monitor { NSEvent.removeMonitor(monitor) }
        monitor = nil
        if let keyMonitor { NSEvent.removeMonitor(keyMonitor) }
        keyMonitor = nil
    }
}
