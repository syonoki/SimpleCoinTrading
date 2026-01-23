namespace SimpleCoinTrading.Core.Algorithms;

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

public interface IAlgorithmEngine
{
    public void StartAlgorithm(IAlgorithm algorithm);
    public void StopAlgorithm(string name);
    public void StopAll();

    public IReadOnlyDictionary<string, AlgorithmRuntime> Algorithms { get; }
}