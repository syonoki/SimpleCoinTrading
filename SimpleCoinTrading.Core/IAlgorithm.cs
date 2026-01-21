using SimpleCoinTrading.Core.Broker;
using SimpleCoinTrading.Core.Data;
using SimpleCoinTrading.Core.Logs;
using SimpleCoinTrading.Core.Time;
using SimpleCoinTrading.Core.Time.Clocks;

namespace SimpleCoinTrading.Core;

public interface IAlgorithmContext
{
    IMarketDataView Market { get; } // bars/trades/book 조회
    IClock Clock { get; }

    IAlgorithmLogger GetLogger(string algorithmId);
    
    // Order
    Task<OrderState?> GetOrderAsync(string orderId, CancellationToken ct = default);
    Task<OrderAck> PlaceOrderAsync(PlaceOrderRequest request, CancellationToken ct = default);
    Task CancelAsync(string orderId, CancellationToken ct = default);
    
    // Market data event handlers
    IDisposable SubscribeBarClosed(Action<BarClosedEvent> handler);
    IDisposable SubscribeTrade(Action<TradeTickEvent> handler);
    IDisposable SubscribeOrderBook(Action<OrderBookTopEvent> handler);
    
    // 선택: 주기 실행
    IDisposable Schedule(TimeSpan interval, Action tick);
}

public interface IAlgorithm
{
    string Name { get; }

    void Initialize(IAlgorithmContext ctx); // 여기서 핸들러 등록
    void Run();                             // 선택: 타이머/백그라운드 작업 시작
    void Stop();                            // 구독 해제/리소스 정리
}