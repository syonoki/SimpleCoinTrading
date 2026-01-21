using SimpleCoinTrading.Core.Broker;

namespace SimpleCoinTrading.Server.Services;

using Google.Protobuf.WellKnownTypes;
using Grpc.Core;


public sealed class TradingControlService : TradingControl.TradingControlBase
{
    private readonly TradingState _state;
    private readonly EventHub _hub;
    private readonly IBroker _broker;
    private readonly ILogger<TradingControlService> _logger;

    public TradingControlService(TradingState state, EventHub hub, IBroker broker, ILogger<TradingControlService> logger)
    {
        _state = state;
        _hub = hub;
        _broker = broker;
        _logger = logger;
    }

    public override Task<SnapshotResponse> GetSnapshot(GetSnapshotRequest request, ServerCallContext context)
    {
        var snapshot = _state.Snapshot();
        var resp = new SnapshotResponse
        {
            Seq = _state.CurrentSeq,
            TimeUtc = Timestamp.FromDateTime(DateTime.UtcNow),
            MarketDataOk = snapshot.MarketDataOk,
            MarketDataStatus = snapshot.MarketDataStatus,
            KillSwitchEnabled = snapshot.KillSwitchEnabled
        };
        
        resp.Orders.AddRange(snapshot.Orders.Select(Map));
        resp.Fills.AddRange(snapshot.RecentFills.Select(Map));
        resp.Positions.AddRange(snapshot.Positions.Select(Map));
        resp.Algorithms.AddRange(snapshot.Algorithms.Select(Map));
        
        return Task.FromResult(resp);
    }

    public override async Task SubscribeEvents(SubscribeEventsRequest request, IServerStreamWriter<ServerEvent> responseStream, ServerCallContext context)
    {
        _logger.LogInformation("SubscribeEvents started. AfterSeq={AfterSeq}", request.AfterSeq);
        var reader = _hub.Subscribe();

        try
        {
            while (await reader.WaitToReadAsync(context.CancellationToken))
            {
                while (reader.TryRead(out var ev))
                {
                    if (ev.Seq <= request.AfterSeq) continue;

                    try
                    {
                        await responseStream.WriteAsync(ev);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to write event to stream. Seq={Seq}", ev.Seq);
                        return; // 스트림이 닫혔거나 에러 발생 시 종료
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("SubscribeEvents canceled by client.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SubscribeEvents loop");
            throw;
        }
        finally
        {
            _hub.Unsubscribe(reader);
            _logger.LogInformation("SubscribeEvents finished and unsubscribed.");
        }
    }
    
    public override async Task<SetKillSwitchResponse> SetKillSwitch(SetKillSwitchRequest request, ServerCallContext context)
    {
        _logger.LogInformation("SetKillSwitch called: Enabled={Enabled}, CancelAll={CancelAll}", request.Enabled, request.CancelAll);
        var seq = _state.SetKillSwitch(request.Enabled);

        if (request.Enabled && request.CancelAll)
        {
            try
            {
                _logger.LogInformation("KillSwitch ON & CancelAll requested. Executing CancelAllAsync...");
                await _broker.CancelAllAsync(context.CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CancelAll failed on KillSwitch activation");
                var seq2 = _state.NextSeq();
                _hub.Publish(ServerEventFactory.FromSystem(seq2, "ERROR", $"CancelAll failed: {ex.Message}"));
            }
        }

        // 이벤트 발행을 마지막에 하여 취소 성공/실패 메시지 이후에 상태 변경 메시지가 오도록 함 (또는 순서 상관없음)
        _hub.Publish(ServerEventFactory.FromSystem(seq, "INFO",
            request.Enabled ? "KillSwitch ON" : "KillSwitch OFF"));

        return new SetKillSwitchResponse { Enabled = request.Enabled, Seq = seq };
    }


    private static SimpleCoinTrading.Server.Order Map(OrderState o) => new()
    {
        OrderId = o.OrderId,
        Symbol = o.Symbol,
        Side = o.Side.ToString().ToUpperInvariant(),
        Type = o.Type.ToString().ToUpperInvariant(),
        Status = o.Status.ToString().ToUpperInvariant(),
        Quantity = (double)o.Quantity,
        FilledQuantity = (double)o.FilledQuantity,
        LimitPrice = (double)(o.LimitPrice ?? 0m),
        AvgFillPrice = (double)(o.AvgFillPrice ?? 0m),
        UpdatedUtc = ToTimestamp(o.UpdatedUtc ?? o.CreatedUtc)
    };

    private static SimpleCoinTrading.Server.Fill Map(SimpleCoinTrading.Core.Broker.Fill f) => new()
    {
        OrderId = f.OrderId,
        Symbol = f.Symbol,
        Side = f.Side.ToString().ToUpperInvariant(),
        Price = (double)f.Price,
        Quantity = (double)f.Quantity,
        Fee = (double)f.Fee,
        FeeCurrency = f.FeeCurrency,
        TimeUtc = ToTimestamp(f.TimeUtc),
        TradeId = f.TradeId ?? ""
    };

    private static SimpleCoinTrading.Server.Position Map(SimpleCoinTrading.Core.Broker.Position p) => new()
    {
        Symbol = p.Symbol,
        Quantity = (double)p.Quantity,
        AvgPrice = (double)p.AvgPrice
    };

    private static SimpleCoinTrading.Server.AlgorithmState Map(AlgorithmRuntimeState a) => new()
    {
        Name = a.Name,
        Status = a.Status,
        Message = a.Message
    };

    private static Timestamp ToTimestamp(DateTime dt)
    {
        if (dt.Kind == DateTimeKind.Unspecified)
        {
            dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }
        else if (dt.Kind == DateTimeKind.Local)
        {
            dt = dt.ToUniversalTime();
        }

        return Timestamp.FromDateTime(dt);
    }
}
