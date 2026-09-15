import Foundation

public enum SparklineCopy {
    public static let title = "近 7 日剩余额度"
    public static let emptyHint = "刷新几次后将显示近 7 日剩余趋势"
    public static let flat = "近 7 日剩余几乎没变"
    public static let axisStart = "7天前"
    public static let axisEnd = "现在"
    public static let reset = "重置"
    public static let minus = "\u{2212}"
    public static let windowDays = 7
    public static let flatBurnEpsilon = 0.05
    public static let resetJump = 15.0
}

public struct SparkMappedPoint: Equatable, Sendable {
    public var x: Double
    public var y: Double
    public var ts: Double
    public var remaining: Double
    public var index: Int

    public init(x: Double, y: Double, ts: Double, remaining: Double, index: Int) {
        self.x = x
        self.y = y
        self.ts = ts
        self.remaining = remaining
        self.index = index
    }
}

public struct SparkPlot: Equatable, Sendable {
    public var points: [SparkMappedPoint]
    public var reset: SparkMappedPoint?
    public var t0: Double
    public var t1: Double

    public init(points: [SparkMappedPoint], reset: SparkMappedPoint?, t0: Double, t1: Double) {
        self.points = points
        self.reset = reset
        self.t0 = t0
        self.t1 = t1
    }
}

public enum SparklineGeometry {
    public static func range(_: [Double]) -> (min: Double, max: Double) { (0, 100) }

    public static func yAt(_ remaining: Double, height: Double) -> Double {
        height * (1 - min(100, max(0, remaining)) / 100)
    }

    public static func points(_ values: [Double], width: Double, height: Double) -> [(x: Double, y: Double)] {
        let n = max(values.count - 1, 1)
        return values.enumerated().map { i, v in
            (width * Double(i) / Double(n), yAt(v, height: height))
        }
    }

    public static func layout(
        _ points: [HistoryPoint],
        width: Double,
        height: Double,
        nowTs: Double,
        cycleStartTs: Double? = nil,
        windowDays: Int = SparklineCopy.windowDays
    ) -> SparkPlot {
        let windowStart = nowTs - Double(max(1, windowDays)) * 86_400
        var minTs = points.first?.ts ?? windowStart
        var maxTs = points.first?.ts ?? nowTs
        for p in points {
            minTs = min(minTs, p.ts)
            maxTs = max(maxTs, p.ts)
        }

        // Fit the axis to the sample span so a day of history is not
        // crushed into the right edge of an empty 7-day window.
        let t0 = max(windowStart, minTs)
        let t1 = max(nowTs, maxTs)
        var span = t1 - t0
        if span <= 0 { span = 1 }

        var mapped: [SparkMappedPoint] = []
        for (i, p) in points.enumerated() {
            var xFrac = (p.ts - t0) / span
            if xFrac.isNaN || xFrac.isInfinite { xFrac = 0 }
            mapped.append(SparkMappedPoint(
                x: width * min(1, max(0, xFrac)),
                y: yAt(p.remaining, height: height),
                ts: p.ts,
                remaining: p.remaining,
                index: i
            ))
        }

        if mapped.count >= 2, maxTs - minTs < 1 {
            let n = max(mapped.count - 1, 1)
            for i in mapped.indices {
                mapped[i].x = width * Double(i) / Double(n)
            }
        }

        return SparkPlot(
            points: mapped,
            reset: findReset(mapped, width: width, height: height, t0: t0, t1: t1, cycleStartTs: cycleStartTs),
            t0: t0,
            t1: t1
        )
    }

    public static func ribbonOffset(_ height: Double) -> Double {
        max(8, height * 0.22)
    }

    public static func formatAxisDay(_ unixSeconds: Double) -> String {
        let dt = Date(timeIntervalSince1970: unixSeconds)
        let cal = Calendar.current
        return "\(cal.component(.month, from: dt))月\(cal.component(.day, from: dt))日"
    }

    public static func axisStartLabel(t0: Double, t1: Double) -> String {
        t1 - t0 >= 6 * 86_400 ? SparklineCopy.axisStart : formatAxisDay(t0)
    }

    public static func findReset(
        _ points: [SparkMappedPoint],
        width: Double,
        height: Double,
        t0: Double,
        t1: Double,
        cycleStartTs: Double?
    ) -> SparkMappedPoint? {
        var span = t1 - t0
        if span <= 0 { span = 1 }
        if let cs = cycleStartTs, cs >= t0, cs <= t1 {
            return SparkMappedPoint(x: width * (cs - t0) / span, y: yAt(0, height: height), ts: cs, remaining: 0, index: -1)
        }
        var best = -1
        var bestJump = SparklineCopy.resetJump
        if points.count >= 2 {
            for i in 1..<points.count {
                let jump = points[i].remaining - points[i - 1].remaining
                if jump >= bestJump {
                    bestJump = jump
                    best = i
                }
            }
        }
        return best >= 0 ? points[best] : nil
    }

    public static func hitIndex(_ points: [SparkMappedPoint], x: Double) -> Int? {
        guard !points.isEmpty else { return nil }
        var best = 0
        var bestDist = abs(points[0].x - x)
        for i in 1..<points.count {
            let d = abs(points[i].x - x)
            if d < bestDist {
                bestDist = d
                best = i
            }
        }
        return best
    }

    public static func caption(pointCount: Int, dailyAvg: Double?, current: Double?) -> String {
        if pointCount < 2 { return SparklineCopy.emptyHint }
        if let burn = dailyAvg, burn >= SparklineCopy.flatBurnEpsilon {
            let rate = String(format: "%@%.1f%%", SparklineCopy.minus, burn)
            if let current {
                return "日均约 \(rate) · 当前 \(formatPercent(current))"
            }
            return "日均约 \(rate)"
        }
        if dailyAvg == nil, let current {
            return "当前 \(formatPercent(current))"
        }
        return SparklineCopy.flat
    }

    public static func windowLabel(spanSeconds: Double) -> String {
        let days = spanSeconds / 86_400
        if days >= 6 { return "近 7 日" }
        if days >= 1.5 { return "近 \(max(2, Int(days.rounded()))) 日" }
        return "近 1 日"
    }

    public static func trendSummary(
        points: [HistoryPoint],
        dailyAvg: Double?,
        current: Double?,
        nowTs: Double
    ) -> String {
        guard points.count >= 2 else { return SparklineCopy.emptyHint }
        var first = points[0]
        var lastPt = points[points.count - 1]
        for p in points {
            if p.ts < first.ts { first = p }
            if p.ts > lastPt.ts { lastPt = p }
        }
        let last = current ?? lastPt.remaining
        let t1 = max(nowTs, lastPt.ts)
        let window = windowLabel(spanSeconds: t1 - first.ts)
        let range = "\(formatPercent(first.remaining)) → \(formatPercent(last))"
        if let burn = dailyAvg, burn >= SparklineCopy.flatBurnEpsilon {
            let rate = String(format: "%@%.1f%%", SparklineCopy.minus, burn)
            return "\(window)  \(range) · 日均约 \(rate)"
        }
        return "\(window)  \(range) · 剩余几乎没变"
    }

    public static func formatPercent(_ remaining: Double) -> String {
        let v = min(100, max(0, remaining))
        let rounded = (v * 10).rounded() / 10
        if abs(rounded - rounded.rounded()) < 0.05 {
            return String(format: "%.0f%%", rounded.rounded())
        }
        return String(format: "%.1f%%", rounded)
    }

    public static func formatLocalStamp(_ unixSeconds: Double) -> String {
        let dt = Date(timeIntervalSince1970: unixSeconds)
        let cal = Calendar.current
        let m = cal.component(.month, from: dt)
        let d = cal.component(.day, from: dt)
        let hour = cal.component(.hour, from: dt)
        let minute = cal.component(.minute, from: dt)
        return String(format: "%d月%d日 %02d:%02d", m, d, hour, minute)
    }

    public static func formatHover(ts: Double, remaining: Double, previous: Double? = nil) -> String {
        var text = "\(formatLocalStamp(ts)) · 剩余 \(String(format: "%.1f%%", remaining))"
        if let previous {
            let delta = remaining - previous
            if abs(delta) >= 0.05 {
                let sign = delta > 0 ? "+" : SparklineCopy.minus
                text += "（\(sign)\(String(format: "%.1f%%", abs(delta)))）"
            }
        }
        return text
    }
}
