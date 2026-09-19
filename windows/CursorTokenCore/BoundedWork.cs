namespace CursorTokenCore;

/// <summary>
/// Run async work with a concurrency cap so multi-account refresh does not stampede the Cursor API.
/// </summary>
public static class BoundedWork
{
    public const int AccountRefreshLimit = 2;

    public static List<T> Prioritize<T>(IEnumerable<T> items, Func<T, bool> isFirst)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(isFirst);
        return items
            .Select((item, index) => (item, index, first: isFirst(item)))
            .OrderBy(row => row.first ? 0 : 1)
            .ThenBy(row => row.index)
            .Select(row => row.item)
            .ToList();
    }

    public static async Task<T[]> MapAsync<TSource, T>(
        IEnumerable<TSource> items,
        Func<TSource, Task<T>> worker,
        int maxConcurrent = AccountRefreshLimit)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(worker);
        var list = items as IList<TSource> ?? items.ToList();
        if (list.Count == 0) return [];
        var cap = Math.Max(1, maxConcurrent);
        using var gate = new SemaphoreSlim(cap, cap);
        var tasks = new Task<T>[list.Count];
        for (var i = 0; i < list.Count; i++)
        {
            var item = list[i];
            tasks[i] = RunOne(gate, worker, item);
        }
        return await Task.WhenAll(tasks);
    }

    static async Task<T> RunOne<TSource, T>(SemaphoreSlim gate, Func<TSource, Task<T>> worker, TSource item)
    {
        await gate.WaitAsync();
        try { return await worker(item); }
        finally { gate.Release(); }
    }
}
