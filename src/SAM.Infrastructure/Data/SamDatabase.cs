using Microsoft.Data.Sqlite;
using SAM.Core;
namespace SAM.Infrastructure.Data;
public sealed class SamDatabase {
    private const int BusyTimeoutSeconds = 5;
    private readonly string _cs;
    private const string AccountUpsertCommand = """
        INSERT INTO Accounts(Id,AccountName,SteamId,PersonaName,Status,RetryCount,LastMessage)
        VALUES($id,$n,$s,$p,$st,$r,$m)
        ON CONFLICT(Id) DO UPDATE SET
          AccountName=excluded.AccountName,SteamId=excluded.SteamId,PersonaName=excluded.PersonaName,
          Status=excluded.Status,RetryCount=excluded.RetryCount,LastMessage=excluded.LastMessage;
        """;
    public SamDatabase(string databasePath) {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        _cs = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false, DefaultTimeout = BusyTimeoutSeconds }.ToString();
    }
    public async Task InitializeAsync(CancellationToken cancellationToken = default) {
        await using var c = new SqliteConnection(_cs);
        await c.OpenAsync(cancellationToken);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
        PRAGMA journal_mode = WAL;
        CREATE TABLE IF NOT EXISTS Accounts(
          Id TEXT PRIMARY KEY,
          AccountName TEXT NOT NULL,
          SteamId TEXT NOT NULL,
          PersonaName TEXT NOT NULL,
          Status INTEGER NOT NULL,
          RetryCount INTEGER NOT NULL,
          LastMessage TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_Accounts_AccountName ON Accounts(AccountName);
        """;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
    public Task SaveAccountAsync(Account a) => SaveAccountAsync(a, CancellationToken.None);

    public async Task SaveAccountAsync(Account a, CancellationToken cancellationToken) {
        ValidateAccount(a);
        await using var c = new SqliteConnection(_cs);
        await c.OpenAsync(cancellationToken);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = AccountUpsertCommand;
        AddAccountParameters(cmd, a);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Deletes one persisted account without replacing the remaining snapshot.</summary>
    public async Task<bool> DeleteAccountAsync(Guid accountId, CancellationToken cancellationToken = default) {
        await using var c = new SqliteConnection(_cs);
        await c.OpenAsync(cancellationToken);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM Accounts WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", accountId.ToString());
        return await cmd.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>Clears the persisted account list and returns the number of deleted accounts.</summary>
    public async Task<int> ClearAccountsAsync(CancellationToken cancellationToken = default) {
        await using var c = new SqliteConnection(_cs);
        await c.OpenAsync(cancellationToken);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM Accounts;";
        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Atomically replaces the persisted account snapshot.</summary>
    public async Task ReplaceAccountsAsync(IEnumerable<Account> accounts, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(accounts);
        var snapshot = accounts.ToArray();
        foreach (var account in snapshot) ValidateAccount(account);
        if (snapshot.Select(account => account.Id).Distinct().Count() != snapshot.Length)
            throw new ArgumentException("An account snapshot cannot contain duplicate account IDs.", nameof(accounts));
        if (snapshot.Select(account => account.AccountName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != snapshot.Length)
            throw new ArgumentException("An account snapshot cannot contain duplicate account names.", nameof(accounts));
        await using var c = new SqliteConnection(_cs);
        await c.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await c.BeginTransactionAsync(cancellationToken);
        await using (var delete = c.CreateCommand()) {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM Accounts;";
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var account in snapshot) {
            await using var insert = c.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = AccountUpsertCommand;
            AddAccountParameters(insert, account);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }
    public async Task<IReadOnlyList<Account>> GetAccountsAsync(CancellationToken cancellationToken = default) {
        var accounts = new List<Account>();
        await using var c = new SqliteConnection(_cs);
        await c.OpenAsync(cancellationToken);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id,AccountName,SteamId,PersonaName,Status,RetryCount,LastMessage FROM Accounts ORDER BY AccountName;";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            accounts.Add(new Account {
                Id = Guid.Parse(reader.GetString(0)), AccountName = reader.GetString(1), SteamId = reader.GetString(2),
                PersonaName = reader.GetString(3), Status = (AccountStatus)reader.GetInt32(4),
                RetryCount = reader.GetInt32(5), LastMessage = reader.GetString(6) });
        return accounts;
    }

    private static void AddAccountParameters(SqliteCommand command, Account account) {
        command.Parameters.AddWithValue("$id", account.Id.ToString());
        command.Parameters.AddWithValue("$n", account.AccountName);
        command.Parameters.AddWithValue("$s", account.SteamId);
        command.Parameters.AddWithValue("$p", account.PersonaName);
        command.Parameters.AddWithValue("$st", (int)account.Status);
        command.Parameters.AddWithValue("$r", account.RetryCount);
        command.Parameters.AddWithValue("$m", account.LastMessage);
    }

    private static void ValidateAccount(Account account) {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(account.AccountName);
    }
}
