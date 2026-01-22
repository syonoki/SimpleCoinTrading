using Microsoft.Extensions.Hosting;
using SimpleCoinTrading.Core.Positions;

namespace SimpleCoinTrading.Server.Services.Positions;

public class PositionProjectionHubBridge:IObserver<PositionChanged>, IDisposable, IHostedService
{
    private readonly IPositionUpdateHub _hub;
    private readonly PositionProjection _projection;
    private IDisposable? _sub;

    public PositionProjectionHubBridge(PositionProjection projection, IPositionUpdateHub hub)
    {
        _projection = projection;
        _hub = hub;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sub = _projection.Changes.Subscribe(this);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sub?.Dispose();
        _sub = null;
        return Task.CompletedTask;
    }
    
    public void OnCompleted()
    {
        
    }

    public void OnError(Exception error)
    {
        
    }

    public void OnNext(PositionChanged value)
    {
        var p = value.Position;

        if (value.Removed)
            _hub.PublishRemove(p.AlgorithmId, p.Symbol);
        else
            _hub.PublishUpsert(p);
    }


    public void Dispose()
    {
        // TODO release managed resources here
        _sub.Dispose();
    }
}