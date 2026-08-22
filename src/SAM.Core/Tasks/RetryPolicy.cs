namespace SAM.Core.Tasks;
public sealed record RetryPolicy(int MaxRetries=3, TimeSpan? BaseDelay=null, TimeSpan? Timeout=null) {
    public TimeSpan EffectiveBaseDelay => BaseDelay ?? TimeSpan.FromMilliseconds(500);
    public TimeSpan EffectiveTimeout => Timeout ?? TimeSpan.FromSeconds(15);
    public TimeSpan GetDelay(int retry) => TimeSpan.FromMilliseconds(
        Math.Min(EffectiveBaseDelay.TotalMilliseconds * Math.Pow(2, Math.Max(0,retry)), 30000));
}
