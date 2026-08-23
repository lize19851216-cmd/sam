using SAM.Core;
using SAM.Core.Steam;
using SAM.Core.Tasks;
using SAM.Infrastructure.Steam;
using Xunit;

namespace SAM.Core.Tests;

public sealed class ExternalSteamBrokerEndToEndTests
{
    [Fact]
    public async Task Explicit_external_broker_path_completes_a_login_without_credentials_in_sam()
    {
        var account = new Account { AccountName = "mock_0001" };
        var pipeName = $"sam-e2e-{Guid.NewGuid():N}";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var server = new SteamAuthenticationBrokerHost(new StubTransport(new SteamAuthenticationResult(
            SteamAuthenticationStatus.Online,
            "internal broker detail",
            "76561190000000001",
            "mock"))).ServeOnceAsync(pipeName, timeout.Token);
        var center = new SamTaskCenter();
        var statuses = new List<SamTaskStatus>();
        center.TaskChanged += (_, update) => statuses.Add(update.Status);
        var client = new SteamClientFactory().CreateWithExternalBroker(new SteamClientOptions(SteamClientMode.SteamKit, EnableSteamKit: true), pipeName);

        await new WorkerPool(client).RunLoginBatchAsync([account], 1, retryPolicy: new RetryPolicy(0, Timeout: TimeSpan.FromSeconds(2)), taskCenter: center);
        await server;

        Assert.Equal(AccountStatus.Online, account.Status);
        Assert.Equal("76561190000000001", account.SteamId);
        Assert.Equal("mock", account.PersonaName);
        Assert.Equal("Steam authentication succeeded.", account.LastMessage);
        Assert.DoesNotContain("internal broker detail", account.LastMessage);
        Assert.Contains(SamTaskStatus.Succeeded, statuses);
    }

    [Fact]
    public async Task External_broker_steam_guard_result_remains_non_secret_and_non_retryable()
    {
        var account = new Account { AccountName = "mock_0002" };
        var pipeName = $"sam-e2e-{Guid.NewGuid():N}";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var server = new SteamAuthenticationBrokerHost(new StubTransport(new SteamAuthenticationResult(
            SteamAuthenticationStatus.RequiresSteamGuard,
            "one-time code detail"))).ServeOnceAsync(pipeName, timeout.Token);
        var client = new SteamClientFactory().CreateWithExternalBroker(new SteamClientOptions(SteamClientMode.SteamKit, EnableSteamKit: true), pipeName);

        await new WorkerPool(client).RunLoginBatchAsync([account], 1, retryPolicy: new RetryPolicy(0, Timeout: TimeSpan.FromSeconds(2)));
        await server;

        Assert.Equal(AccountStatus.RequiresSteamGuard, account.Status);
        Assert.Equal("Steam Guard verification is required.", account.LastMessage);
        Assert.DoesNotContain("one-time code detail", account.LastMessage);
    }

    private sealed class StubTransport(SteamAuthenticationResult result) : ISteamAuthenticationTransport
    {
        public Task<SteamAuthenticationResult> AuthenticateAsync(string accountName, CancellationToken cancellationToken) => Task.FromResult(result);
    }
}
