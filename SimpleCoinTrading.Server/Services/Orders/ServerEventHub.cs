using System.Collections.Concurrent;
using System.Threading.Channels;

namespace SimpleCoinTrading.Server.Services.Orders;



public sealed class ServerEventHub
{
    private readonly ConcurrentDictionary<Guid, Channel<ServerEvent>> _subs = new();

    public ChannelReader<ServerEvent> Subscribe()
    {
        var ch = Channel.CreateUnbounded<ServerEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

        _subs[Guid.NewGuid()] = ch;
        return ch.Reader;
    }

    public void Unsubscribe(ChannelReader<ServerEvent> reader)
    {
        foreach (var kv in _subs)
        {
            if (kv.Value.Reader == reader)
            {
                _subs.TryRemove(kv.Key, out var ch);
                ch?.Writer.TryComplete();
                return;
            }
        }
    }

    public void Publish(ServerEvent ev)
    {
        foreach (var ch in _subs.Values)
            ch.Writer.TryWrite(ev);
    }
}
