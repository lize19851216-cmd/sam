using Xunit;
using SAM.Core;
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
    public async Task Batch_processes_1000_simulated_accounts_with_bounded_concurrency()
    {
        var client = new TrackingSteamClient();
        var accounts = Enumerable.Range(1, 1000).Select(i => new Account { AccountName = $"mock_{i}" }).ToArray();

        await new WorkerPool(client).RunLoginBatchAsync(accounts, 25, retryPolicy: new SAM.Core.Tasks.RetryPolicy(0, Timeout: TimeSpan.FromSeconds(2)));

        Assert.All(accounts, account => Assert.Equal(AccountStatus.Online, account.Status));
        Assert.InRange(client.PeakConcurrentRequests, 1, 25);
    }

    [Fact]
    public async Task Cancellation_marks_active_and_queued_accounts_cancelled()
    {
        var accounts = Enumerable.Range(1, 100).Select(i => new Account { AccountName = $"mock_{i}" }).ToArray();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        await new WorkerPool(new BlockingSteamClient()).RunLoginBatchAsync(accounts, 1, cancellationToken: cancellation.Token);

        Assert.All(accounts, account => Assert.Equal(AccountStatus.Cancelled, account.Status));
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
            try { await Task.Delay(2, cancellationToken); return new(AccountStatus.Online, "success"); }
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
}

