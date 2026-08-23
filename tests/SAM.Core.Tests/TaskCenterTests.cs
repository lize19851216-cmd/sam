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
    public async Task Sqlite_store_orders_task_history_by_instant_across_time_zones()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sam-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteTaskStore(path);
            await store.InitializeAsync();
            var oldest = new SamTaskRecord
            {
                TaskType = "oldest",
                UpdatedAt = new DateTimeOffset(2026, 8, 23, 20, 0, 0, TimeSpan.FromHours(8))
            };
            var newest = new SamTaskRecord
            {
                TaskType = "newest",
                UpdatedAt = new DateTimeOffset(2026, 8, 23, 5, 0, 0, TimeSpan.FromHours(-8))
            };
            await store.SaveAsync(oldest);
            await store.SaveAsync(newest);

            var history = await store.GetPageAsync(0, 2);

            Assert.Equal(["newest", "oldest"], history.Select(task => task.TaskType));
            Assert.All(history, task => Assert.Equal(TimeSpan.Zero, task.UpdatedAt.Offset));
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
    public async Task Sqlite_store_cursor_pagination_is_not_shifted_by_newer_tasks()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sam-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteTaskStore(path);
            await store.InitializeAsync();
            var baseTime = DateTimeOffset.UtcNow;
            var oldest = new SamTaskRecord { TaskType = "oldest", UpdatedAt = baseTime.AddMinutes(1) };
            var middle = new SamTaskRecord { TaskType = "middle", UpdatedAt = baseTime.AddMinutes(2) };
            var newest = new SamTaskRecord { TaskType = "newest", UpdatedAt = baseTime.AddMinutes(3) };
            foreach (var task in new[] { oldest, middle, newest }) await store.SaveAsync(task);

            var firstPage = await store.GetPageAfterAsync(null, 2);
            await store.SaveAsync(new SamTaskRecord { TaskType = "arrived-later", UpdatedAt = baseTime.AddMinutes(4) });
            var secondPage = await store.GetPageAfterAsync(firstPage.NextCursor, 2);

            Assert.Equal(["newest", "middle"], firstPage.Tasks.Select(task => task.TaskType));
            Assert.Equal(["oldest"], secondPage.Tasks.Select(task => task.TaskType));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Plugin_trust_policy_can_safely_skip_an_unreadable_diagnostic_hash()
    {
        var missingAssembly = Path.Combine(Path.GetTempPath(), $"sam-missing-{Guid.NewGuid():N}.dll");

        var calculated = PluginTrustPolicy.TryCalculateHash(missingAssembly, out var hash);

        Assert.False(calculated);
        Assert.Empty(hash);
    }

    [Fact]
    public async Task Sqlite_task_store_persists_concurrent_worker_writes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sam-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteTaskStore(path);
            await store.InitializeAsync();
            var tasks = Enumerable.Range(1, 32).Select(index => new SamTaskRecord
            {
                AccountId = Guid.NewGuid(),
                TaskType = $"Login-{index}",
                Status = SamTaskStatus.Succeeded,
                Message = "done"
            }).ToArray();

            await Task.WhenAll(tasks.Select(task => store.SaveAsync(task)));

            var persisted = await store.GetPageAsync(0, tasks.Length);
            Assert.Equal(tasks.Length, persisted.Count);
            Assert.Equal(tasks.Select(task => task.Id).Order(), persisted.Select(task => task.Id).Order());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Sqlite_task_store_prunes_only_expired_terminal_history()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sam-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteTaskStore(path);
            await store.InitializeAsync();
            var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
            var expiredSucceeded = new SamTaskRecord { TaskType = "expired-success", Status = SamTaskStatus.Succeeded, CompletedAt = cutoff.AddTicks(-1) };
            var expiredFailed = new SamTaskRecord { TaskType = "expired-failure", Status = SamTaskStatus.Failed, CompletedAt = cutoff.AddTicks(-1).ToOffset(TimeSpan.FromHours(8)) };
            var expiredCancelled = new SamTaskRecord { TaskType = "expired-cancelled", Status = SamTaskStatus.Cancelled, CompletedAt = cutoff.AddTicks(-1) };
            var recentSucceeded = new SamTaskRecord { TaskType = "recent-success", Status = SamTaskStatus.Succeeded, CompletedAt = cutoff.AddTicks(1) };
            var active = new SamTaskRecord { TaskType = "active", Status = SamTaskStatus.Running };
            var retrying = new SamTaskRecord { TaskType = "retrying", Status = SamTaskStatus.RetryWaiting };
            var tasks = new[] { expiredSucceeded, expiredFailed, expiredCancelled, recentSucceeded, active, retrying };
            foreach (var task in tasks) await store.SaveAsync(task);

            var deleted = await store.PruneTerminalTasksAsync(cutoff);
            var remaining = await store.GetPageAsync(0, 10);

            Assert.Equal(3, deleted);
            Assert.Equal(new[] { recentSucceeded.Id, active.Id, retrying.Id }.Order(), remaining.Select(task => task.Id).Order());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Plugin_isolation_endpoint_rejects_unsafe_pipe_names()
    {
        Assert.Equal("sam-plugin_01", PluginIsolationEndpoint.ValidatePipeName("sam-plugin_01"));
        Assert.Throws<ArgumentException>(() => PluginIsolationEndpoint.ValidatePipeName("sam/plugin"));
        Assert.Throws<ArgumentException>(() => new NamedPipePluginIsolationHost("sam plugin"));
        Assert.Throws<ArgumentOutOfRangeException>(() => PluginIsolationEndpoint.ValidatePipeName(new string('a', PluginIsolationEndpoint.MaximumPipeNameLength + 1)));
    }

    [Fact]
    public async Task Named_pipe_isolation_host_times_out_when_no_local_host_is_available()
    {
        var host = new NamedPipePluginIsolationHost($"sam-missing-{Guid.NewGuid():N}", TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            host.InspectAsync(new PluginIsolationRequest("plugin.dll", new string('A', 64))));
    }

    [Fact]
    public void Named_pipe_isolation_host_rejects_non_positive_operation_timeout()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NamedPipePluginIsolationHost("sam-plugin", TimeSpan.Zero));
    }

    [Fact]
    public void Plugin_isolation_policy_rejects_untrusted_assemblies_before_execution()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"sam-plugin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var assemblyPath = Path.Combine(directory, "unreviewed.dll");
        try
        {
            File.WriteAllText(assemblyPath, "unreviewed plugin payload");

            var decision = new PluginIsolationPolicy().Decide(assemblyPath, PluginTrustPolicy.FromManifest(directory));

            Assert.False(decision.CanExecute);
            Assert.Equal(PluginExecutionMode.Rejected, decision.Mode);
            Assert.Contains("cannot execute in-process", decision.Message);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Plugin_isolation_policy_allows_only_reviewed_assemblies_in_process()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"sam-plugin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var assemblyPath = Path.Combine(directory, "reviewed.dll");
        try
        {
            File.WriteAllText(assemblyPath, "reviewed plugin payload");
            File.WriteAllText(Path.Combine(directory, PluginTrustPolicy.ManifestFileName), PluginTrustPolicy.CalculateHash(assemblyPath));

            var decision = new PluginIsolationPolicy().Decide(assemblyPath, PluginTrustPolicy.FromManifest(directory));

            Assert.True(decision.CanExecute);
            Assert.Equal(PluginExecutionMode.TrustedInProcess, decision.Mode);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Named_pipe_isolation_host_round_trips_metadata_only()
    {
        var pipeName = $"sam-test-{Guid.NewGuid():N}";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var expected = new PluginIsolationResult(true, "accepted", [new PluginMetadata("sample", "Sample", "1.0")]);
        var server = NamedPipePluginIsolationHost.ServeOnceAsync(pipeName, (request, _) =>
        {
            Assert.Equal("plugin.dll", request.AssemblyPath);
            return Task.FromResult(expected);
        }, timeout.Token);

        var result = await new NamedPipePluginIsolationHost(pipeName).InspectAsync(new PluginIsolationRequest("plugin.dll", new string('A', 64)), timeout.Token);
        await server;
        Assert.Equal(expected.Accepted, result.Accepted);
        Assert.Equal(expected.Message, result.Message);
        Assert.Equal(expected.Plugins, result.Plugins);
    }

    [Fact]
    public async Task Named_pipe_isolation_host_rejects_oversized_response_before_writing()
    {
        var pipeName = $"sam-test-{Guid.NewGuid():N}";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var server = NamedPipePluginIsolationHost.ServeOnceAsync(pipeName, (_, _) =>
            Task.FromResult(new PluginIsolationResult(true, "accepted", [new PluginMetadata("sample", new string('x', 1_048_576), "1.0")])), timeout.Token);

        await Assert.ThrowsAnyAsync<IOException>(() =>
            new NamedPipePluginIsolationHost(pipeName).InspectAsync(new PluginIsolationRequest("plugin.dll", new string('A', 64)), timeout.Token));
        await Assert.ThrowsAsync<InvalidDataException>(() => server);
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
