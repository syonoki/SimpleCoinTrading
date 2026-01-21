using SimpleCoinTrading.Core.Positions;

namespace SimpleCoinTrading.Server.Services.Positions;

public class PositionProjectionHubBridge:IObserver<PositionChanged>, IDisposable
{
    private readonly IPositionUpdateHub _hub;
    private readonly IDisposable _sub;

    public PositionProjectionHubBridge(PositionProjection projection, IPositionUpdateHub hub)
    {
        _hub = hub;
        _sub = projection.Changes.Subscribe(this);
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