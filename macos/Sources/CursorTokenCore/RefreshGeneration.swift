import Foundation

public enum RefreshGeneration {
    public static let accountRefreshLimit = 2

    public static func prioritize<T>(_ items: [T], firstWhere: (T) -> Bool) -> [T] {
        items.enumerated().sorted { lhs, rhs in
            let left = firstWhere(lhs.element)
            let right = firstWhere(rhs.element)
            if left != right { return left && !right }
            return lhs.offset < rhs.offset
        }.map(\.element)
    }

    public static func shouldApply(
        outcomeId: String,
        activeId: String,
        outcomeGeneration: Int,
        currentGeneration: Int
    ) -> Bool {
        outcomeId == activeId && outcomeGeneration == currentGeneration
    }

    /// Keep at most `limit` account fetches in flight so a large account list does not 429.
    public static func mapBounded<T: Sendable, R: Sendable>(
        _ items: [T],
        limit: Int = accountRefreshLimit,
        operation: @escaping @Sendable (T) async -> R
    ) async -> [R] {
        if items.isEmpty { return [] }
        let cap = max(1, limit)
        return await withTaskGroup(of: (Int, R).self, returning: [R].self) { group in
            var results = [R?](repeating: nil, count: items.count)
            var next = 0
            func enqueue() {
                guard next < items.count else { return }
                let index = next
                let item = items[index]
                next += 1
                group.addTask {
                    let value = await operation(item)
                    return (index, value)
                }
            }
            for _ in 0..<min(cap, items.count) { enqueue() }
            for await (index, value) in group {
                results[index] = value
                enqueue()
            }
            return results.map { $0! }
        }
    }
}
