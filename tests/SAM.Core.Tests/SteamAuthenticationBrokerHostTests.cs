using SAM.Core.Steam;
using SAM.Infrastructure.Steam;
using Xunit;

namespace SAM.Core.Tests;

public sealed class SteamAuthenticationBrokerHostTests
{
    [Fact]
    public async Task Host_forwards_only_the_sanitized_authentication_outcome()
    {
        var pipeName = $"sam-steam-host-{Guid.NewGuid():N}";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new StubTransport(new SteamAuthenticationResult(SteamAuthenticationStatus.Online, "private detail", "76561190000000001", "mock"));
        var server = new SteamAuthenticationBrokerHost(transport).ServeOnceAsync(pipeName, timeout.Token);

        var result = await new NamedPipeSteamAuthenticationBroker(pipeName).AuthenticateAsync("mock_0001", timeout.Token);
        await server;

        Assert.Equal("mock_0001", transport.AccountName);
        Assert.Equal(SteamAuthenticationStatus.Online, result.Status);
        Assert.Equal("Steam authentication succeeded.", result.Message);
        Assert.DoesNotContain("private detail", result.Message);
    }

    [Fact]
    public async Task Host_converts_transport_exceptions_to_a_secret_free_failure()
    {
        var pipeName = $"sam-steam-host-{Guid.NewGuid():N}";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var server = new SteamAuthenticationBrokerHost(new ThrowingTransport()).ServeOnceAsync(pipeName, timeout.Token);

        var result = await new NamedPipeSteamAuthenticationBroker(pipeName).AuthenticateAsync("mock_0001", timeout.Token);
        await server;

        Assert.Equal(SteamAuthenticationStatus.Failed, result.Status);
        Assert.Equal("Steam authentication was rejected.", result.Message);
    }

    private sealed class StubTransport(SteamAuthenticationResult result) : ISteamAuthenticationTransport
    {
        public string? AccountName { get; private set; }
        public Task<SteamAuthenticationResult> AuthenticateAsync(string accountName, CancellationToken cancellationToken)
        {
            AccountName = accountName;
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingTransport : ISteamAuthenticationTransport
    {
        public Task<SteamAuthenticationResult> AuthenticateAsync(string accountName, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("sensitive external failure");
    }
}
