namespace SimpleCoinTrading.Core.Logs;

public interface IAlgorithmLogSink
{
    void Write(AlgoLogEvent e);
}