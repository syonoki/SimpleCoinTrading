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

    public void SetupAlgorithm(IAlgorithm algorithm)
    {
        if (_algorithms.ContainsKey(algorithm.AlgorithmId))
            throw new InvalidOperationException($"Algorithm '{algorithm.AlgorithmId}' already running.");
        
        var runtime = new AlgorithmRuntime(algorithm);
        var context = CreateContext(algorithm.AlgorithmId);

        try
        {
            algorithm.Initialize(context);
            _algorithms[algorithm.AlgorithmId] = (runtime, context);
        }
        catch (Exception ex)
        {
            runtime.MarkFaulted(ex);
            context.Dispose();
            throw;
        }
    }

    public void StartAlgorithm(string algorithmId)
    {
        if (!_algorithms.ContainsKey(algorithmId))
            throw new InvalidOperationException($"Algorithm '{algorithmId}' not found.");
        
        var algorithm = _algorithms[algorithmId].Item1.Algorithm;
        var runtime = _algorithms[algorithmId].Item1;

        try
        {
            algorithm.Run();
            runtime.MarkRunning();
        }
        catch (Exception ex)
        {
            runtime.MarkFaulted(ex);
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
    
    public void Dispose()
    {
        StopAll();
        foreach (var entry in _algorithms.Values)
        {
            entry.Context.Dispose();
        }
        _algorithms.Clear();
    }
}