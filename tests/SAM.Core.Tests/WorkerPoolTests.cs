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
}
