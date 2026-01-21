namespace SimpleCoinTrading.Core.Broker;

public interface IBroker
{
    /// <summary>브로커 이름/식별자 (예: "BithumbLive", "Paper", "Backtest")</summary>
    string Name { get; }

    /// <summary>브로커 연결/초기화 (API 키 로드, 세션 준비 등)</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>브로커 정지/정리 (연결 종료, 남은 작업 정리)</summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>주문 제출 (시장가/지정가 등)</summary>
    Task<OrderAck> PlaceOrderAsync(PlaceOrderRequest req, CancellationToken ct = default);

    /// <summary>주문 취소</summary>
    Task<CancelAck> CancelOrderAsync(CancelOrderRequest req, CancellationToken ct = default);

    /// <summary>단일 주문 조회(가장 최근 상태)</summary>
    Task<OrderState?> GetOrderAsync(string orderId, CancellationToken ct = default);

    /// <summary>미체결(활성) 주문 조회</summary>
    Task<IReadOnlyList<OrderState>> GetOpenOrdersAsync(string symbol, CancellationToken ct = default);

    /// <summary>보유/포지션 조회 (현물 기준: 보유 수량/평단)</summary>
    Task<Position?> GetPositionAsync(string symbol, CancellationToken ct = default);

    /// <summary>계좌 잔고 조회(현물: KRW 및 코인 잔고)</summary>
    Task<AccountSnapshot> GetAccountAsync(CancellationToken ct = default);

    /// <summary>
    /// 브로커 이벤트 스트림:
    /// - 주문 상태 변경(접수/부분체결/완료/취소/거부)
    /// - 체결(Fill)
    /// - 에러/경고
    /// </summary>
    IObservable<BrokerEvent> Events { get; }

    Task CancelAllAsync(CancellationToken contextCancellationToken);
}
