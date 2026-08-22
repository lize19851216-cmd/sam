using SAM.Core.Tasks;
using SAM.Infrastructure.Data;
using SAM.PluginHost;
using SAM.Infrastructure.Steam;
using SAM.Infrastructure.Logging;
using SAM.Core.Steam;
using Xunit;

namespace SAM.Core.Tests;

public sealed class TaskCenterTests
{
    [Fact]
    public async Task SteamKit_adapter_maps_sanitized_transport_result_without_credentials()
    {
        var account = new SAM.Core.Account { AccountName = "mock_0001" };
        var client = new SteamKitClientAdapter(new StubSteamTransport());
        var result = await client.LoginAsync(account, CancellationToken.None);
        Assert.Equal(SAM.Core.AccountStatus.Online, result.Status);
        Assert.Equal("76561190000000001", account.SteamId);
    }

    [Fact]
    public void Steam_client_factory_defaults_to_fake_and_requires_explicit_transport()
    {
        var factory = new SteamClientFactory();
        Assert.IsType<SAM.Core.FakeSteamClient>(factory.Create(SteamClientMode.Fake));
        Assert.Throws<InvalidOperationException>(() => factory.Create(SteamClientMode.SteamKit));
        Assert.IsType<SAM.Core.FakeSteamClient>(factory.Create(new SteamClientOptions(SteamClientMode.SteamKit)));
        Assert.Throws<InvalidOperationException>(() => factory.Create(new SteamClientOptions(SteamClientMode.SteamKit, EnableSteamKit: true)));
    }
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
    public async Task Task_center_publishes_immutable_state_updates()
    {
        var center = new SamTaskCenter();
        var updates = new List<SamTaskUpdate>();
        center.TaskChanged += (_, update) => updates.Add(update);
        var attempts = 0;

        await center.ExecuteAsync(new SamTaskRecord { TaskType = "Test" }, _ =>
        {
            attempts++;
            return Task.FromResult(attempts == 1 ? SamTaskOutcome.Failed("temporary", true) : SamTaskOutcome.Succeeded("done"));
        }, new RetryPolicy(1, TimeSpan.Zero, TimeSpan.FromSeconds(1)));

        Assert.Equal([SamTaskStatus.Running, SamTaskStatus.RetryWaiting, SamTaskStatus.Running, SamTaskStatus.Succeeded], updates.Select(update => update.Status));
        Assert.Equal("temporary", updates[1].Message);
        Assert.Equal("done", updates[^1].Message);
    }

    [Fact]
    public async Task Task_center_isolates_failing_state_observers()
    {
        var center = new SamTaskCenter();
        var receivedUpdates = 0;
        center.TaskChanged += (_, _) => throw new InvalidOperationException("observer failed");
        center.TaskChanged += (_, _) => receivedUpdates++;

        var result = await center.ExecuteAsync(new SamTaskRecord { TaskType = "Test" }, _ => Task.FromResult(SamTaskOutcome.Succeeded("done")));

        Assert.Equal(SamTaskStatus.Succeeded, result.Status);
        Assert.Equal(2, receivedUpdates);
    }

    [Fact]
    public void Structured_logger_writes_named_task_properties()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"sam-log-{Guid.NewGuid():N}");
        var taskId = Guid.NewGuid();
        try
        {
            var logger = SamLog.Create(directory);
            logger.ForContext("TaskId", taskId)
                .ForContext("TaskStatus", SamTaskStatus.Running)
                .Information("Task state changed");
            (logger as IDisposable)?.Dispose();

            var logFile = Assert.Single(Directory.GetFiles(directory, "sam-*.log"));
            var content = File.ReadAllText(logFile);
            Assert.Contains("TaskId", content);
            Assert.Contains(taskId.ToString(), content);
            Assert.Contains("TaskStatus", content);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
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
    public async Task Sqlite_store_pages_task_history_in_descending_update_order()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sam-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteTaskStore(path);
            await store.InitializeAsync();
            var baseTime = DateTimeOffset.UtcNow;
            var tasks = Enumerable.Range(1, 5).Select(index => new SamTaskRecord
            {
                TaskType = $"Task-{index}",
                Status = SamTaskStatus.Succeeded,
                UpdatedAt = baseTime.AddMinutes(index)
            }).ToArray();
            foreach (var task in tasks) await store.SaveAsync(task);

            var page = await store.GetPageAsync(1, 2);
            Assert.Equal(["Task-4", "Task-3"], page.Select(task => task.TaskType));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Account_database_round_trips_generated_account()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sam-{Guid.NewGuid():N}.db");
        try
        {
            var database = new SamDatabase(path);
            await database.InitializeAsync();
            var account = new SAM.Core.Account { AccountName = "mock_0001", SteamId = "76561190000000001", Status = SAM.Core.AccountStatus.Online };
            await database.SaveAccountAsync(account);
            var restored = Assert.Single(await database.GetAccountsAsync());
            Assert.Equal(account.Id, restored.Id);
            Assert.Equal(SAM.Core.AccountStatus.Online, restored.Status);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Account_database_replaces_previous_simulated_account_snapshot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sam-{Guid.NewGuid():N}.db");
        try
        {
            var database = new SamDatabase(path);
            await database.InitializeAsync();
            await database.ReplaceAccountsAsync([
                new SAM.Core.Account { AccountName = "old_0001" },
                new SAM.Core.Account { AccountName = "old_0002" }
            ]);

            var current = new SAM.Core.Account { AccountName = "mock_0001", SteamId = "76561190000000001" };
            await database.ReplaceAccountsAsync([current]);

            var restored = Assert.Single(await database.GetAccountsAsync());
            Assert.Equal(current.Id, restored.Id);
            Assert.Equal("mock_0001", restored.AccountName);
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

    [Fact]
    public void Plugin_trust_policy_is_default_deny_and_accepts_manifest_hash()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"sam-plugin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var assemblyPath = Path.Combine(directory, "sample.dll");
        try
        {
            File.WriteAllText(assemblyPath, "plugin payload");
            Assert.False(PluginTrustPolicy.FromManifest(directory).IsTrusted(assemblyPath));

            File.WriteAllText(Path.Combine(directory, PluginTrustPolicy.ManifestFileName), PluginTrustPolicy.CalculateHash(assemblyPath));
            Assert.True(PluginTrustPolicy.FromManifest(directory).IsTrusted(assemblyPath));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Plugin_isolation_request_requires_a_sha256_hash()
    {
        new PluginIsolationRequest("plugin.dll", new string('A', 64)).Validate();
        Assert.Throws<ArgumentException>(() => new PluginIsolationRequest("plugin.dll", "invalid").Validate());
    }

    [Fact]
    public void Plugin_runtime_stops_plugins_in_reverse_order_and_only_once()
    {
        var lifecycle = new List<string>();
        var runtime = new PluginRuntime([new LifecyclePlugin("first", lifecycle), new LifecyclePlugin("second", lifecycle)]);

        var report = runtime.Stop();
        Assert.Equal(["second", "first"], report.StoppedPluginIds);
        Assert.Equal(["shutdown:second", "dispose:second", "shutdown:first", "dispose:first"], lifecycle);
        Assert.Empty(runtime.Stop().StoppedPluginIds);
    }

    [Fact]
    public void Plugin_runtime_disposes_remaining_resources_after_shutdown_failure()
    {
        var lifecycle = new List<string>();
        var report = new PluginRuntime([new FailingLifecyclePlugin(lifecycle)]).Stop();

        Assert.Single(report.Failures);
        Assert.Equal(["shutdown", "dispose"], lifecycle);
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

    private sealed class LifecyclePlugin(string id, List<string> lifecycle) : SAM.Core.Plugins.ISamPlugin, IDisposable
    {
        public string Id => id;
        public string Name => id;
        public Version Version => new(1, 0);
        public void Initialize() { }
        public void Shutdown() => lifecycle.Add($"shutdown:{id}");
        public void Dispose() => lifecycle.Add($"dispose:{id}");
    }

    private sealed class FailingLifecyclePlugin(List<string> lifecycle) : SAM.Core.Plugins.ISamPlugin, IDisposable
    {
        public string Id => "failing";
        public string Name => Id;
        public Version Version => new(1, 0);
        public void Initialize() { }
        public void Shutdown()
        {
            lifecycle.Add("shutdown");
            throw new InvalidOperationException("shutdown failed");
        }
        public void Dispose() => lifecycle.Add("dispose");
    }
    private sealed class StubSteamTransport : ISteamAuthenticationTransport
    {
        public Task<SteamAuthenticationResult> AuthenticateAsync(string accountName, CancellationToken cancellationToken) =>
            Task.FromResult(new SteamAuthenticationResult(SteamAuthenticationStatus.Online, "connected", "76561190000000001", "mock"));
    }
}
