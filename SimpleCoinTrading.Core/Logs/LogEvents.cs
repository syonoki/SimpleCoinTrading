namespace SimpleCoinTrading.Core.Logs;

public enum AlgoLogLevel { Trace, Debug, Info, Warn, Error }

public sealed record AlgoLogEvent(
    DateTimeOffset Time,
    string AlgorithmId,
    AlgoLogLevel Level,
    string Message,
    string? Symbol = null,
    string? ClientOrderId = null,
    string? OrderId = null,
    IReadOnlyDictionary<string, string>? Tags = null
);