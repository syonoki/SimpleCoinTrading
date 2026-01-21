namespace SimpleCoinTrading.Core.Logs;

using System.Collections.Concurrent;
using System.Text;

public sealed class AlgorithmFileLogSink : IAlgorithmLogSink
{
    private readonly string _baseDir;
    private readonly ConcurrentDictionary<string, object> _locks = new();

    public AlgorithmFileLogSink(string baseDir)
    {
        _baseDir = baseDir;
        Directory.CreateDirectory(_baseDir);
    }

    public void Write(AlgoLogEvent e)
    {
        var algoId = Sanitize(e.AlgorithmId);
        var date = e.Time.ToString("yyyyMMdd");
        var path = Path.Combine(_baseDir, $"algo-{algoId}-{date}.log");

        var line = Format(e);

        // 알고리즘별 파일 lock (단순/안전)
        var gate = _locks.GetOrAdd(algoId, _ => new object());
        lock (gate)
        {
            File.AppendAllText(path, line, Encoding.UTF8);
        }
    }

    private static string Format(AlgoLogEvent e)
    {
        // 한 줄, 파싱 쉬운 포맷
        return $"{e.Time:O} [{e.Level}] {e.Message}"
               + (e.Symbol is not null ? $" symbol={e.Symbol}" : "")
               + (e.ClientOrderId is not null ? $" cid={e.ClientOrderId}" : "")
               + (e.OrderId is not null ? $" oid={e.OrderId}" : "")
               + Environment.NewLine;
    }

    private static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s;
    }
}
