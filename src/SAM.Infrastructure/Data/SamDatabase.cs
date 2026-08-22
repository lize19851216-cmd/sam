using Microsoft.Data.Sqlite;
using SAM.Core;
namespace SAM.Infrastructure.Data;
public sealed class SamDatabase {
    private readonly string _cs;
    public SamDatabase(string databasePath) {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _cs = $"Data Source={databasePath}";
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
}
