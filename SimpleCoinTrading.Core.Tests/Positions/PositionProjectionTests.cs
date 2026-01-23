using System.Reactive.Subjects;
using NSubstitute;
using SimpleCoinTrading.Core.Broker;
using SimpleCoinTrading.Core.Data;
using SimpleCoinTrading.Core.Orders;
using SimpleCoinTrading.Core.Positions;
using Xunit;

namespace SimpleCoinTrading.Core.Tests.Positions;

public class PositionProjectionTests
{
    private readonly IOrderOwnershipStore _ownership = Substitute.For<IOrderOwnershipStore>();
    private readonly IBroker _broker = Substitute.For<IBroker>();
    private readonly MarketDataEventBus _mdebus = new();
    private readonly Subject<BrokerEvent> _brokerEvents = new();

    public PositionProjectionTests()
    {
        _broker.Events.Returns(_brokerEvents);
    }

    [Fact]
    public void OnFill_Should_CreateNewPosition()
    {
        // Arrange
        var sut = new PositionProjection(_ownership, _broker, _mdebus);
        var orderId = "order-1";
        var algoId = "algo-1";
        _ownership.TryGetOwner(orderId, out Arg.Any<string>()!).Returns(x => { x[1] = algoId; return true; });

        var fill = new Fill(orderId, "BTC", OrderSide.Buy, 50000m, 1m, 10m, "KRW", DateTime.UtcNow);
        var fillEvent = new FillEvent(DateTime.UtcNow, fill);

        // Act
        var result = sut.OnFill(fillEvent).ToList();

        // Assert
        Assert.Single(result);
        var pos = result[0];
        Assert.Equal("algo-1", pos.AlgorithmId);
        Assert.Equal("BTC", pos.Symbol);
        Assert.Equal(1m, pos.NetQty);
        Assert.Equal(50000m, pos.AvgPrice);
        Assert.Equal(-10m, pos.RealizedPnl);
    }

    [Fact]
    public void HandleFill_ViaEvent_Should_NotifyChanges()
    {
        // Arrange
        var sut = new PositionProjection(_ownership, _broker, _mdebus);
        var orderId = "order-1";
        var algoId = "algo-1";
        _ownership.TryGetOwner(orderId, out Arg.Any<string>()!).Returns(x => { x[1] = algoId; return true; });

        var fill = new Fill(orderId, "BTC", OrderSide.Buy, 50000m, 1m, 10m, "KRW", DateTime.UtcNow);
        var fillEvent = new FillEvent(DateTime.UtcNow, fill);

        PositionChanged? captured = null;
        sut.Changes.Subscribe(c => captured = c);

        // Act
        _brokerEvents.OnNext(fillEvent);

        // Assert
        Assert.NotNull(captured);
        Assert.Equal("algo-1", captured.Position.AlgorithmId);
        Assert.Equal(1m, captured.Position.NetQty);
    }

    [Fact]
    public void HandleTick_ViaEvent_Should_UpdateUnrealizedPnl()
    {
        // Arrange
        var sut = new PositionProjection(_ownership, _broker, _mdebus);
        var orderId = "order-1";
        var algoId = "algo-1";
        _ownership.TryGetOwner(orderId, out Arg.Any<string>()!).Returns(x => { x[1] = algoId; return true; });

        // 1. Fill로 포지션 생성
        var fill = new Fill(orderId, "BTC", OrderSide.Buy, 50000m, 1m, 0m, "KRW", DateTime.UtcNow);
        sut.OnFill(new FillEvent(DateTime.UtcNow, fill));

        PositionChanged? captured = null;
        sut.Changes.Subscribe(c => captured = c);

        // 2. Tick 발생 (가격 상승 50000 -> 55000)
        var tick = new TradeTick(DateTime.UtcNow, 55000m, 0.1m, true);
        var tickEvent = new TradeTickEvent("BTC", tick);

        // Act
        _mdebus.Publish(tickEvent);

        // Assert
        Assert.NotNull(captured);
        Assert.Equal("BTC", captured.Position.Symbol);
        Assert.Equal(55000m, captured.Position.LastPrice);
        Assert.Equal(5000m, captured.Position.UnrealizedPnl); // (55000 - 50000) * 1
    }

    [Fact]
    public void HandleTick_Should_UpdateMultipleAlgorithms()
    {
        // Arrange
        var sut = new PositionProjection(_ownership, _broker, _mdebus);
        
        // Algo 1
        _ownership.TryGetOwner("o1", out Arg.Any<string>()!).Returns(x => { x[1] = "a1"; return true; });
        sut.OnFill(new FillEvent(DateTime.UtcNow, new Fill("o1", "BTC", OrderSide.Buy, 50000m, 1m, 0m, "KRW", DateTime.UtcNow)));

        // Algo 2
        _ownership.TryGetOwner("o2", out Arg.Any<string>()!).Returns(x => { x[1] = "a2"; return true; });
        sut.OnFill(new FillEvent(DateTime.UtcNow, new Fill("o2", "BTC", OrderSide.Buy, 50000m, 2m, 0m, "KRW", DateTime.UtcNow)));

        int changeCount = 0;
        sut.Changes.Subscribe(_ => changeCount++);

        // Act
        _mdebus.Publish(new TradeTickEvent("BTC", new TradeTick(DateTime.UtcNow, 60000m, 0.1m, true)));

        // Assert
        Assert.Equal(2, changeCount); // 두 알고리즘 모두 업데이트되어야 함
        
        sut.TryGet("a1", "BTC", out var p1);
        sut.TryGet("a2", "BTC", out var p2);
        
        Assert.Equal(10000m, p1.UnrealizedPnl); // (60000-50000)*1
        Assert.Equal(20000m, p2.UnrealizedPnl); // (60000-50000)*2
    }

    [Fact]
    public void HandleFill_UnknownOwner_Should_StillNotifyAsUnknown()
    {
        // Arrange
        var sut = new PositionProjection(_ownership, _broker, _mdebus);
        var orderId = "unknown-order";
        _ownership.TryGetOwner(orderId, out Arg.Any<string>()!).Returns(false);

        PositionChanged? captured = null;
        sut.Changes.Subscribe(c => captured = c);

        var fill = new Fill(orderId, "BTC", OrderSide.Buy, 50000m, 1m, 0m, "KRW", DateTime.UtcNow);

        // Act
        _brokerEvents.OnNext(new FillEvent(DateTime.UtcNow, fill));

        // Assert
        Assert.NotNull(captured);
        Assert.Equal("UNKNOWN", captured.Position.AlgorithmId);
    }
}
