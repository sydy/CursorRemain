import Foundation

/// Usage-report chart metrics shared with the Windows <c>UsageChartLayout</c>.
public enum UsageChartLayout {
    public static let legendChipHSpacing = 8.0
    public static let legendChipVSpacing = 6.0

    public struct ChipFrame: Equatable, Sendable {
        public var x: Double
        public var y: Double
        public var width: Double
        public var height: Double

        public init(x: Double, y: Double, width: Double, height: Double) {
            self.x = x
            self.y = y
            self.width = width
            self.height = height
        }
    }

    /// Pack chips left-to-right at their own width and wrap when the next chip
    /// does not fit. Unlike equal-width grid columns, a long label keeps its size.
    public static func wrapChips(
        sizes: [(width: Double, height: Double)],
        containerWidth: Double,
        hSpacing: Double = legendChipHSpacing,
        vSpacing: Double = legendChipVSpacing
    ) -> (width: Double, height: Double, frames: [ChipFrame]) {
        let limit = containerWidth > 0 && containerWidth.isFinite ? containerWidth : .infinity
        var frames: [ChipFrame] = []
        var x = 0.0
        var y = 0.0
        var rowH = 0.0
        var maxX = 0.0
        for size in sizes {
            let w = max(0, size.width)
            let h = max(0, size.height)
            if x > 0 && x + w > limit {
                maxX = max(maxX, x - hSpacing)
                x = 0
                y += rowH + vSpacing
                rowH = 0
            }
            frames.append(ChipFrame(x: x, y: y, width: w, height: h))
            x += w + hSpacing
            rowH = max(rowH, h)
        }
        maxX = max(maxX, max(0, x - hSpacing))
        return (maxX, y + rowH, frames)
    }
}
