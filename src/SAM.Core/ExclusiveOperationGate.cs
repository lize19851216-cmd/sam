namespace SAM.Core;

/// <summary>Allows one active operation and releases the slot exactly once when its lease is disposed.</summary>
public sealed class ExclusiveOperationGate
{
    private int _isHeld;

    public IDisposable? TryEnter()
    {
        return Interlocked.CompareExchange(ref _isHeld, 1, 0) == 0
            ? new Lease(this)
            : null;
    }

    private void Exit() => Volatile.Write(ref _isHeld, 0);

    private sealed class Lease(ExclusiveOperationGate owner) : IDisposable
    {
        private ExclusiveOperationGate? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Exit();
    }
}
