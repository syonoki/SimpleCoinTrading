namespace SimpleCoinTrading.Core.OrderOrchestrators;

public interface ITradingGuard
{
    bool IsReadOnly { get; }
    string? Reason { get; }

    // 이벤트/규칙 위반 시 호출
    void Trip(string reason);

    // 운영자가 명시적으로 거래 재개
    void Clear();
}


public sealed class TradingGuard : ITradingGuard
{
    private int _readOnly; // 0/1
    private string? _reason;

    public bool IsReadOnly => Volatile.Read(ref _readOnly) == 1;
    public string? Reason => _reason;

    public void Trip(string reason)
    {
        Volatile.Write(ref _readOnly, 1);
        _reason = reason;
    }

    public void Clear()
    {
        Volatile.Write(ref _readOnly, 0);
        _reason = null;
    }
}