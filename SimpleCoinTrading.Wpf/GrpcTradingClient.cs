using Grpc.Core;
using SimpleCoinTrading.Server;

namespace SimpleCoinTrading.Wpf;

using Grpc.Net.Client;

public sealed class GrpcTradingClient
{
    private readonly TradingControl.TradingControlClient _orderClient;
    private readonly AlgorithmAdminService.AlgorithmAdminServiceClient _algoClient;

    public GrpcTradingClient(string address)
    {
        var channel = GrpcChannel.ForAddress(address);
        _orderClient = new TradingControl.TradingControlClient(channel);
        _algoClient = new AlgorithmAdminService.AlgorithmAdminServiceClient(channel);
    }

    public Task<SnapshotResponse> GetSnapshotAsync(CancellationToken ct)
        => _orderClient.GetSnapshotAsync(new GetSnapshotRequest(), cancellationToken: ct).ResponseAsync;

    public AsyncServerStreamingCall<ServerEvent> SubscribeEvents(long afterSeq, CancellationToken ct)
        => _orderClient.SubscribeEvents(new SubscribeEventsRequest { AfterSeq = afterSeq }, cancellationToken: ct);
    
    public async Task<SetKillSwitchResponse> SetKillSwitchAsync(bool enabled, bool cancelAll, CancellationToken ct)
        => await _orderClient.SetKillSwitchAsync(new SetKillSwitchRequest { Enabled = enabled, CancelAll = cancelAll }, cancellationToken: ct);

    public Task<ListAlgorithmsResponse> ListAlgorithmsAsync(CancellationToken ct)
        => _algoClient.ListAlgorithmsAsync(new ListAlgorithmsRequest(), cancellationToken: ct).ResponseAsync;

    public Task<StartAlgorithmResponse> StartAlgorithmAsync(string algorithmId, CancellationToken ct)
        => _algoClient.StartAlgorithmAsync(new StartAlgorithmRequest { AlgorithmId = algorithmId }, cancellationToken: ct).ResponseAsync;

    public Task<StopAlgorithmResponse> StopAlgorithmAsync(string algorithmId, CancellationToken ct)
        => _algoClient.StopAlgorithmAsync(new StopAlgorithmRequest { AlgorithmId = algorithmId }, cancellationToken: ct).ResponseAsync;
}
