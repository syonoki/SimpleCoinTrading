using SimpleCoinTrading.Core.Algorithms;

namespace SimpleCoinTrading.Server.Algorithms;

public class PaperOrderAlgorithmFactory:IAlgorithmFactory
{
    public string Key  => "PaperOrderAlgorithm";
    public IAlgorithm Create(string instanceId, IReadOnlyDictionary<string, object> @params)
    {
        var symbol = @params["symbol"].ToString();
        return new PaperOrderTestAlgorithm(instanceId, symbol);
    }
}

public class VolatilityBreakoutAlgorithmFactory:IAlgorithmFactory
{
    public string Key  => "VolatilityBreakoutAlgorithm";
    public IAlgorithm Create(string instanceId, IReadOnlyDictionary<string, object> @params)
    {
        var symbol = @params["symbol"].ToString();
        return new VolatilityBreakoutAlgorithm(instanceId, symbol);
    }
}