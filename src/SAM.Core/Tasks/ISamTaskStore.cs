namespace SAM.Core.Tasks;

public interface ISamTaskStore
{
    Task SaveAsync(SamTaskRecord task, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SamTaskRecord>> GetRecentAsync(int limit, CancellationToken cancellationToken = default);
}
