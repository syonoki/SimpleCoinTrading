namespace SimpleCoinTrading.Core.Time.TimeFlows;

public readonly record struct Resolution(int Minutes)
{
    public static readonly Resolution M1 = new(1);
    public static readonly Resolution M5 = new(5);
    public override string ToString() => $"{Minutes}m";
}