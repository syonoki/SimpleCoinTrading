namespace SimpleCoinTrading.Core.Algorithms;

public interface IAlgorithmFactory
{
    string Key { get; } // 예: "mean_reversion", "mom_v1"
    IAlgorithm Create(string instanceId, IReadOnlyDictionary<string, object> @params);
}

public interface IAlgorithmFactoryRegistry
{
    IAlgorithmFactory Get(string key);
    IReadOnlyList<string> ListKeys();
}

public interface IAlgorithmParameterRegistry
{
    IReadOnlyDictionary<string, object> GetParameters(string algorithmId);
}

public class AlgorithmRegistryItem
{
    public string AlgorithmId { get; set; }
    public string FactoryKey { get; set; }
    public bool IsActive { get; set; }
}

public interface IAlgorithmRegistry
{
    public List<AlgorithmRegistryItem> getAllAlgorithms();
}

public sealed class AlgorithmFactoryRegistry : IAlgorithmFactoryRegistry
{
    private readonly Dictionary<string, IAlgorithmFactory> _map;

    public AlgorithmFactoryRegistry(IEnumerable<IAlgorithmFactory> factories)
        => _map = factories.ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);

    public IAlgorithmFactory Get(string key)
        => _map.TryGetValue(key, out var f)
            ? f
            : throw new InvalidOperationException($"Unknown algorithm key: {key}");

    public IReadOnlyList<string> ListKeys() => _map.Keys.ToList();
}

public class AlgorithmParameterRegistry : IAlgorithmParameterRegistry
{
    private readonly Dictionary<string, IReadOnlyDictionary<string, object>> _map = new();

    public AlgorithmParameterRegistry(Dictionary<string, IReadOnlyDictionary<string, object>> map)
    {
        _map = map;
    }

    public IReadOnlyDictionary<string, object> GetParameters(string algorithmId)
    {
        return _map.TryGetValue(algorithmId, out var dict) ? dict : new Dictionary<string, object>();
    }
}

public class AlgorithmRegistry : IAlgorithmRegistry
{
    private readonly List<AlgorithmRegistryItem> _map = new();

    public AlgorithmRegistry(IEnumerable<AlgorithmRegistryItem> map) => _map.AddRange(map);

    public List<AlgorithmRegistryItem> getAllAlgorithms()
    {
        return _map;
    }
}