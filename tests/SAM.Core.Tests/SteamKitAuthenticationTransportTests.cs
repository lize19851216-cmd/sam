using SAM.Core.Steam;
using SAM.Infrastructure.Steam;
using SteamKit2;
using Xunit;

namespace SAM.Core.Tests;

public sealed class SteamKitAuthenticationTransportTests
{
    [Fact]
    public async Task Transport_uses_a_short_lived_session_and_disposes_it()
    {
        var session = new StubSession(new SteamAuthenticationResult(SteamAuthenticationStatus.Online, "connected"));
        var result = await new SteamKitAuthenticationTransport(new StubSessionFactory(session)).AuthenticateAsync("mock_0001", CancellationToken.None);

        Assert.Equal(SteamAuthenticationStatus.Online, result.Status);
        Assert.Equal("mock_0001", session.AccountName);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task Transport_sanitizes_external_session_failures()
    {
        var result = await new SteamKitAuthenticationTransport(new StubSessionFactory(new ThrowingSession())).AuthenticateAsync("mock_0001", CancellationToken.None);

        Assert.Equal(SteamAuthenticationStatus.Failed, result.Status);
        Assert.Equal("Steam authentication could not be completed.", result.Message);
    }

    [Fact]
    public async Task Transport_preserves_caller_requested_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SteamKitAuthenticationTransport(new StubSessionFactory(new CancelledSession())).AuthenticateAsync("mock_0001", cancellation.Token));
    }

    [Fact]
    public void Logon_details_keep_the_host_account_and_disable_password_remembering()
    {
        var details = SteamKitLogOnDetailsFactory.Create("mock_0001", new PasswordRememberingConfigurator());

        Assert.Equal("mock_0001", details.Username);
        Assert.False(details.ShouldRememberPassword);
    }

    [Fact]
    public void Logon_details_reject_an_external_source_that_changes_the_account()
    {
        Assert.Throws<InvalidOperationException>(() => SteamKitLogOnDetailsFactory.Create("mock_0001", new AccountChangingConfigurator()));
    }

    [Theory]
    [InlineData(EResult.OK, SteamAuthenticationStatus.Online)]
    [InlineData(EResult.AccountLogonDenied, SteamAuthenticationStatus.RequiresSteamGuard)]
    [InlineData(EResult.RateLimitExceeded, SteamAuthenticationStatus.RateLimited)]
    [InlineData(EResult.Fail, SteamAuthenticationStatus.Failed)]
    public void SteamKit_results_are_mapped_without_exposing_protocol_details(EResult result, SteamAuthenticationStatus expectedStatus)
    {
        var mapped = SteamKitAuthenticationResultMapper.From(result, "76561190000000001");

        Assert.Equal(expectedStatus, mapped.Status);
        Assert.DoesNotContain(result.ToString(), mapped.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubSessionFactory(ISteamKitAuthenticationSession session) : ISteamKitAuthenticationSessionFactory
    {
        public ISteamKitAuthenticationSession Create() => session;
    }

    private sealed class StubSession(SteamAuthenticationResult result) : ISteamKitAuthenticationSession
    {
        public string? AccountName { get; private set; }
        public bool Disposed { get; private set; }

        public Task<SteamAuthenticationResult> AuthenticateAsync(string accountName, CancellationToken cancellationToken)
        {
            AccountName = accountName;
            return Task.FromResult(result);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingSession : ISteamKitAuthenticationSession
    {
        public Task<SteamAuthenticationResult> AuthenticateAsync(string accountName, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("external source returned sensitive material");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CancelledSession : ISteamKitAuthenticationSession
    {
        public Task<SteamAuthenticationResult> AuthenticateAsync(string accountName, CancellationToken cancellationToken) =>
            Task.FromCanceled<SteamAuthenticationResult>(cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class PasswordRememberingConfigurator : IExternalSteamLogOnConfigurator
    {
        public void Configure(SteamUser.LogOnDetails logOnDetails) => logOnDetails.ShouldRememberPassword = true;
    }

    private sealed class AccountChangingConfigurator : IExternalSteamLogOnConfigurator
    {
        public void Configure(SteamUser.LogOnDetails logOnDetails) => logOnDetails.Username = "another-account";
    }
}
