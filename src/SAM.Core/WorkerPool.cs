using System.Collections.Concurrent;

namespace SAM.Core;

public sealed class WorkerPool
{
    private readonly ISteamClientService _steam;
    public WorkerPool(ISteamClientService steam) => _steam = steam;

    public async Task RunLoginBatchAsync(
        IEnumerable<Account> accounts,
        int concurrency,
        Action<Account>? changed = null,
        CancellationToken cancellationToken = default)
    {
        concurrency = Math.Clamp(concurrency, 1, 200);
        using var gate = new SemaphoreSlim(concurrency);

        var tasks = accounts.Select(async account =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                account.Status = AccountStatus.Connecting;
                account.LastMessage = "Worker 已领取任务";
                changed?.Invoke(account);

                var result = await _steam.LoginAsync(account, cancellationToken);
                account.Status = result.Status;
                account.LastMessage = result.Message;
                changed?.Invoke(account);
            }
            finally { gate.Release(); }
        });

        await Task.WhenAll(tasks);
    }
}
