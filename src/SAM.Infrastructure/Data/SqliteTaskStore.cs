using Microsoft.Data.Sqlite;
using SAM.Core.Tasks;

namespace SAM.Infrastructure.Data;

/// <summary>SQLite-backed Task Center store. Each operation owns its connection for safe concurrent workers.</summary>
public sealed class SqliteTaskStore : ISamTaskStore
{
    private readonly string _connectionString;
    public SqliteTaskStore(string databasePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        // Short-lived connections avoid retaining file handles across Task Center operations.
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Tasks (
                Id TEXT PRIMARY KEY, AccountId TEXT NOT NULL, TaskType TEXT NOT NULL,
                Status INTEGER NOT NULL, RetryCount INTEGER NOT NULL, Message TEXT NOT NULL,
                CreatedAt TEXT NOT NULL, StartedAt TEXT NULL, CompletedAt TEXT NULL, UpdatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Tasks_UpdatedAt ON Tasks(UpdatedAt DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveAsync(SamTaskRecord task, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Tasks(Id, AccountId, TaskType, Status, RetryCount, Message, CreatedAt, StartedAt, CompletedAt, UpdatedAt)
            VALUES($id,$accountId,$taskType,$status,$retryCount,$message,$createdAt,$startedAt,$completedAt,$updatedAt)
            ON CONFLICT(Id) DO UPDATE SET Status=excluded.Status, RetryCount=excluded.RetryCount,
              Message=excluded.Message, StartedAt=excluded.StartedAt, CompletedAt=excluded.CompletedAt, UpdatedAt=excluded.UpdatedAt;
            """;
        command.Parameters.AddWithValue("$id", task.Id.ToString());
        command.Parameters.AddWithValue("$accountId", task.AccountId.ToString());
        command.Parameters.AddWithValue("$taskType", task.TaskType);
        command.Parameters.AddWithValue("$status", (int)task.Status);
        command.Parameters.AddWithValue("$retryCount", task.RetryCount);
        command.Parameters.AddWithValue("$message", task.Message);
        command.Parameters.AddWithValue("$createdAt", task.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$startedAt", (object?)task.StartedAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$completedAt", (object?)task.CompletedAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", task.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SamTaskRecord>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        return await GetPageAsync(0, limit, cancellationToken);
    }

    public async Task<IReadOnlyList<SamTaskRecord>> GetPageAsync(int offset, int limit, CancellationToken cancellationToken = default)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit <= 0) return [];
        var tasks = new List<SamTaskRecord>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,AccountId,TaskType,Status,RetryCount,Message,CreatedAt,StartedAt,CompletedAt,UpdatedAt FROM Tasks ORDER BY UpdatedAt DESC, Id DESC LIMIT $limit OFFSET $offset;";
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            tasks.Add(new SamTaskRecord {
                Id = Guid.Parse(reader.GetString(0)), AccountId = Guid.Parse(reader.GetString(1)), TaskType = reader.GetString(2),
                Status = (SamTaskStatus)reader.GetInt32(3), RetryCount = reader.GetInt32(4), Message = reader.GetString(5),
                CreatedAt = DateTimeOffset.Parse(reader.GetString(6)), StartedAt = reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7)),
                CompletedAt = reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8)), UpdatedAt = DateTimeOffset.Parse(reader.GetString(9)) });
        return tasks;
    }
}
