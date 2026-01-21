namespace SimpleCoinTrading.Core.Logs;

public interface IAlgorithmLoggerFactory
{
    IAlgorithmLogger Create(string algorithmId);
}

public sealed class AlgorithmLoggerFactory : IAlgorithmLoggerFactory
{
    private readonly IAlgorithmLogHub _hub;
    public AlgorithmLoggerFactory(IAlgorithmLogHub hub) => _hub = hub;

    public IAlgorithmLogger Create(string algorithmId)
        => new AlgorithmLogger(algorithmId, _hub);
}
