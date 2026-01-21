using Grpc.Core;
using SimpleCoinTrading.Server;

namespace SimpleCoinTrading.Wpf;

using Grpc.Net.Client;

public sealed class GrpcTradingClient
{
    private readonly TradingControl.TradingControlClient _client;

    public GrpcTradingClient(string address)
    {
        var channel = GrpcChannel.ForAddress(address);
        _client = new TradingControl.TradingControlClient(channel);
    }

    public Task<SnapshotResponse> GetSnapshotAsync(CancellationToken ct)
        => _client.GetSnapshotAsync(new GetSnapshotRequest(), cancellationToken: ct).ResponseAsync;

    public AsyncServerStreamingCall<ServerEvent> SubscribeEvents(long afterSeq, CancellationToken ct)
        => _client.SubscribeEvents(new SubscribeEventsRequest { AfterSeq = afterSeq }, cancellationToken: ct);
    
    public async Task<SetKillSwitchResponse> SetKillSwitchAsync(bool enabled, bool cancelAll, CancellationToken ct)
        => await _client.SetKillSwitchAsync(new SetKillSwitchRequest { Enabled = enabled, CancelAll = cancelAll }, cancellationToken: ct);
}
