using System.Collections.Concurrent;

namespace SimpleCoinTrading.Core;

public interface IAlgorithmEngine
{
    public void StartAlgorithm(IAlgorithm algorithm);
    public void StopAlgorithm(string name);
    public void StopAll();
}

public sealed class AlgorithmRuntime
{
    public IAlgorithm Algorithm { get; }
    public AlgorithmStatus Status { get; private set; }
    public Exception? LastError { get; private set; }

    public AlgorithmRuntime(IAlgorithm algorithm)
    {
        Algorithm = algorithm;
        Status = AlgorithmStatus.Created;
    }

    public void MarkRunning() => Status = AlgorithmStatus.Running;
    public void MarkStopped() => Status = AlgorithmStatus.Stopped;

    public void MarkFaulted(Exception ex)
    {
        Status = AlgorithmStatus.Faulted;
        LastError = ex;
    }
}

public enum AlgorithmStatus
{
    Created,
    Running,
    Stopped,
    Faulted
}


public sealed class AlgorithmEngine : IAlgorithmEngine
{
    private readonly IAlgorithmContext _context;
    private readonly ConcurrentDictionary<string, AlgorithmRuntime> _algorithms = new();

    public AlgorithmEngine(IAlgorithmContext context)
    {
        _context = context;
    }

    public IReadOnlyCollection<string> RunningAlgorithms => _algorithms.Keys.ToList();

    // 전략 추가 + 시작
    public void StartAlgorithm(IAlgorithm algorithm)
    {
        if (_algorithms.ContainsKey(algorithm.Name))
            throw new InvalidOperationException($"Algorithm '{algorithm.Name}' already running.");

        var runtime = new AlgorithmRuntime(algorithm);

        try
        {
            algorithm.Initialize(_context);
            algorithm.Run();

            runtime.MarkRunning();
            _algorithms[algorithm.Name] = runtime;
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
        if (!_algorithms.TryRemove(name, out var runtime))
            return;

        try
        {
            runtime.Algorithm.Stop();
            runtime.MarkStopped();
        }
        catch (Exception ex)
        {
            runtime.MarkFaulted(ex);
            throw;
        }
    }

    // 전체 중지
    public void StopAll()
    {
        foreach (var name in _algorithms.Keys)
            StopAlgorithm(name);
    }
}
