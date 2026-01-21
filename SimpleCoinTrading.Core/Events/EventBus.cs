using System.Threading.Channels;

namespace SimpleCoinTrading.Core.Events;

public interface IEventBus
{
    ValueTask PublishNormalAsync(TradingEvent e);
    ValueTask PublishImmediateAsync(TradingEvent e);

    ChannelReader<TradingEvent> NormalReader { get; }
    ChannelReader<TradingEvent> ImmediateReader { get; }
}

public sealed class EventBus : IEventBus
{
    private readonly Channel<TradingEvent> _normal =
        Channel.CreateUnbounded<TradingEvent>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly Channel<TradingEvent> _immediate =
        Channel.CreateUnbounded<TradingEvent>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public ChannelReader<TradingEvent> NormalReader => _normal.Reader;
    public ChannelReader<TradingEvent> ImmediateReader => _immediate.Reader;

    public ValueTask PublishNormalAsync(TradingEvent e) => _normal.Writer.WriteAsync(e);
    public ValueTask PublishImmediateAsync(TradingEvent e) => _immediate.Writer.WriteAsync(e);
}