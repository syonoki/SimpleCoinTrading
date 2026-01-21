using Grpc.Core;
using SimpleCoinTrading.Core.Logs;

namespace SimpleCoinTrading.Server.Services;

public class AlgoLogGrpcService: AlgoLogService.AlgoLogServiceBase
{
    private readonly IAlgorithmLogHub _hub;

    public AlgoLogGrpcService(IAlgorithmLogHub hub) => _hub = hub;

    public override Task<GetAlgoLogsResponse> GetRecent(GetAlgoLogsRequest request, ServerCallContext context)
    {
        var limit = request.Limit <= 0 ? 500 : request.Limit;
        var logs = _hub.GetRecent(request.AlgorithmId, limit);

        var resp = new GetAlgoLogsResponse();
        resp.Logs.AddRange(logs.Select(ToDto));
        return Task.FromResult(resp);
    }

    public override async Task Subscribe(SubscribeAlgoLogsRequest request, IServerStreamWriter<AlgoLogDto> responseStream, ServerCallContext context)
    {
        var reader = _hub.Subscribe(request.AlgorithmId);

        try
        {
            while (await reader.WaitToReadAsync(context.CancellationToken))
            {
                while (reader.TryRead(out var e))
                {
                    await responseStream.WriteAsync(ToDto(e));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 클라이언트 연결 종료 시 발생하는 정상적인 예외
        }
        finally
        {
            _hub.Unsubscribe(request.AlgorithmId, reader);
        }
    }

    private static AlgoLogDto ToDto(AlgoLogEvent e) => new()
    {
        TimeUnixMs = e.Time.ToUnixTimeMilliseconds(),
        AlgorithmId = e.AlgorithmId ?? "",
        Level = (AlgoLogLevel)(int)e.Level,
        Message = e.Message ?? "",
        Symbol = e.Symbol ?? "",
        ClientOrderId = e.ClientOrderId ?? "",
        OrderId = e.OrderId ?? ""
    };
}