import Foundation

public enum RefreshGeneration {
    public static func shouldApply(
        outcomeId: String,
        activeId: String,
        outcomeGeneration: Int,
        currentGeneration: Int
    ) -> Bool {
        outcomeId == activeId && outcomeGeneration == currentGeneration
    }
}
