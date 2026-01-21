namespace SimpleCoinTrading.Core.Logs;

public interface IAlgorithmLogger
{
    void Trace(string message, string? symbol = null);
    void Debug(string message, string? symbol = null);
    void Info(string message, string? symbol = null);
    void Warn(string message, string? symbol = null);
    void Error(string message, Exception? ex = null, string? symbol = null);
}