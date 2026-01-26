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

public interface IAlgorithmEngine: IDisposable
{
    public void SetupAlgorithm(IAlgorithm algorithm);
    
    public void StartAlgorithm(string algorithmId);
    public void StopAlgorithm(string algorithmId);
    public void StopAll();

    public IReadOnlyDictionary<string, AlgorithmRuntime> Algorithms { get; }
}