namespace SAM.Core;

public sealed class FakeSteamClient : ISteamClientService
{
    public async Task<LoginResult> LoginAsync(Account account, CancellationToken cancellationToken)
    {
        await Task.Delay(Random.Shared.Next(250, 1200), cancellationToken);
        var roll = Random.Shared.Next(100);

        if (roll < 75)
            return new(AccountStatus.Online, "模拟登录成功");
        if (roll < 83)
            return new(AccountStatus.RequiresSteamGuard, "需要 Steam Guard");
        if (roll < 91)
            return new(AccountStatus.RateLimited, "模拟限流");
        return new(AccountStatus.Failed, "模拟网络失败");
    }
}
