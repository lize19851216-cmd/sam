namespace SAM.Core.Tasks;

public sealed record SamTaskOutcome(SamTaskStatus Status, string Message, bool IsRetryable = false)
{
    public static SamTaskOutcome Succeeded(string message = "Completed") => new(SamTaskStatus.Succeeded, message);
    public static SamTaskOutcome Failed(string message, bool isRetryable = false) => new(SamTaskStatus.Failed, message, isRetryable);
}

public sealed class SamTaskCenter
{
    private readonly ISamTaskStore? _store;
    public SamTaskCenter(ISamTaskStore? store = null) => _store = store;

    /// <summary>Raised after a task state is persisted, with an immutable UI-safe snapshot. Handler failures are isolated from task execution.</summary>
    public event EventHandler<SamTaskUpdate>? TaskChanged;

    public async Task<SamTaskRecord> CancelAsync(SamTaskRecord task, string message = "Cancelled")
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task.Status is SamTaskStatus.Succeeded or SamTaskStatus.Failed or SamTaskStatus.Cancelled) return task;
        await FinishAsync(task, SamTaskStatus.Cancelled, message, CancellationToken.None);
        return task;
    }

    public async Task<SamTaskRecord> ExecuteAsync(SamTaskRecord task, Func<CancellationToken, Task<SamTaskOutcome>> operation, RetryPolicy? policy = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(operation);
        policy ??= new RetryPolicy();
        if (policy.MaxRetries < 0 || policy.EffectiveBaseDelay < TimeSpan.Zero || policy.EffectiveTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(policy));
        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return await CancelAsync(task);
            }
            task.StartedAt ??= DateTimeOffset.UtcNow;
            await SetStateAsync(task, SamTaskStatus.Running, "Running", cancellationToken);
            try
            {
                using var timeout = new CancellationTokenSource(policy.EffectiveTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
                var outcome = await operation(linked.Token);
                if (outcome.Status == SamTaskStatus.Succeeded || !outcome.IsRetryable || task.RetryCount >= policy.MaxRetries)
                {
                    await FinishAsync(task, outcome.Status, outcome.Message, cancellationToken); return task;
                }
                await WaitToRetryAsync(task, outcome.Message, policy, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return await CancelAsync(task);
            }
            catch (OperationCanceledException)
            {
                if (task.RetryCount >= policy.MaxRetries) { await FinishAsync(task, SamTaskStatus.Failed, "Timed out", cancellationToken); return task; }
                await WaitToRetryAsync(task, "Timed out", policy, cancellationToken);
            }
            catch (Exception exception)
            {
                if (task.RetryCount >= policy.MaxRetries) { await FinishAsync(task, SamTaskStatus.Failed, exception.Message, cancellationToken); return task; }
                await WaitToRetryAsync(task, exception.Message, policy, cancellationToken);
            }
        }
    }
    private async Task WaitToRetryAsync(SamTaskRecord task, string message, RetryPolicy policy, CancellationToken cancellationToken)
    {
        task.RetryCount++; await SetStateAsync(task, SamTaskStatus.RetryWaiting, message, cancellationToken);
        await Task.Delay(policy.GetDelay(task.RetryCount - 1), cancellationToken);
    }
    private async Task SetStateAsync(SamTaskRecord task, SamTaskStatus status, string message, CancellationToken cancellationToken)
    {
        task.Status = status; task.Message = message; task.UpdatedAt = DateTimeOffset.UtcNow;
        if (_store is not null) await _store.SaveAsync(task, cancellationToken);
        PublishTaskChanged(SamTaskUpdate.From(task));
    }
    private async Task FinishAsync(SamTaskRecord task, SamTaskStatus status, string message, CancellationToken cancellationToken)
    {
        task.CompletedAt = DateTimeOffset.UtcNow; await SetStateAsync(task, status, message, cancellationToken);
    }

    private void PublishTaskChanged(SamTaskUpdate update)
    {
        var handlers = TaskChanged?.GetInvocationList().Cast<EventHandler<SamTaskUpdate>>() ?? [];
        foreach (var handler in handlers)
        {
            try { handler(this, update); }
            catch { /* Observers must not interfere with task execution. */ }
        }
    }
}

public sealed record SamTaskUpdate(
    Guid Id,
    Guid AccountId,
    string TaskType,
    SamTaskStatus Status,
    int RetryCount,
    string Message,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset UpdatedAt)
{
    public static SamTaskUpdate From(SamTaskRecord task) => new(
        task.Id, task.AccountId, task.TaskType, task.Status, task.RetryCount, task.Message,
        task.CreatedAt, task.StartedAt, task.CompletedAt, task.UpdatedAt);

    public SamTaskRecord ToRecord() => new()
    {
        Id = Id,
        AccountId = AccountId,
        TaskType = TaskType,
        Status = Status,
        RetryCount = RetryCount,
        Message = Message,
        CreatedAt = CreatedAt,
        StartedAt = StartedAt,
        CompletedAt = CompletedAt,
        UpdatedAt = UpdatedAt
    };
}
