namespace SimpleCoinTrading.Core.Logs;

public sealed class AlgorithmLogger : IAlgorithmLogger
{
    private readonly string _algorithmId;
    private readonly IAlgorithmLogHub _hub;

    public AlgorithmLogger(string algorithmId, IAlgorithmLogHub hub)
    {
        _algorithmId = algorithmId;
        _hub = hub;
    }

    public void Info(string message, string? symbol = null)
        => Write(AlgoLogLevel.Info, message, symbol);

    public void Warn(string message, string? symbol = null)
        => Write(AlgoLogLevel.Warn, message, symbol);

    public void Error(string message, Exception? ex = null, string? symbol = null)
        => Write(AlgoLogLevel.Error, ex is null ? message : $"{message} | {ex.Message}", symbol);

    public void Trace(string message, string? symbol = null) 
        => Write(AlgoLogLevel.Trace, message, symbol);

    public void Debug(string message, string? symbol = null) 
        => Write(AlgoLogLevel.Debug, message, symbol);

    private void Write(AlgoLogLevel level, string message, string? symbol)
    {
        _hub.Write(new AlgoLogEvent(
            Time: DateTimeOffset.UtcNow,
            AlgorithmId: _algorithmId,
            Level: level,
            Message: message,
            Symbol: symbol
        ));
    }

    // Trace/Debug도 동일 패턴
}
