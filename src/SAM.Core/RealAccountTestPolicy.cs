namespace SAM.Core;

/// <summary>
/// Guards the deliberately narrow path used to test a user-owned Steam account
/// through the separately launched local authentication broker.
/// </summary>
public static class RealAccountTestPolicy
{
    public const int MaximumAccountNameLength = Steam.SteamAuthenticationBrokerRequest.MaximumAccountNameLength;

    public static bool IsSimulatedAccountName(string? accountName) =>
        accountName?.StartsWith("mock_", StringComparison.OrdinalIgnoreCase) == true;

    public static string ValidateAccountName(string? accountName)
    {
        if (string.IsNullOrWhiteSpace(accountName))
            throw new ArgumentException("An account name is required.", nameof(accountName));

        var normalized = accountName.Trim();
        new Steam.SteamAuthenticationBrokerRequest(normalized).Validate();

        if (IsSimulatedAccountName(normalized))
            throw new ArgumentException("Simulated accounts cannot use the external authentication broker.", nameof(accountName));

        return normalized;
    }

    public static void EnsureSingleExternalTestAccount(IEnumerable<Account> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        var snapshot = accounts as ICollection<Account> ?? accounts.ToArray();
        if (snapshot.Count != 1)
            throw new InvalidOperationException("External authentication broker tests require exactly one account.");

        ValidateAccountName(snapshot.Single().AccountName);
    }

    public static bool TryGetSingleExternalTestAccountName(IEnumerable<Account> accounts, out string accountName)
    {
        accountName = string.Empty;
        try
        {
            ArgumentNullException.ThrowIfNull(accounts);
            var snapshot = accounts as ICollection<Account> ?? accounts.ToArray();
            EnsureSingleExternalTestAccount(snapshot);
            accountName = ValidateAccountName(snapshot.Single().AccountName);
            return true;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
