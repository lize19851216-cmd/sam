using Microsoft.Data.Sqlite;
using SAM.Core;
namespace SAM.Infrastructure.Data;
public sealed class SamDatabase {
    private readonly string _cs;
    public SamDatabase(string databasePath) {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        _cs = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
    }
    public async Task InitializeAsync() {
        await using var c = new SqliteConnection(_cs);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
        CREATE TABLE IF NOT EXISTS Accounts(
          Id TEXT PRIMARY KEY,
          AccountName TEXT NOT NULL,
          SteamId TEXT NOT NULL,
          PersonaName TEXT NOT NULL,
          Status INTEGER NOT NULL,
          RetryCount INTEGER NOT NULL,
          LastMessage TEXT NOT NULL
        );
        """;
        await cmd.ExecuteNonQueryAsync();
    }
    public async Task SaveAccountAsync(Account a) {
        await using var c = new SqliteConnection(_cs);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
        INSERT INTO Accounts(Id,AccountName,SteamId,PersonaName,Status,RetryCount,LastMessage)
        VALUES($id,$n,$s,$p,$st,$r,$m)
        ON CONFLICT(Id) DO UPDATE SET
          AccountName=excluded.AccountName,SteamId=excluded.SteamId,PersonaName=excluded.PersonaName,
          Status=excluded.Status,RetryCount=excluded.RetryCount,LastMessage=excluded.LastMessage;
        """;
        cmd.Parameters.AddWithValue("$id", a.Id.ToString());
        cmd.Parameters.AddWithValue("$n", a.AccountName);
        cmd.Parameters.AddWithValue("$s", a.SteamId);
        cmd.Parameters.AddWithValue("$p", a.PersonaName);
        cmd.Parameters.AddWithValue("$st", (int)a.Status);
        cmd.Parameters.AddWithValue("$r", a.RetryCount);
        cmd.Parameters.AddWithValue("$m", a.LastMessage);
        await cmd.ExecuteNonQueryAsync();
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
}
