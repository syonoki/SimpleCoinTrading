using System.Collections.Concurrent;
using System.Threading.Channels;
using SimpleCoinTrading.Core.Positions;

namespace SimpleCoinTrading.Server.Services.Positions;

public interface IPositionUpdateHub
{
    ChannelReader<PositionUpdate> Subscribe(string? algorithmId);
    void PublishUpsert(PositionState p);
    void PublishRemove(string algorithmId, string symbol);
}

public sealed class PositionUpdateHub : IPositionUpdateHub
{
    private readonly ConcurrentBag<(string AlgoId, Channel<PositionUpdate> Ch)> _subs = new();

    public ChannelReader<PositionUpdate> Subscribe(string? algorithmId)
    {
        var algo = string.IsNullOrWhiteSpace(algorithmId) ? "" : algorithmId;

        var ch = Channel.CreateUnbounded<PositionUpdate>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _subs.Add((algo, ch));
        return ch.Reader;
    }

    public void PublishUpsert(PositionState p)
    {
        var dto = PositionDtoMapper.ToDto(p);
        var upd = new PositionUpdate
        {
            Upserted = new PositionUpserted { Position = dto }
        };

        foreach (var (algo, ch) in _subs)
        {
            // algo=="" 이면 전체 구독
            if (algo.Length == 0 || algo == dto.AlgorithmId)
                ch.Writer.TryWrite(upd);
        }
    }

    public void PublishRemove(string algorithmId, string symbol)
    {
        var algo = string.IsNullOrWhiteSpace(algorithmId) ? "UNKNOWN" : algorithmId;

        var upd = new PositionUpdate
        {
            Removed = new PositionRemoved { AlgorithmId = algo, Symbol = symbol ?? "" }
        };

        foreach (var (a, ch) in _subs)
        {
            if (a.Length == 0 || a == algo)
                ch.Writer.TryWrite(upd);
        }
    }
}