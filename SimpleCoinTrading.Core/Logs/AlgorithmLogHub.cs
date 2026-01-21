using System.Collections.Concurrent;
using System.Threading.Channels;

namespace SimpleCoinTrading.Core.Logs;

public sealed class AlgorithmLogHub : IAlgorithmLogHub
{
    private readonly int _capacityPerAlgo;
    private readonly ConcurrentDictionary<string, RingBuffer<AlgoLogEvent>> _buffers = new();
    private readonly ConcurrentDictionary<string, ConcurrentBag<Channel<AlgoLogEvent>>> _subs = new();
    private readonly IReadOnlyList<IAlgorithmLogSink> _sinks;

    public AlgorithmLogHub(int capacityPerAlgo = 5000, IEnumerable<IAlgorithmLogSink> sinks = null!)
    {
        _capacityPerAlgo = capacityPerAlgo;
        _sinks = sinks.ToList();
    }

    public void Write(AlgoLogEvent e)
    {
        var algoId = string.IsNullOrWhiteSpace(e.AlgorithmId) ? "UNKNOWN" : e.AlgorithmId;

        var buf = _buffers.GetOrAdd(algoId, _ => new RingBuffer<AlgoLogEvent>(_capacityPerAlgo));
        buf.Add(e);
        
        foreach (var sink in _sinks)
            sink.Write(e);

        if (_subs.TryGetValue(algoId, out var bag))
        {
            foreach (var ch in bag)
                ch.Writer.TryWrite(e);
        }
    }

    public IReadOnlyList<AlgoLogEvent> GetRecent(string algorithmId, int limit)
    {
        var algoId = string.IsNullOrWhiteSpace(algorithmId) ? "UNKNOWN" : algorithmId;
        return _buffers.TryGetValue(algoId, out var buf) ? buf.Tail(limit) : Array.Empty<AlgoLogEvent>();
    }

    public ChannelReader<AlgoLogEvent> Subscribe(string algorithmId)
    {
        var algoId = string.IsNullOrWhiteSpace(algorithmId) ? "UNKNOWN" : algorithmId;

        var ch = Channel.CreateUnbounded<AlgoLogEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        var bag = _subs.GetOrAdd(algoId, _ => new ConcurrentBag<Channel<AlgoLogEvent>>());
        bag.Add(ch);

        return ch.Reader;
    }
}