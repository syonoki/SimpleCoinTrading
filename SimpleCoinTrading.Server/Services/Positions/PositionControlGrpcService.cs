using Grpc.Core;
using SimpleCoinTrading.Core.Positions;

namespace SimpleCoinTrading.Server.Services.Positions;
public static class PositionDtoMapper
{
    public static PositionDto ToDto(PositionState p) => new()
    {
        AlgorithmId = p.AlgorithmId ?? "UNKNOWN",
        Symbol = p.Symbol ?? "",
        NetQty = (double)p.NetQty,
        AvgPrice = (double)p.AvgPrice,
        LastPrice = (double)p.LastPrice,
        RealizedPnl = (double)p.RealizedPnl,
        UnrealizedPnl = (double)p.UnrealizedPnl,
        UpdatedAtUnixMs = p.UpdatedAt.ToUnixTimeMilliseconds()
    };
}
public class PositionControlGrpcService: PositionStateService.PositionStateServiceBase
{
    private readonly PositionProjection _projection;
    private readonly IPositionUpdateHub _hub;

    public PositionControlGrpcService(PositionProjection projection, IPositionUpdateHub hub)
    {
        _projection = projection;
        _hub = hub;
    }

    public override Task<GetPositionsResponse> GetSnapshot(GetPositionsRequest request, ServerCallContext context)
    {
        var algo = string.IsNullOrWhiteSpace(request.AlgorithmId) ? null : request.AlgorithmId;
        var list = _projection.Snapshot(algo);

        var resp = new GetPositionsResponse();
        resp.Positions.AddRange(list
            .Where(p => p.NetQty != 0m)                 // MVP: 0은 숨김
            .Select(PositionDtoMapper.ToDto));

        return Task.FromResult(resp);
    }

    public override async Task Subscribe(GetPositionsRequest request, IServerStreamWriter<PositionUpdate> responseStream, ServerCallContext context)
    {
        var algo = string.IsNullOrWhiteSpace(request.AlgorithmId) ? null : request.AlgorithmId;
        var reader = _hub.Subscribe(algo);

        while (await reader.WaitToReadAsync(context.CancellationToken))
        {
            while (reader.TryRead(out var upd))
            {
                await responseStream.WriteAsync(upd);
            }
        }
    }
}