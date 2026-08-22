using Xunit;
using SAM.Core;
using SAM.Core.Tasks;
namespace SAM.Core.Tests;

public sealed class WorkerPoolTests
{
    [Fact]
    public async Task Batch_completes_all_accounts()
    {
        var accounts = Enumerable.Range(1, 100)
            .Select(i => new Account { AccountName = $"mock_{i}" }).ToArray();
        var pool = new WorkerPool(new FakeSteamClient());
        await pool.RunLoginBatchAsync(accounts, 10);
        Assert.DoesNotContain(accounts, a => a.Status is AccountStatus.Imported or AccountStatus.Connecting);
    }

    [Fact]
    public async Task Batch_caps_requested_concurrency_at_10()
    {
        var client = new TrackingSteamClient();
        var accounts = Enumerable.Range(1, 20).Select(i => new Account { AccountName = $"mock_{i}" }).ToArray();

        await new WorkerPool(client).RunLoginBatchAsync(accounts, 1_000, retryPolicy: new SAM.Core.Tasks.RetryPolicy(0, Timeout: TimeSpan.FromSeconds(2)));

        Assert.All(accounts, account => Assert.Equal(AccountStatus.Online, account.Status));
        Assert.Equal(WorkerPool.MaximumConcurrency, client.PeakConcurrentRequests);
    }

    [Fact]
    public async Task Cancellation_marks_active_and_queued_accounts_cancelled()
    {
        var accounts = Enumerable.Range(1, 100).Select(i => new Account { AccountName = $"mock_{i}" }).ToArray();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        var store = new MemoryTaskStore();

        await new WorkerPool(new BlockingSteamClient()).RunLoginBatchAsync(accounts, 1, cancellationToken: cancellation.Token, taskCenter: new SamTaskCenter(store));

        Assert.All(accounts, account => Assert.Equal(AccountStatus.Cancelled, account.Status));
        Assert.Equal(accounts.Length, store.Saved.Where(task => task.Status == SamTaskStatus.Cancelled).Select(task => task.Id).Distinct().Count());
    }

    private sealed class TrackingSteamClient : ISteamClientService
    {
        private int _active;
        private int _peak;
        public int PeakConcurrentRequests => _peak;
        public async Task<LoginResult> LoginAsync(Account account, CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _active);
            while (true)
            {
                var observed = _peak;
                if (observed >= active || Interlocked.CompareExchange(ref _peak, active, observed) == observed) break;
            }
            try { await Task.Delay(100, cancellationToken); return new(AccountStatus.Online, "success"); }
            finally { Interlocked.Decrement(ref _active); }
        }
    }

    private sealed class BlockingSteamClient : ISteamClientService
    {
        public async Task<LoginResult> LoginAsync(Account account, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return new(AccountStatus.Online, "success");
        }
    }

    private sealed class MemoryTaskStore : ISamTaskStore
    {
        public System.Collections.Concurrent.ConcurrentBag<SamTaskRecord> Saved { get; } = [];
        public Task SaveAsync(SamTaskRecord task, CancellationToken cancellationToken = default)
        {
            Saved.Add(new SamTaskRecord { Id = task.Id, Status = task.Status });
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<SamTaskRecord>> GetRecentAsync(int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SamTaskRecord>>([]);
    }
}

