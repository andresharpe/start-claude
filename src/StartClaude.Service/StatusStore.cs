namespace StartClaude.Service;

public sealed class StatusStore
{
    private readonly object _gate = new();
    public DateTime? LastPollUtc { get; private set; }
    public DateTime? LastSpawnUtc { get; private set; }
    public string? LastError { get; private set; }
    public int LastTargetCount { get; private set; }

    public void RecordPoll(int targetCount)
    {
        lock (_gate)
        {
            LastPollUtc = DateTime.UtcNow;
            LastTargetCount = targetCount;
        }
    }

    public void RecordSpawn()
    {
        lock (_gate)
        {
            LastSpawnUtc = DateTime.UtcNow;
            LastError = null;
        }
    }

    public void RecordError(string error)
    {
        lock (_gate)
        {
            LastError = error;
        }
    }
}
