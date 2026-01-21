using SimpleCoinTrading.Core.Broker;
using SimpleCoinTrading.Core.Data;
using SimpleCoinTrading.Core.Time;
using SimpleCoinTrading.Core.Time.Clocks;
using Xunit;

namespace SimpleCoinTrading.Core.Tests.Broker;

public class PaperBrokerTests
{
    private (PaperBroker broker, SimulatedClock clock, InMemoryMarketDataRepository storage, MarketDataView view, MarketDataEventBus bus) CreateSystem()
    {
        var clock = new SimulatedClock();
        clock.SetUtc(new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc));
        var storage = new InMemoryMarketDataRepository(new InMemoryTradeStorage(), new InMemoryOrderBookStorage(), new InMemoryBarStorage());
        var view = new MarketDataView(clock, storage);
        var bus = new MarketDataEventBus();
        var broker = new PaperBroker("TestBroker", view, handler => bus.SubBook(handler), initialKrw: 10_000_000m);
        return (broker, clock, storage, view, bus);
    }

    [Fact]
    public async Task GetAccountAsync_ShouldReturnInitialBalance()
    {
        var (broker, _, _, _, _) = CreateSystem();
        
        var account = await broker.GetAccountAsync();
        
        var krw = account.Balances.FirstOrDefault(b => b.Currency == "KRW");
        Assert.NotNull(krw);
        Assert.Equal(10_000_000m, krw.Total);
    }

    [Fact]
    public async Task PlaceLimitOrder_ShouldRemainOpen_WhenNoMatchingPrice()
    {
        var (broker, _, storage, _, _) = CreateSystem();
        await broker.StartAsync();

        // BTC price is 50,000
        storage.UpdateTopOfBook("BTC", new OrderBookTop(DateTime.UtcNow, 49990, 1, 50010, 1));

        var req = new PlaceOrderRequest("BTC", OrderSide.Buy, OrderType.Limit, 1.0m, LimitPrice: 40000m);
        var ack = await broker.PlaceOrderAsync(req);

        Assert.True(ack.Accepted);
        var order = await broker.GetOrderAsync(ack.OrderId!);
        Assert.Equal(OrderStatus.Accepted, order!.Status);
        
        var openOrders = await broker.GetOpenOrdersAsync("BTC");
        Assert.Single(openOrders);
    }

    [Fact]
    public async Task PlaceMarketOrder_ShouldFillImmediately_IfLiquidityExists()
    {
        var (broker, _, storage, _, _) = CreateSystem();
        await broker.StartAsync();

        // BTC Ask is 50010
        storage.UpdateTopOfBook("BTC", new OrderBookTop(DateTime.UtcNow, 49990, 1, 50010, 1));

        var req = new PlaceOrderRequest("BTC", OrderSide.Buy, OrderType.Market, 0.1m);
        var ack = await broker.PlaceOrderAsync(req);

        Assert.True(ack.Accepted);
        var order = await broker.GetOrderAsync(ack.OrderId!);
        Assert.Equal(OrderStatus.Filled, order!.Status);
        Assert.Equal(50010m, order.AvgFillPrice);
        
        var pos = await broker.GetPositionAsync("BTC");
        Assert.Equal(0.1m, pos!.Quantity);
    }

    [Fact]
    public async Task CancelOrder_ShouldWork()
    {
        var (broker, _, storage, _, _) = CreateSystem();
        await broker.StartAsync();
        storage.UpdateTopOfBook("BTC", new OrderBookTop(DateTime.UtcNow, 49990, 1, 50010, 1));

        var req = new PlaceOrderRequest("BTC", OrderSide.Buy, OrderType.Limit, 1.0m, LimitPrice: 40000m);
        var ack = await broker.PlaceOrderAsync(req);
        Assert.True(ack.Accepted);
        
        var cancelAck = await broker.CancelOrderAsync(new CancelOrderRequest(ack.OrderId!));
        Assert.True(cancelAck.Accepted);
        
        var order = await broker.GetOrderAsync(ack.OrderId!);
        Assert.Equal(OrderStatus.Canceled, order!.Status);
    }

    [Fact]
    public async Task CancelAllAsync_ShouldCancelAllOpenOrders()
    {
        var (broker, _, storage, _, _) = CreateSystem();
        await broker.StartAsync();
        storage.UpdateTopOfBook("BTC", new OrderBookTop(DateTime.UtcNow, 49990, 1, 50010, 1));
        storage.UpdateTopOfBook("ETH", new OrderBookTop(DateTime.UtcNow, 2990, 1, 3010, 1));

        // 3 orders
        var ack1 = await broker.PlaceOrderAsync(new PlaceOrderRequest("BTC", OrderSide.Buy, OrderType.Limit, 1.0m, LimitPrice: 40000m));
        var ack2 = await broker.PlaceOrderAsync(new PlaceOrderRequest("BTC", OrderSide.Buy, OrderType.Limit, 1.0m, LimitPrice: 40000m));
        var ack3 = await broker.PlaceOrderAsync(new PlaceOrderRequest("ETH", OrderSide.Buy, OrderType.Limit, 1.0m, LimitPrice: 2000m));

        Assert.True(ack1.Accepted && ack2.Accepted && ack3.Accepted);
        
        var open1 = await broker.GetOpenOrdersAsync("BTC");
        var open2 = await broker.GetOpenOrdersAsync("ETH");
        Assert.Equal(2, open1.Count);
        Assert.Equal(1, open2.Count);

        // Cancel All
        await broker.CancelAllAsync();

        // Check status
        var o1 = await broker.GetOrderAsync(ack1.OrderId!);
        var o2 = await broker.GetOrderAsync(ack2.OrderId!);
        var o3 = await broker.GetOrderAsync(ack3.OrderId!);
        
        Assert.Equal(OrderStatus.Canceled, o1!.Status);
        Assert.Equal(OrderStatus.Canceled, o2!.Status);
        Assert.Equal(OrderStatus.Canceled, o3!.Status);

        Assert.Empty(await broker.GetOpenOrdersAsync("BTC"));
        Assert.Empty(await broker.GetOpenOrdersAsync("ETH"));
    }

    [Fact]
    public async Task InsufficientBalance_ShouldRejectOrder()
    {
        var (broker, _, storage, _, _) = CreateSystem();
        await broker.StartAsync();
        
        storage.UpdateTopOfBook("BTC", new OrderBookTop(DateTime.UtcNow, 49990, 1, 50010, 1));

        // Try to buy 1000 BTC at 50000 each = 50,000,000 (Initial KRW is 10,000,000)
        var req = new PlaceOrderRequest("BTC", OrderSide.Buy, OrderType.Market, 1000m);
        var ack = await broker.PlaceOrderAsync(req);

        // 잔고 부족 시 PlaceOrderAsync 가 false 인 OrderAck 를 반환함
        Assert.False(ack.Accepted);
        Assert.Contains("Insufficient KRW", ack.Message);
        
        var order = await broker.GetOrderAsync(ack.OrderId ?? "");
        Assert.Null(order);
    }

    [Fact]
    public async Task LimitOrder_ShouldFill_WhenOrderBookUpdates()
    {
        var (broker, _, storage, _, bus) = CreateSystem();
        await broker.StartAsync();

        // 1. 초기 호가: 50010 (Ask)
        var top1 = new OrderBookTop(DateTime.UtcNow, 49990, 1, 50010, 1);
        storage.UpdateTopOfBook("BTC", top1);

        // 2. 50005에 매수 지정가 주문 (현재 Ask 50010 보다 낮으므로 미체결)
        var req = new PlaceOrderRequest("BTC", OrderSide.Buy, OrderType.Limit, 0.5m, LimitPrice: 50005m);
        var ack = await broker.PlaceOrderAsync(req);
        Assert.True(ack.Accepted);
        
        var order = await broker.GetOrderAsync(ack.OrderId!);
        Assert.Equal(OrderStatus.Accepted, order!.Status);

        // 3. 호가 업데이트: Ask 가 50005로 하락
        var top2 = new OrderBookTop(DateTime.UtcNow, 49990, 1, 50005, 1);
        storage.UpdateTopOfBook("BTC", top2);
        bus.Publish(new OrderBookTopEvent("BTC", top2)); // 이벤트를 통해 브로커에 알림

        // 4. 체결 확인
        order = await broker.GetOrderAsync(ack.OrderId!);
        Assert.Equal(OrderStatus.Filled, order!.Status);
        Assert.Equal(50005m, order.AvgFillPrice);
        
        var pos = await broker.GetPositionAsync("BTC");
        Assert.Equal(0.5m, pos!.Quantity);
    }
}
