namespace SimpleCoinTrading.Core.Ids;

public readonly record struct BatchId(Guid Value)
{
    public static BatchId New() => new(Guid.NewGuid());

    public bool IsEmpty => Value == Guid.Empty;

    public override string ToString()
        => Value.ToString("N"); // 로그/UI에 짧게
}