import AppKit
import CursorTokenCore
import SwiftUI

enum SettingsDateTime {
    static let weekdays = ["一", "二", "三", "四", "五", "六", "日"]
    static let minDate = Calendar.current.date(from: DateComponents(year: 2000, month: 1, day: 1)) ?? Date.distantPast
    static let maxDate = Calendar.current.date(from: DateComponents(year: 2100, month: 12, day: 31, hour: 23, minute: 59)) ?? Date.distantFuture

    static var calendar: Calendar {
        var calendar = Calendar(identifier: .gregorian)
        calendar.firstWeekday = 2
        calendar.timeZone = .current
        return calendar
    }

    struct Cell: Identifiable {
        let day: Date
        let inMonth: Bool
        var id: TimeInterval { day.timeIntervalSince1970 }
    }

    static func format(_ date: Date) -> String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.timeZone = .current
        formatter.dateFormat = "yyyy-MM-dd HH:mm"
        return formatter.string(from: clamp(date))
    }

    static func parse(_ raw: String) -> Date? {
        let text = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty else { return nil }
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.timeZone = .current
        formatter.isLenient = false
        for pattern in ["yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy/M/d H:m", "yyyy-MM-dd"] {
            formatter.dateFormat = pattern
            if let date = formatter.date(from: text) { return clamp(date) }
        }
        return nil
    }

    static func clamp(_ date: Date) -> Date {
        min(max(date, minDate), maxDate)
    }

    static func monthStart(of date: Date) -> Date {
        calendar.date(from: calendar.dateComponents([.year, .month], from: date)) ?? date
    }

    static func monthTitle(_ date: Date) -> String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "zh_CN")
        formatter.dateFormat = "yyyy年M月"
        return formatter.string(from: date)
    }

    static func cells(for month: Date) -> [Cell] {
        let start = monthStart(of: month)
        let weekday = calendar.component(.weekday, from: start)
        let leading = (weekday + 5) % 7
        let days = calendar.range(of: .day, in: .month, for: start)?.count ?? 30
        var items: [Cell] = []
        if leading > 0 {
            for offset in stride(from: leading, through: 1, by: -1) {
                if let day = calendar.date(byAdding: .day, value: -offset, to: start) {
                    items.append(Cell(day: day, inMonth: false))
                }
            }
        }
        for offset in 0 ..< days {
            if let day = calendar.date(byAdding: .day, value: offset, to: start) {
                items.append(Cell(day: day, inMonth: true))
            }
        }
        while items.count % 7 != 0 {
            if let last = items.last?.day, let day = calendar.date(byAdding: .day, value: 1, to: last) {
                items.append(Cell(day: day, inMonth: false))
            } else {
                break
            }
        }
        return items
    }

    static func shiftMonth(_ month: Date, by value: Int) -> Date {
        clamp(calendar.date(byAdding: .month, value: value, to: monthStart(of: month)) ?? month)
    }

    static func combine(day: Date, time: Date) -> Date {
        var parts = calendar.dateComponents([.year, .month, .day], from: day)
        let clock = calendar.dateComponents([.hour, .minute], from: time)
        parts.hour = clock.hour
        parts.minute = clock.minute
        parts.second = 0
        return clamp(calendar.date(from: parts) ?? day)
    }

    static func setClock(_ date: Date, hour: Int? = nil, minute: Int? = nil) -> Date {
        var parts = calendar.dateComponents([.year, .month, .day, .hour, .minute], from: date)
        if let hour { parts.hour = wrap(hour, in: 0...23) }
        if let minute { parts.minute = wrap(minute, in: 0...59) }
        parts.second = 0
        return clamp(calendar.date(from: parts) ?? date)
    }

    static func wrap(_ value: Int, in range: ClosedRange<Int>) -> Int {
        let span = range.upperBound - range.lowerBound + 1
        let offset = value - range.lowerBound
        return range.lowerBound + ((offset % span) + span) % span
    }

    static func sameDay(_ left: Date, _ right: Date) -> Bool {
        calendar.isDate(left, inSameDayAs: right)
    }
}

enum SettingsFieldChrome {
    static let height: CGFloat = 28
    static let padding: CGFloat = 8
    static let radius: CGFloat = 6
    static let dateWidth: CGFloat = 220
    static let durationWidth: CGFloat = 112
}

struct SettingsFieldBox<Content: View>: View {
    var width: CGFloat? = nil
    var focused: Bool = false
    @ViewBuilder var content: () -> Content

    var body: some View {
        content()
            .font(.body)
            .padding(.horizontal, SettingsFieldChrome.padding)
            .frame(width: width, height: SettingsFieldChrome.height, alignment: .leading)
            .background(
                RoundedRectangle(cornerRadius: SettingsFieldChrome.radius, style: .continuous)
                    .fill(Color(nsColor: .textBackgroundColor))
            )
            .overlay(
                RoundedRectangle(cornerRadius: SettingsFieldChrome.radius, style: .continuous)
                    .strokeBorder(
                        focused ? Color.accentColor : Color.secondary.opacity(0.22),
                        lineWidth: focused ? 2 : 1
                    )
            )
    }
}

struct SettingsDurationField: View {
    @Binding var days: Int
    @Binding var hours: Int
    @State private var daysDraft: String = ""
    @State private var hoursDraft: String = ""
    @FocusState private var focusedField: Field?

    enum Field: Hashable {
        case days
        case hours
    }

    var body: some View {
        HStack(spacing: 10) {
            SettingsFieldBox(width: SettingsFieldChrome.durationWidth, focused: focusedField == .days) {
                HStack(spacing: 6) {
                    TextField("0", text: $daysDraft)
                        .textFieldStyle(.plain)
                        .monospacedDigit()
                        .focused($focusedField, equals: .days)
                        .onSubmit { commitDays() }
                    Text("天")
                        .foregroundStyle(.secondary)
                }
            }
            SettingsFieldBox(width: SettingsFieldChrome.durationWidth, focused: focusedField == .hours) {
                HStack(spacing: 6) {
                    TextField("0", text: $hoursDraft)
                        .textFieldStyle(.plain)
                        .monospacedDigit()
                        .focused($focusedField, equals: .hours)
                        .onSubmit { commitHours() }
                    Text("小时")
                        .foregroundStyle(.secondary)
                }
            }
        }
        .onAppear { syncDrafts() }
        .onChange(of: days) { _ in daysDraft = "\(days)" }
        .onChange(of: hours) { _ in hoursDraft = "\(hours)" }
        .onChange(of: focusedField) { field in
            if field != .days { commitDays() }
            if field != .hours { commitHours() }
        }
    }

    func syncDrafts() {
        daysDraft = "\(days)"
        hoursDraft = "\(hours)"
    }

    func commitDays() {
        if let value = Int(daysDraft.trimmingCharacters(in: .whitespacesAndNewlines)) {
            days = AccountValidity.clampDays(value)
        }
        daysDraft = "\(days)"
    }

    func commitHours() {
        if let value = Int(hoursDraft.trimmingCharacters(in: .whitespacesAndNewlines)) {
            hours = AccountValidity.clampHours(value)
        }
        hoursDraft = "\(hours)"
    }
}

struct SettingsDateTimeField: View {
    @Binding var selection: Date
    @State private var dateDraft: String = ""
    @State private var calendarOpen: Bool = false
    @State private var calendarMonth: Date = Date()
    @FocusState private var draftFocused: Bool

    var body: some View {
        SettingsFieldBox(width: SettingsFieldChrome.dateWidth, focused: calendarOpen || draftFocused) {
            HStack(spacing: 6) {
                TextField("2026-09-14 10:07", text: $dateDraft)
                    .textFieldStyle(.plain)
                    .monospacedDigit()
                    .focused($draftFocused)
                    .onSubmit { commitDraft() }
                Button {
                    commitDraft()
                    calendarMonth = SettingsDateTime.monthStart(of: selection)
                    calendarOpen.toggle()
                } label: {
                    Image(systemName: "calendar")
                        .font(.body)
                        .foregroundStyle(.secondary)
                }
                .buttonStyle(.borderless)
                .help("选择日期和时间")
            }
        }
        .onAppear { dateDraft = SettingsDateTime.format(selection) }
        .onChange(of: selection) { dateDraft = SettingsDateTime.format($0) }
        .onChange(of: draftFocused) { if !$0 { commitDraft() } }
        .popover(isPresented: $calendarOpen, arrowEdge: .bottom) {
            SettingsDateTimePopover(selection: $selection, month: $calendarMonth)
        }
    }

    func commitDraft() {
        if let date = SettingsDateTime.parse(dateDraft) {
            selection = date
            dateDraft = SettingsDateTime.format(date)
        } else {
            dateDraft = SettingsDateTime.format(selection)
        }
    }
}

private struct SettingsDateTimePopover: View {
    @Binding var selection: Date
    @Binding var month: Date

    var body: some View {
        HStack(alignment: .center, spacing: 14) {
            monthGrid
            Divider()
            timeColumn
        }
        .padding(12)
        .frame(minWidth: 360)
    }

    var monthGrid: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Text(SettingsDateTime.monthTitle(month))
                    .font(.subheadline.weight(.medium))
                Spacer()
                HStack(spacing: 8) {
                    Button {
                        month = SettingsDateTime.shiftMonth(month, by: -1)
                    } label: {
                        Image(systemName: "chevron.left")
                    }
                    Button {
                        month = SettingsDateTime.shiftMonth(month, by: 1)
                    } label: {
                        Image(systemName: "chevron.right")
                    }
                }
                .buttonStyle(.borderless)
            }
            LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 0), count: 7), spacing: 2) {
                ForEach(Array(SettingsDateTime.weekdays.enumerated()), id: \.offset) { _, title in
                    Text(title)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .frame(maxWidth: .infinity, minHeight: 22)
                }
                ForEach(SettingsDateTime.cells(for: month)) { cell in
                    let selected = SettingsDateTime.sameDay(cell.day, selection)
                    Button {
                        selection = SettingsDateTime.combine(day: cell.day, time: selection)
                        month = SettingsDateTime.monthStart(of: cell.day)
                    } label: {
                        Text("\(SettingsDateTime.calendar.component(.day, from: cell.day))")
                            .font(.caption)
                            .frame(maxWidth: .infinity, minHeight: 26)
                            .background(selected ? Color.accentColor : Color.clear, in: Circle())
                            .foregroundStyle(selected ? Color.white : (cell.inMonth ? Color.primary : Color.secondary))
                    }
                    .buttonStyle(.plain)
                }
            }
            .frame(width: 228)
        }
    }

    var timeColumn: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("时间")
                .font(.caption)
                .foregroundStyle(.secondary)
            HStack(spacing: 4) {
                TimeWheel(range: 0...23, value: hourBinding)
                Text(":")
                    .foregroundStyle(.secondary)
                TimeWheel(range: 0...59, value: minuteBinding)
            }
        }
        .frame(minWidth: 96)
    }

    var hourBinding: Binding<Int> {
        Binding(
            get: { SettingsDateTime.calendar.component(.hour, from: selection) },
            set: { selection = SettingsDateTime.setClock(selection, hour: $0) }
        )
    }

    var minuteBinding: Binding<Int> {
        Binding(
            get: { SettingsDateTime.calendar.component(.minute, from: selection) },
            set: { selection = SettingsDateTime.setClock(selection, minute: $0) }
        )
    }
}

private struct TimeWheel: View {
    let range: ClosedRange<Int>
    @Binding var value: Int

    var body: some View {
        VStack(spacing: 4) {
            wheelRow(value - 1)
            Text(label(value))
                .padding(.horizontal, 7)
                .padding(.vertical, 2)
                .background(Color.accentColor, in: RoundedRectangle(cornerRadius: 6, style: .continuous))
                .foregroundStyle(.white)
            wheelRow(value + 1)
        }
        .font(.caption)
        .monospacedDigit()
    }

    func wheelRow(_ raw: Int) -> some View {
        let next = SettingsDateTime.wrap(raw, in: range)
        return Button(label(next)) {
            value = next
        }
        .buttonStyle(.plain)
        .foregroundStyle(.primary)
    }

    func label(_ number: Int) -> String {
        String(format: "%02d", number)
    }
}
