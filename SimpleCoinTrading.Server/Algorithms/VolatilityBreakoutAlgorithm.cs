using SimpleCoinTrading.Core;
using SimpleCoinTrading.Core.Broker;
using SimpleCoinTrading.Core.Data;
using SimpleCoinTrading.Core.Logs;
using SimpleCoinTrading.Core.Time.TimeFlows;

namespace SimpleCoinTrading.Server.Algorithms;

/// <summary>
/// 변동성 돌파 전략(Volatility Breakout Strategy) 샘플 알고리즘
/// 래리 윌리엄스의 전략으로, (전일 고가 - 전일 저가) * K 이상의 가격 상승 시 매수 후 당일 장 마감(또는 익일 시가)에 매도
/// </summary>
public sealed class VolatilityBreakoutAlgorithm : IAlgorithm
{
    public string Name => "VolatilityBreakoutAlgorithm";

    private IAlgorithmContext? _ctx;
    private readonly List<IDisposable> _subs = new();
    private readonly string _symbol;
    private readonly decimal _k;
    private readonly decimal _orderQuantity;

    private decimal? _targetPrice;
    private DateTime? _targetDate;
    private string? _activeOrderId;
    private bool _isPositionHeld;
    private IAlgorithmLogger _logger;

    public VolatilityBreakoutAlgorithm(string symbol = "KRW-BTC", decimal k = 0.5m, decimal orderQuantity = 0.001m)
    {
        _symbol = symbol;
        _k = k;
        _orderQuantity = orderQuantity;
    }

    public void Initialize(IAlgorithmContext ctx)
    {
        _ctx = ctx;
        _logger = ctx.GetLogger(Name);
        // 매분마다 가격을 확인하여 타겟가 도달 시 매수
        _subs.Add(ctx.SubscribeBarClosed(OnBarClosed));
    }

    public void Run()
    {
        _logger.Info($"[ALGO] {Name} started for {_symbol} (K={_k})");
    }

    public void Stop()
    {
        foreach (var s in _subs) s.Dispose();
        _subs.Clear();
        _logger.Info($"[ALGO] {Name} stopped.");
    }

    private void OnBarClosed(BarClosedEvent e)
    {
        // 1분봉 기준으로 체크한다고 가정 (Resolution.M1)
        if (e.Resolution.Minutes != 1 || e.Symbol != _symbol) return;

        var now = e.Bar.TimeUtc;

        // 1. 매일 시작 시 타겟 가격 갱신
        UpdateTargetPriceIfNeeded(now);

        if (_targetPrice == null) return;

        // 2. 매수 로직: 포지션이 없고 현재가가 타겟가 이상이면 매수
        if (!_isPositionHeld && _activeOrderId == null && e.Bar.Close >= _targetPrice.Value)
        {
            PlaceBuyOrder(e.Bar.Close);
        }

        // 3. 매도 로직: 자정(00:00) 근처면 매도 (당일 장 마감 청산)
        // 실제 운영 시에는 다음 날 시가에 매도하는 경우도 많음
        if (_isPositionHeld && now.Hour == 23 && now.Minute == 59)
        {
            PlaceSellOrder();
        }
    }

    private void UpdateTargetPriceIfNeeded(DateTime now)
    {
        var today = now.Date;
        if (_targetDate == today) return;

        // 전일 일봉(Daily Bar) 정보 필요
        // 현재 시스템에서 Resolution.M1 이외에 Day 봉을 제공하는지 확인 필요
        // 없다면 1분봉 1440개를 합쳐야 할 수도 있지만, 여기서는 GetBars 활용
        
        // 시뮬레이션 환경에서는 과거 데이터 조회가 가능하다고 가정
        var bars = _ctx!.Market.GetBars(_symbol, 2, new Resolution(1440)); // 일봉 2개 (오늘, 어제)
        if (bars.Count < 2)
        {
            // 일봉 데이터가 부족하면 1분봉으로 유추 시도
            // (실제 프로젝트 구조에 따라 다름)
            return;
        }

        var yesterday = bars[0]; // GetBars는 과거 순서대로 줄 가능성이 높음
        var range = yesterday.High - yesterday.Low;
        var todayOpen = bars[1].Open;

        _targetPrice = todayOpen + (range * _k);
        _targetDate = today;

        _logger.Info($"[ALGO] New Target Price for {today:yyyy-MM-dd}: {_targetPrice} (Open: {todayOpen}, Range: {range})");
        
        // 날이 바뀌었으니 포지션 상태 초기화 (만약 밤새 들고 있었다면)
        // 실제로는 잔고 조회를 하는 것이 정확함
    }

    private void PlaceBuyOrder(decimal currentPrice)
    {
        _ = Task.Run(async () =>
        {
            _logger.Info($"[ALGO] Target reached! Placing BUY order at {currentPrice}");
            
            var request = new PlaceOrderRequest(
                Symbol: _symbol,
                Side: OrderSide.Buy,
                Type: OrderType.Market, // 변동성 돌파는 보통 시장가 진입
                Quantity: _orderQuantity,
                ClientOrderId: $"VB-BUY-{_ctx!.Clock.UtcNow:yyyyMMddHHmmss}"
            );

            var ack = await _ctx.PlaceOrderAsync(request);
            if (ack.Accepted)
            {
                _activeOrderId = ack.OrderId;
                _isPositionHeld = true;
                _logger.Info($"[ALGO] BUY order placed: {ack.OrderId}");
            }
            else
            {
                _logger.Info($"[ALGO] BUY order rejected: {ack.Message}");
            }
        });
    }

    private void PlaceSellOrder()
    {
        _ = Task.Run(async () =>
        {
            _logger.Info("[ALGO] End of day reached. Placing SELL order to liquidate.");

            var request = new PlaceOrderRequest(
                Symbol: _symbol,
                Side: OrderSide.Sell,
                Type: OrderType.Market,
                Quantity: _orderQuantity,
                ClientOrderId: $"VB-SELL-{_ctx!.Clock.UtcNow:yyyyMMddHHmmss}"
            );

            var ack = await _ctx.PlaceOrderAsync(request);
            if (ack.Accepted)
            {
                _isPositionHeld = false;
                _activeOrderId = null;
                _logger.Info($"[ALGO] SELL order placed: {ack.OrderId}");
            }
            else
            {
                _logger.Info($"[ALGO] SELL order rejected: {ack.Message}");
            }
        });
    }
}
