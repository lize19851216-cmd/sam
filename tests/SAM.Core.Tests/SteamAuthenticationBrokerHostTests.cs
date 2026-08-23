using SAM.Core.Steam;
using SAM.Infrastructure.Steam;
using System.Diagnostics;
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

        var result = await new NamedPipeSteamAuthenticationBroker(pipeName).AuthenticateAsync("test_account_0001", timeout.Token);
        await server;

        Assert.Equal("test_account_0001", transport.AccountName);
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

        var result = await new NamedPipeSteamAuthenticationBroker(pipeName).AuthenticateAsync("test_account_0001", timeout.Token);
        await server;

        Assert.Equal(SteamAuthenticationStatus.Failed, result.Status);
        Assert.Equal("Steam authentication was rejected.", result.Message);
    }

    [Fact]
    public async Task Host_answers_a_probe_without_calling_the_credential_transport()
    {
        var pipeName = $"sam-steam-host-{Guid.NewGuid():N}";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ThrowingTransport();
        var server = new SteamAuthenticationBrokerHost(transport).ServeOnceAsync(pipeName, timeout.Token);

        var connected = await new NamedPipeSteamAuthenticationBroker(pipeName).ProbeAsync(timeout.Token);
        await server;

        Assert.True(connected);
        Assert.False(transport.WasCalled);
    }

    [Fact]
    public async Task Host_continues_serving_multiple_credential_free_probes_until_cancelled()
    {
        var pipeName = $"sam-steam-host-{Guid.NewGuid():N}";
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transport = new ThrowingTransport();
        var server = new SteamAuthenticationBrokerHost(transport).ServeUntilCancelledAsync(pipeName, cancellation.Token);
        var broker = new NamedPipeSteamAuthenticationBroker(pipeName);

        Assert.True(await broker.ProbeAsync(cancellation.Token));
        Assert.True(await broker.ProbeAsync(cancellation.Token));
        cancellation.Cancel();

        await server;
        Assert.False(transport.WasCalled);
    }

    [Fact]
    public async Task Standalone_broker_accepts_repeated_credential_free_probes_without_requesting_login_data()
    {
        var pipeName = $"sam-broker-smoke-{Guid.NewGuid():N}";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = GetBrokerExecutablePath(),
            Arguments = pipeName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("Failed to start the standalone broker.");

        try
        {
            var startup = await process.StandardOutput.ReadLineAsync(timeout.Token);
            Assert.Contains("waiting for local requests", startup, StringComparison.Ordinal);
            var broker = new NamedPipeSteamAuthenticationBroker(pipeName);
            Assert.True(await broker.ProbeAsync(timeout.Token));
            Assert.True(await broker.ProbeAsync(timeout.Token));
            Assert.False(process.HasExited);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(timeout.Token);
        }
    }

    private static string GetBrokerExecutablePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SAM.slnx"))) directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("Could not locate the SAM repository root.");

        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name;
        if (configuration is not "Debug" and not "Release")
            throw new InvalidOperationException("Could not determine the test build configuration.");

        return Path.Combine(directory.FullName, "src", "SAM.SteamBroker", "bin", configuration, "net10.0", "SAM.SteamBroker.exe");
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
        public bool WasCalled { get; private set; }
        public Task<SteamAuthenticationResult> AuthenticateAsync(string accountName, CancellationToken cancellationToken) =>
            Throw();

        private Task<SteamAuthenticationResult> Throw()
        {
            WasCalled = true;
            throw new InvalidOperationException("sensitive external failure");
        }
    }
}
