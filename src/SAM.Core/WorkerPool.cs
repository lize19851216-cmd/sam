using System.Collections.Concurrent;

using SAM.Core.Tasks;

namespace SAM.Core;

public sealed class WorkerPool
{
    public const int MaximumConcurrency = 10;

    private readonly ISteamClientService _steam;
    public WorkerPool(ISteamClientService steam) => _steam = steam;

    public static int NormalizeConcurrency(int requestedConcurrency) => Math.Clamp(requestedConcurrency, 1, MaximumConcurrency);

    public async Task RunLoginBatchAsync(
        IEnumerable<Account> accounts, int concurrency, Action<Account>? changed = null,
        CancellationToken cancellationToken = default, RetryPolicy? retryPolicy = null, SamTaskCenter? taskCenter = null)
    {
        concurrency = NormalizeConcurrency(concurrency);
        using var gate = new SemaphoreSlim(concurrency);
        var center = taskCenter ?? new SamTaskCenter();
        var tasks = accounts.Select(async account =>
        {
            var entered = false;
            var record = new SamTaskRecord { AccountId = account.Id, TaskType = "Login" };
            try
            {
                await gate.WaitAsync(cancellationToken);
                entered = true;
                account.Status = AccountStatus.Connecting;
                account.LastMessage = "Worker 已领取任务";
                changed?.Invoke(account);
                LoginResult? loginResult = null;
                var completed = await center.ExecuteAsync(record, async token =>
                {
                    loginResult = await _steam.LoginAsync(account, token);
                    var retryable = loginResult.Status is AccountStatus.RateLimited or AccountStatus.Failed;
                    return loginResult.Status == AccountStatus.Online
                        ? SamTaskOutcome.Succeeded(loginResult.Message)
                        : SamTaskOutcome.Failed(loginResult.Message, retryable);
                }, retryPolicy, cancellationToken);
                account.RetryCount = completed.RetryCount;
                account.Status = completed.Status == SamTaskStatus.Cancelled ? AccountStatus.Cancelled : loginResult?.Status ?? AccountStatus.Failed;
                account.LastMessage = completed.Message;
                changed?.Invoke(account);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await center.CancelAsync(record);
                account.Status = AccountStatus.Cancelled;
                account.LastMessage = "Cancelled";
                changed?.Invoke(account);
            }
            finally { if (entered) gate.Release(); }
        });
        await Task.WhenAll(tasks);
    }
}
