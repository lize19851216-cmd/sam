namespace SAM.Core.Tasks;

/// <summary>Stable cursor for loading older task history while newer tasks continue to arrive.</summary>
public sealed record SamTaskHistoryCursor(DateTimeOffset UpdatedAt, Guid Id);

public sealed record SamTaskHistoryPage(IReadOnlyList<SamTaskRecord> Tasks, SamTaskHistoryCursor? NextCursor);
