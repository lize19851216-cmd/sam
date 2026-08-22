namespace SAM.Core.Tasks;
public enum SamTaskStatus { Pending, Running, Succeeded, Failed, RetryWaiting, Cancelled }
public sealed class SamTaskRecord {
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid AccountId { get; init; }
    public string TaskType { get; init; } = "";
    public SamTaskStatus Status { get; set; } = SamTaskStatus.Pending;
    public int RetryCount { get; set; }
    public string Message { get; set; } = "";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
