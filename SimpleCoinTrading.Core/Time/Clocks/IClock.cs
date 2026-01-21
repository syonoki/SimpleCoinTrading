namespace SimpleCoinTrading.Core.Time.Clocks;

public interface IClock
{
    DateTime UtcNow { get; }
}

public interface ISettableClock : IClock
{
    void SetUtc(DateTime nowUtc);
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

public sealed class SimulatedClock : ISettableClock
{
    public DateTime UtcNow { get; private set; }

    public void SetUtc(DateTime nowUtc)
    {
        UtcNow = nowUtc;
    }
}

public sealed class ManualClock : IClock
{
    public DateTime UtcNow { get; private set; }

    public ManualClock(DateTime startUtc)
    {
        UtcNow = startUtc;
    }

    public void Advance(TimeSpan delta)
    {
        UtcNow = UtcNow.Add(delta);
    }
}