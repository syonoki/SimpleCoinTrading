using SimpleCoinTrading.Core;
using SimpleCoinTrading.Core.Algorithms;
using SimpleCoinTrading.Core.Broker;
using SimpleCoinTrading.Core.Data;
using SimpleCoinTrading.Core.Logs;

namespace SimpleCoinTrading.Server.Algorithms;

public sealed class PaperOrderTestAlgorithm : IAlgorithm
{
    public string Name => "PaperOrderTestAlgorithm";

    private IAlgorithmContext _ctx;
    private readonly List<IDisposable> _subs = new();

    private string? _activeOrderId;
    private IAlgorithmLogger _logger;

    public void Initialize(IAlgorithmContext ctx)
    {
        _ctx = ctx;
        _logger = ctx.GetLogger();
    }

    public void Run()
    {
        _subs.Add(_ctx.SubscribeBarClosed(OnBarClosed));
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
        var limit = ob.Value.BestAskPrice * 1.001m;

        // 아주 작은 수량으로 테스트
        var qty = 0.0002m;

        _ = Task.Run(async () =>
        {
            var ack = await _ctx!.PlaceOrderAsync(new PlaceOrderRequest(
                AlgorithmId: Name,
                Symbol: sym,
                Side: OrderSide.Buy,
                Type: OrderType.Limit,
                Quantity: qty,
                LimitPrice: limit,
                ClientOrderId: $"test-{DateTime.UtcNow:HHmmss}"
            ));

            if (!ack.Accepted || ack.OrderId is null)
            {
                _logger.Info($"[ALGO] order rejected: {ack.Message}");
                return;
            }

            _activeOrderId = ack.OrderId;
            _logger.Info($"[ALGO] placed LIMIT BUY {sym} qty={qty} px={limit} id={ack.OrderId}");

            // 상태 폴링(테스트 편의) - 이벤트 기반만으로도 가능하지만, v1은 폴링도 OK
            for (int i = 0; i < 50; i++)
            {
                var st = await _ctx.GetOrderAsync(ack.OrderId);
                if (st is null) break;

                if (st.Status is OrderStatus.Filled or OrderStatus.Canceled or OrderStatus.Rejected)
                {
                    _logger.Info(
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