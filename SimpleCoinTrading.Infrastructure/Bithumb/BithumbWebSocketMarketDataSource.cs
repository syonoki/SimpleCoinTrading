using System.Buffers;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SimpleCoinTrading.Core.Data;

namespace SimpleCoinTrading.Data;

/// <summary>
/// 빗썸 Public WebSocket에서 Trade/Orderbook 데이터를 받아 MarketPipeline으로 전달.
/// - Endpoint: wss://ws-api.bithumb.com/websocket/v1
/// - Subscribe msg example: [{"ticket":"test"},{"type":"trade","codes":["KRW-BTC"]},{"format":"DEFAULT"}]
/// </summary>
public sealed class BithumbWebSocketMarketDataSource : IAsyncDisposable,IMarketDataSource
{
    private static readonly Uri WsUri = new("wss://ws-api.bithumb.com/websocket/v1");

    private readonly MarketPipeline _pipeline;
    private readonly string[] _codes;         // e.g. "KRW-BTC"
    private readonly bool _subscribeTrade;
    private readonly bool _subscribeOrderBook;

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _runTask;

    public BithumbWebSocketMarketDataSource(
        MarketPipeline pipeline,
        IEnumerable<string> codes,
        bool subscribeTrade = true,
        bool subscribeOrderBook = true)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _codes = (codes ?? throw new ArgumentNullException(nameof(codes))) is string[] a ? a : new List<string>(codes).ToArray();
        if (_codes.Length == 0) throw new ArgumentException("codes is empty.", nameof(codes));

        _subscribeTrade = subscribeTrade;
        _subscribeOrderBook = subscribeOrderBook;
        if (!_subscribeTrade && !_subscribeOrderBook)
            throw new ArgumentException("At least one of trade/orderbook must be subscribed.");
    }

    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (_runTask != null) throw new InvalidOperationException("Already started.");
            
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runTask = Task.Run(() => RunLoopAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cts == null) return;

        try { _cts.Cancel(); } catch { /* ignore */ }

        if (_runTask != null)
        {
            try { await _runTask.ConfigureAwait(false); } catch { /* ignore */ }
        }

        _runTask = null;
        _cts.Dispose();
        _cts = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _ws?.Dispose();
        _ws = null;
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        // 재연결 backoff (너무 공격적으로 재구독하면 WS rate-limit/차단 위험)
        var backoff = TimeSpan.FromSeconds(1);
        var maxBackoff = TimeSpan.FromSeconds(30);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAndConsumeAsync(ct).ConfigureAwait(false);

                // 정상 종료(서버 close 등)면 backoff 초기화 후 즉시 재시도
                backoff = TimeSpan.FromSeconds(1);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BithumbWS] Error: {ex.GetType().Name}: {ex.Message}");
            }

            // backoff sleep
            if (!ct.IsCancellationRequested)
            {
                await Task.Delay(backoff, ct).ConfigureAwait(false);
                var next = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, maxBackoff.TotalSeconds));
                backoff = next;
            }
        }
    }

    private async Task ConnectAndConsumeAsync(CancellationToken ct)
    {
        using var ws = new ClientWebSocket();
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

        Console.WriteLine($"[BithumbWS] Connecting to {WsUri} ...");
        await ws.ConnectAsync(WsUri, ct).ConfigureAwait(false);
        Console.WriteLine("[BithumbWS] Connected.");

        _ws = ws;

        // 구독 요청 전송 (주의: 요청 전송 rate limit 존재. 재연결 시 1회만 전송)
        var subscribeJson = BuildSubscribeMessage(ticket: $"bot-{Guid.NewGuid():N}");
        await SendTextAsync(ws, subscribeJson, ct).ConfigureAwait(false);

        // 수신 루프
        var receiveLoop = ReceiveLoopAsync(ws, ct);
        var flushLoop = PeriodicFlushLoopAsync(ct);

        await Task.WhenAny(receiveLoop, flushLoop).ConfigureAwait(false);
    }

    private async Task PeriodicFlushLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _pipeline.FlushBars();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BithumbWS] Flush error: {ex.Message}");
            }
            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
        }
    }

    private string BuildSubscribeMessage(string ticket)
    {
        // 공식 예시 형태(배열) 기반: ticket / type / format
        // 여러 type field를 한 요청에 넣을 수 있음(문서 설명). :contentReference[oaicite:4]{index=4}
        var msg = new List<object>
        {
            new Dictionary<string, object> { ["ticket"] = ticket }
        };

        if (_subscribeTrade)
        {
            msg.Add(new Dictionary<string, object>
            {
                ["type"] = "trade",
                ["codes"] = _codes,
                // 필요하면 아래 옵션도 넣을 수 있음(문서에 isOnlySnapshot/isOnlyRealtime 언급)
                // ["isOnlyRealtime"] = true
            });
        }

        if (_subscribeOrderBook)
        {
            msg.Add(new Dictionary<string, object>
            {
                ["type"] = "orderbook",
                ["codes"] = _codes,
                // ["isOnlyRealtime"] = true
            });
        }

        msg.Add(new Dictionary<string, object> { ["format"] = "DEFAULT" });

        return JsonSerializer.Serialize(msg);
    }

    private static async Task SendTextAsync(ClientWebSocket ws, string text, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken: ct).ConfigureAwait(false);
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            var sb = new StringBuilder();

            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                sb.Clear();
                WebSocketReceiveResult? result;

                do
                {
                    result = await ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Console.WriteLine($"[BithumbWS] Close received: {ws.CloseStatus} {ws.CloseStatusDescription}");
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", ct).ConfigureAwait(false);
                        return;
                    }

                    var chunk = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    sb.Append(chunk);
                }
                while (!result.EndOfMessage);

                var jsonText = sb.ToString();
                if (string.IsNullOrWhiteSpace(jsonText))
                    continue;

                HandleMessage(jsonText);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void HandleMessage(string jsonText)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            // 많은 WS는 {"type":"..."} 형태
            if (!TryGetString(root, "type", out var type))
            {
                // 혹시 응답/에러/ack 형태일 수 있음. 필요하면 로그로 확인.
                // Console.WriteLine($"[BithumbWS] Unknown msg(no type): {jsonText}");
                return;
            }

            switch (type)
            {
                case "trade":
                    HandleTrade(root);
                    break;

                case "orderbook":
                    HandleOrderBook(root);
                    break;

                default:
                    // ticker 등 다른 타입이 올 수 있음
                    // Console.WriteLine($"[BithumbWS] Ignored type={type}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BithumbWS] Parse error: {ex.Message}");
            // 문제 분석을 위해 원문을 잠깐 찍고 싶으면 아래 주석 해제
            // Console.WriteLine(jsonText);
        }
    }

    private void HandleTrade(JsonElement root)
    {
        // 예상 필드(공식/예시 기반 + 일반적인 빗썸/업비트 계열): code, trade_timestamp(ms), trade_price, trade_volume, ask_bid
        if (!TryGetString(root, "code", out var code))
            return;

        // time: trade_timestamp(ms) or timestamp
        var timeUtc = DateTime.UtcNow;
        if (TryGetInt64(root, "trade_timestamp", out var ms) || TryGetInt64(root, "timestamp", out ms))
            timeUtc = SafeFromUnixMs(ms);

        // price/qty
        if (!TryGetDecimal(root, "trade_price", out var price))
            TryGetDecimal(root, "price", out price);

        if (!TryGetDecimal(root, "trade_volume", out var qty))
            TryGetDecimal(root, "volume", out qty);

        // side
        var isBuy = false;
        if (TryGetString(root, "ask_bid", out var askBid))
        {
            // 흔히 "ASK"/"BID" 또는 "SELL"/"BUY" 형태
            if (askBid.Equals("BID", StringComparison.OrdinalIgnoreCase) || askBid.Equals("BUY", StringComparison.OrdinalIgnoreCase))
                isBuy = true; //
            else if (askBid.Equals("ASK", StringComparison.OrdinalIgnoreCase) || askBid.Equals("SELL", StringComparison.OrdinalIgnoreCase))
                isBuy = false;
        }

        var tick = new TradeTick(timeUtc, price, qty, isBuy);

        // 너의 파이프라인 입력
        _pipeline.IngestTrade(code, tick);
    }

    private void HandleOrderBook(JsonElement root)
    {
        if (!TryGetString(root, "code", out var code))
            return;

        var timeUtc = DateTime.UtcNow;
        if (TryGetInt64(root, "timestamp", out var ms))
            timeUtc = SafeFromUnixMs(ms);

        // orderbook_units: [{ask_price, bid_price, ask_size, bid_size}, ...]
        if (!root.TryGetProperty("orderbook_units", out var units) || units.ValueKind != JsonValueKind.Array)
            return;

        using var enumerator = units.EnumerateArray();
        if (!enumerator.MoveNext())
            return;

        var top = enumerator.Current;

        if (!TryGetDecimal(top, "ask_price", out var ask) || !TryGetDecimal(top, "bid_price", out var bid))
            return;

        TryGetDecimal(top, "ask_size", out var askSize);
        TryGetDecimal(top, "bid_size", out var bidSize);

        var tob = new OrderBookTop(timeUtc, bid, bidSize, ask, askSize);

        _pipeline.IngestOrderBookTop(code, tob);
    }

    private static DateTime SafeFromUnixMs(long ms)
    {
        // DateTimeOffset valid range: -62,135,596,800,000 to 253,402,300,799,999 ms
        // Current time is around 1,700,000,000,000 ms.
        // If ms > 2e14, it's likely microseconds (1.7e15) or something else.
        if (ms > 253402300799999L)
        {
            // Try as microseconds
            ms /= 1000;
        }

        if (ms < -62135596800000L || ms > 253402300799999L)
        {
            return DateTime.UtcNow;
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
    }

    // =========================
    // JSON helpers (defensive)
    // =========================
    private static bool TryGetString(JsonElement obj, string name, out string value)
    {
        value = "";
        if (!obj.TryGetProperty(name, out var p)) return false;
        if (p.ValueKind == JsonValueKind.String) { value = p.GetString() ?? ""; return true; }
        return false;
    }

    private static bool TryGetInt64(JsonElement obj, string name, out long value)
    {
        value = 0;
        if (!obj.TryGetProperty(name, out var p)) return false;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out value)) return true;
        if (p.ValueKind == JsonValueKind.String && long.TryParse(p.GetString(), out value)) return true;
        return false;
    }

    private static bool TryGetDecimal(JsonElement obj, string name, out decimal value)
    {
        value = 0m;
        if (!obj.TryGetProperty(name, out var p)) return false;

        if (p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out value)) return true;

        if (p.ValueKind == JsonValueKind.String)
        {
            var s = p.GetString();
            if (s != null && decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
                return true;
        }
        return false;
    }

}