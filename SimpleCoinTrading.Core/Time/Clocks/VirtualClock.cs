namespace SimpleCoinTrading.Core.Time.Clocks;



public sealed class VirtualClock : IClock, ISettableClock
{
    private long _utcTicks;

    public DateTime UtcNow => new DateTime(
        Interlocked.Read(ref _utcTicks),
        DateTimeKind.Utc);

    public void SetUtc(DateTime utc)
    {
        utc = EnsureUtc(utc);
        Interlocked.Exchange(ref _utcTicks, utc.Ticks);
    }

    public void AdvanceToUtc(DateTime utc)
    {
        utc = EnsureUtc(utc);

        while (true)
        {
            var cur = Interlocked.Read(ref _utcTicks);
            if (utc.Ticks <= cur) return;
            if (Interlocked.CompareExchange(ref _utcTicks, utc.Ticks, cur) == cur) return;
        }
    }

    private static DateTime EnsureUtc(DateTime dt)
        => dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
}
