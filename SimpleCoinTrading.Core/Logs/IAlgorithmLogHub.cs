using System.Threading.Channels;

namespace SimpleCoinTrading.Core.Logs;

public interface IAlgorithmLogHub
{
    void Write(AlgoLogEvent e);

    IReadOnlyList<AlgoLogEvent> GetRecent(string algorithmId, int limit);

    ChannelReader<AlgoLogEvent> Subscribe(string algorithmId);
}