using SAM.Core.Tasks;
using SAM.Infrastructure.Data;
using SAM.PluginHost;
using Xunit;

namespace SAM.Core.Tests;

public sealed class TaskCenterTests
{
    [Fact]
    public async Task Retries_transient_failure_and_persists_terminal_state()
    {
        var store = new MemoryTaskStore();
        var attempts = 0;
        var result = await new SamTaskCenter(store).ExecuteAsync(new SamTaskRecord { TaskType = "Test" }, _ =>
        {
            attempts++;
            return Task.FromResult(attempts == 1 ? SamTaskOutcome.Failed("temporary", true) : SamTaskOutcome.Succeeded("done"));
        }, new RetryPolicy(2, TimeSpan.Zero, TimeSpan.FromSeconds(1)));

        Assert.Equal(2, attempts);
        Assert.Equal(1, result.RetryCount);
        Assert.Equal(SamTaskStatus.Succeeded, result.Status);
        Assert.Contains(store.Saved, task => task.Status == SamTaskStatus.RetryWaiting);
    }

    [Fact]
    public async Task Cancellation_is_a_terminal_task_state()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await new SamTaskCenter().ExecuteAsync(new SamTaskRecord { TaskType = "Test" }, _ => Task.FromResult(SamTaskOutcome.Succeeded()), cancellationToken: cancellation.Token);
        Assert.Equal(SamTaskStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task Timeout_retries_then_fails()
    {
        var result = await new SamTaskCenter().ExecuteAsync(new SamTaskRecord { TaskType = "Test" }, async token =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            return SamTaskOutcome.Succeeded();
        }, new RetryPolicy(1, TimeSpan.Zero, TimeSpan.FromMilliseconds(20)));
        Assert.Equal(SamTaskStatus.Failed, result.Status);
        Assert.Equal(1, result.RetryCount);
        Assert.Equal("Timed out", result.Message);
    }

    [Fact]
    public async Task Sqlite_store_round_trips_task()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sam-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteTaskStore(path);
            await store.InitializeAsync();
            var task = new SamTaskRecord { AccountId = Guid.NewGuid(), TaskType = "Login", Status = SamTaskStatus.Succeeded, Message = "done", CompletedAt = DateTimeOffset.UtcNow };
            await store.SaveAsync(task);
            var restored = Assert.Single(await store.GetRecentAsync(10));
            Assert.Equal(task.Id, restored.Id);
            Assert.Equal(SamTaskStatus.Succeeded, restored.Status);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Plugin_registry_rejects_duplicate_ids()
    {
        var registry = new PluginRegistry();
        registry.Register(new TestPlugin("sample"));
        Assert.Throws<InvalidOperationException>(() => registry.Register(new TestPlugin("sample")));
    }

    private sealed class MemoryTaskStore : ISamTaskStore
    {
        public List<SamTaskRecord> Saved { get; } = [];
        public Task SaveAsync(SamTaskRecord task, CancellationToken cancellationToken = default) { Saved.Add(new SamTaskRecord { Id = task.Id, Status = task.Status, RetryCount = task.RetryCount, Message = task.Message }); return Task.CompletedTask; }
        public Task<IReadOnlyList<SamTaskRecord>> GetRecentAsync(int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SamTaskRecord>>(Saved);
    }

    private sealed class TestPlugin(string id) : SAM.Core.Plugins.ISamPlugin
    {
        public string Id => id; public string Name => id; public Version Version => new(1, 0); public void Initialize() { }
    }
}
