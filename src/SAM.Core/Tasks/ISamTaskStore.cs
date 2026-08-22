namespace SAM.Core.Tasks;

public interface ISamTaskStore
{
    Task SaveAsync(SamTaskRecord task, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SamTaskRecord>> GetRecentAsync(int limit, CancellationToken cancellationToken = default);

    async Task<IReadOnlyList<SamTaskRecord>> GetPageAsync(int offset, int limit, CancellationToken cancellationToken = default)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit <= 0) return [];
        return (await GetRecentAsync(checked(offset + limit), cancellationToken)).Skip(offset).ToArray();
    }
}
