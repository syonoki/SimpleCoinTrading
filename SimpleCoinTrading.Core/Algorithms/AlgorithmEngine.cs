using System.Collections.Concurrent;
using SimpleCoinTrading.Core.Data;
using SimpleCoinTrading.Core.Logs;
using SimpleCoinTrading.Core.Orders;
using SimpleCoinTrading.Core.Time.Clocks;

namespace SimpleCoinTrading.Core.Algorithms;

public sealed class AlgorithmEngine : IAlgorithmEngine
{
    private readonly IMarketDataView _market;
    private readonly IClock _clock;
    private readonly MarketDataEventBus _bus;
    private readonly IOrderOrchestrator _orderOrchestrator;
    private readonly IAlgorithmLoggerFactory _logFactory;
    private readonly ConcurrentDictionary<string, (AlgorithmRuntime Runtime, IAlgorithmContext Context)> _algorithms = new();

    public AlgorithmEngine(IMarketDataView market,
        IClock clock, MarketDataEventBus bus,
        IOrderOrchestrator orderOrchestrator,
        IAlgorithmLoggerFactory logFactory)
    {
        _market = market;
        _clock = clock;
        _bus = bus;
        _orderOrchestrator = orderOrchestrator;
        _logFactory = logFactory;
    }

    public IReadOnlyCollection<string> RunningAlgorithms => _algorithms.Keys.ToList();

    // 전략 추가 + 시작
    public void StartAlgorithm(IAlgorithm algorithm)
    {
        if (_algorithms.ContainsKey(algorithm.Name))
            throw new InvalidOperationException($"Algorithm '{algorithm.Name}' already running.");

        var runtime = new AlgorithmRuntime(algorithm);
        var context = CreateContext(algorithm.Name);

        try
        {
            algorithm.Initialize(context);
            algorithm.Run();

            runtime.MarkRunning();
            _algorithms[algorithm.Name] = (runtime, context);
        }
        catch (Exception ex)
        {
            runtime.MarkFaulted(ex);
            context.Dispose();
            throw;
        }
    }

    // 전략 중지
    public void StopAlgorithm(string name)
    {
        if (!_algorithms.TryRemove(name, out var entry))
            return;

        try
        {
            entry.Runtime.Algorithm.Stop();
            entry.Context.Dispose();
            entry.Runtime.MarkStopped();
        }
        catch (Exception ex)
        {
            entry.Runtime.MarkFaulted(ex);
            throw;
        }
    }

    // 전체 중지
    public void StopAll()
    {
        foreach (var name in _algorithms.Keys)
            StopAlgorithm(name);
    }

    public IReadOnlyDictionary<string, AlgorithmRuntime> Algorithms 
        => _algorithms.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Runtime);


    private IAlgorithmContext CreateContext(string algorithmId)
    {
        return new AlgorithmContext(
            algorithmId,
            _market, _clock, _bus, _orderOrchestrator, _logFactory);
    }
}