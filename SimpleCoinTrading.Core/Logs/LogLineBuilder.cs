namespace SimpleCoinTrading.Core.Logs;

public static class AlgoLogFmt
{
    public static string Line(
        string tag,
        string? algoId = null,
        string? symbol = null,
        string? clientOrderId = null,
        string? orderId = null,
        string? msg = null,
        params (string Key, object? Value)[] extra)
    {
        static string Esc(object? v) => v is null ? "" : v.ToString()!.Replace(" ", "_");

        var parts = new List<string>(8) { tag };

        if (!string.IsNullOrWhiteSpace(algoId))        parts.Add($"algo={Esc(algoId)}");
        if (!string.IsNullOrWhiteSpace(symbol))        parts.Add($"sym={Esc(symbol)}");
        if (!string.IsNullOrWhiteSpace(clientOrderId)) parts.Add($"cid={Esc(clientOrderId)}");
        if (!string.IsNullOrWhiteSpace(orderId))       parts.Add($"oid={Esc(orderId)}");
        if (!string.IsNullOrWhiteSpace(msg))           parts.Add($"msg={Esc(msg)}");

        foreach (var (k, v) in extra)
            if (!string.IsNullOrWhiteSpace(k) && v is not null)
                parts.Add($"{k}={Esc(v)}");

        return string.Join(' ', parts);
    }
}