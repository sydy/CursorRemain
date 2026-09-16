namespace CursorTokenCore;

public static class RefreshGeneration
{
    public static bool ShouldApply(string outcomeId, string activeId, long outcomeGeneration, long currentGeneration) =>
        outcomeId == activeId && outcomeGeneration == currentGeneration;
}
