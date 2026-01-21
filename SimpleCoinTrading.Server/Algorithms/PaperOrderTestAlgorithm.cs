using SimpleCoinTrading.Core;
using SimpleCoinTrading.Core.Broker;
using SimpleCoinTrading.Core.Data;

namespace SimpleCoinTrading.Server.Algorithms;

public sealed class PaperOrderTestAlgorithm : IAlgorithm
{
    public string Name => "PaperOrderTestAlgorithm";

    private IAlgorithmContext? _ctx;
    private readonly List<IDisposable> _subs = new();

    private string? _activeOrderId;

    public void Initialize(IAlgorithmContext ctx)
    {
        _ctx = ctx;

        _subs.Add(ctx.SubscribeBarClosed(OnBarClosed));

        // 체결/주문상태는 broker.Events로도 볼 수 있지만,
        // 전략이 직접 구독하고 싶다면 ctx가 broker를 노출하는 형태로 받으면 됨.
    }

    public void Run()
    {
    }

    public void Stop()
    {
        foreach (var s in _subs) s.Dispose();
        _subs.Clear();
    }

    private void OnBarClosed(BarClosedEvent e)
    {
        // 심볼 1개만 테스트한다고 가정
        var sym = e.Symbol;

        // 이미 활성 주문이 있으면 중복 제출 방지
        if (_activeOrderId != null) return;

        var ob = _ctx!.Market.GetLastOrderBookTop(sym);
        if (ob is null) return;

        // ask보다 약간 높은 가격으로 넣어서 체결 유도(또는 ask 이하로 넣어 미체결 테스트도 가능)
        var limit = ob.Value.BestAskPrice - 1000000;

        // 아주 작은 수량으로 테스트
        var qty = 0.0002m;

        _ = Task.Run(async () =>
        {
            var ack = await _ctx!.PlaceOrderAsync(new PlaceOrderRequest(
                Symbol: sym,
                Side: OrderSide.Buy,
                Type: OrderType.Limit,
                Quantity: qty,
                LimitPrice: limit,
                ClientOrderId: $"test-{DateTime.UtcNow:HHmmss}"
            ));

            if (!ack.Accepted || ack.OrderId is null)
            {
                Console.WriteLine($"[ALGO] order rejected: {ack.Message}");
                return;
            }

            _activeOrderId = ack.OrderId;
            Console.WriteLine($"[ALGO] placed LIMIT BUY {sym} qty={qty} px={limit} id={ack.OrderId}");

            // 상태 폴링(테스트 편의) - 이벤트 기반만으로도 가능하지만, v1은 폴링도 OK
            for (int i = 0; i < 50; i++)
            {
                var st = await _ctx.GetOrderAsync(ack.OrderId);
                if (st is null) break;

                if (st.Status is OrderStatus.Filled or OrderStatus.Canceled or OrderStatus.Rejected)
                {
                    Console.WriteLine(
                        $"[ALGO] done: {st.Status} filled={st.FilledQuantity}/{st.Quantity} avg={st.AvgFillPrice}");
                    _activeOrderId = null;
                    return;
                }

                await Task.Delay(200);
            }

            // 오래 걸리면 취소
            await _ctx.CancelAsync(ack.OrderId);
            _activeOrderId = null;
        });
    }
}