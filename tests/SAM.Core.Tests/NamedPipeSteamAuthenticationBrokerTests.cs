using SAM.Core.Steam;
using SAM.Infrastructure.Steam;
using Xunit;

namespace SAM.Core.Tests;

public sealed class NamedPipeSteamAuthenticationBrokerTests
{
    [Fact]
    public void Default_timeout_allows_manual_authentication_without_extending_credential_free_probes()
    {
        Assert.Equal(TimeSpan.FromMinutes(3), NamedPipeSteamAuthenticationBroker.DefaultAuthenticationTimeout);
        Assert.Equal(TimeSpan.FromSeconds(2), NamedPipeSteamAuthenticationBroker.DefaultProbeTimeout);
    }

    [Fact]
    public async Task Broker_round_trip_exchanges_only_a_secret_free_request_and_sanitized_response()
    {
        var pipeName = $"sam-steam-broker-{Guid.NewGuid():N}";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var server = NamedPipeSteamAuthenticationBroker.ServeOnceAsync(pipeName, (request, _) =>
        {
            Assert.Equal("test_account_0001", request.AccountName);
            return Task.FromResult(new SteamAuthenticationBrokerResponse(SteamAuthenticationStatus.Online, "76561190000000001", "mock"));
        }, timeout.Token);

        var result = await new NamedPipeSteamAuthenticationBroker(pipeName).AuthenticateAsync("test_account_0001", timeout.Token);
        await server;

        Assert.Equal(SteamAuthenticationStatus.Online, result.Status);
        Assert.Equal("Steam authentication succeeded.", result.Message);
        Assert.Equal("76561190000000001", result.SteamId);
        Assert.Equal("mock", result.PersonaName);
    }

    [Fact]
    public async Task Unavailable_broker_returns_a_sanitized_failure()
    {
        var result = await new NamedPipeSteamAuthenticationBroker($"sam-steam-broker-{Guid.NewGuid():N}", TimeSpan.FromMilliseconds(100)).AuthenticateAsync("test_account_0001", CancellationToken.None);

        Assert.Equal(SteamAuthenticationStatus.Failed, result.Status);
        Assert.Equal("Steam authentication broker is unavailable.", result.Message);
    }

    [Fact]
    public async Task Broker_preserves_a_sanitized_invalid_Steam_Guard_code_outcome()
    {
        var pipeName = $"sam-steam-broker-{Guid.NewGuid():N}";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var server = NamedPipeSteamAuthenticationBroker.ServeOnceAsync(pipeName, (_, _) =>
            Task.FromResult(new SteamAuthenticationBrokerResponse(SteamAuthenticationStatus.InvalidSteamGuardCode)), timeout.Token);

        var result = await new NamedPipeSteamAuthenticationBroker(pipeName).AuthenticateAsync("test_account_0001", timeout.Token);
        await server;

        Assert.Equal(SteamAuthenticationStatus.InvalidSteamGuardCode, result.Status);
        Assert.Equal("Steam Guard code was rejected or expired.", result.Message);
    }

    [Fact]
    public async Task Caller_requested_cancellation_is_not_replaced_by_the_broker_timeout()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new NamedPipeSteamAuthenticationBroker($"sam-steam-broker-{Guid.NewGuid():N}", TimeSpan.FromSeconds(5)).AuthenticateAsync("test_account_0001", cancellation.Token));
    }

    [Fact]
    public async Task Credential_free_probe_round_trip_does_not_submit_an_account_name()
    {
        var pipeName = $"sam-steam-broker-{Guid.NewGuid():N}";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var server = NamedPipeSteamAuthenticationBroker.ServeOnceAsync(pipeName, (request, _) =>
        {
            Assert.Equal(SteamAuthenticationBrokerRequestKind.Probe, request.Kind);
            Assert.Equal(string.Empty, request.AccountName);
            return Task.FromResult(new SteamAuthenticationBrokerResponse(SteamAuthenticationStatus.Failed));
        }, timeout.Token);

        var connected = await new NamedPipeSteamAuthenticationBroker(pipeName).ProbeAsync(timeout.Token);
        await server;

        Assert.True(connected);
    }

    [Fact]
    public void Broker_endpoint_rejects_unsafe_pipe_names_and_broker_models_reject_invalid_data()
    {
        Assert.Throws<ArgumentException>(() => new NamedPipeSteamAuthenticationBroker("sam/steam"));
        Assert.Throws<ArgumentException>(() => new SteamAuthenticationBrokerRequest("bad\nname").Validate());
        Assert.Throws<ArgumentException>(() => new SteamAuthenticationBrokerRequest(" account_with_padding ").Validate());
        Assert.Throws<ArgumentException>(() => new SteamAuthenticationBrokerRequest("mock_0001").Validate());
        Assert.Throws<ArgumentException>(() => new SteamAuthenticationBrokerRequest("MOCK_0001").Validate());
        Assert.Throws<ArgumentException>(() => new SteamAuthenticationBrokerRequest("mock_0001", SteamAuthenticationBrokerRequestKind.Probe).Validate());
        Assert.Throws<ArgumentException>(() => new SteamAuthenticationBrokerResponse(SteamAuthenticationStatus.Online, "not-a-steam-id").Validate());
        Assert.Throws<ArgumentException>(() => new SteamAuthenticationBrokerResponse(SteamAuthenticationStatus.InvalidCredentials, "76561190000000001", "unnecessary").Validate());
    }
}
