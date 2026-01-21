using SimpleCoinTrading.Core.Data;
using SimpleCoinTrading.Data;
using Xunit;

namespace SimpleCoinTrading.Core.Tests;

public class CsvBarParserTests
{
    private const string StandardHeader = "candle_date_time_utc,candle_date_time_kst,opening_price,high_price,low_price,trade_price,candle_acc_trade_volume";

    [Fact]
    public void InitFromHeader_ShouldInitializeColumns()
    {
        var parser = new CsvBarParser();
        parser.InitFromHeader(StandardHeader);
        
        // TryParseBar should return false for empty or invalid lines instead of failing because of uninitialized state
        bool result = parser.TryParseBar("", out _);
        Assert.False(result);
    }

    [Fact]
    public void TryParseBar_WithUtcTime_ShouldWork()
    {
        var parser = new CsvBarParser();
        parser.InitFromHeader(StandardHeader);
        
        string line = "2026-01-15T10:00:00Z,2026-01-15T19:00:00,50000,51000,49000,50500,1.5";
        bool result = parser.TryParseBar(line, out Bar bar);
        
        Assert.True(result);
        Assert.Equal(new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc), bar.TimeUtc);
        Assert.Equal(50000m, bar.Open);
        Assert.Equal(51000m, bar.High);
        Assert.Equal(49000m, bar.Low);
        Assert.Equal(50500m, bar.Close);
        Assert.Equal(1.5m, bar.Volume);
    }

    [Fact]
    public void TryParseBar_WithKstTime_ShouldConvertToUtc()
    {
        var parser = new CsvBarParser();
        // UTC 컬럼 없이 KST 컬럼만 있는 경우 테스트를 위해 헤더 조정
        string header = "candle_date_time_kst,opening_price,high_price,low_price,trade_price,candle_acc_trade_volume";
        parser.InitFromHeader(header);
        
        string line = "2026-01-15 19:00:00,50000,51000,49000,50500,1.5";
        bool result = parser.TryParseBar(line, out Bar bar);
        
        Assert.True(result);
        // KST 19:00 is UTC 10:00
        Assert.Equal(new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc), bar.TimeUtc);
    }

    [Fact]
    public void TryParseBar_WithQuotedFields_ShouldWork()
    {
        var parser = new CsvBarParser();
        parser.InitFromHeader(StandardHeader);
        
        string line = "\"2026-01-15T10:00:00Z\",\"2026-01-15T19:00:00\",\"50,000\",\"51,000\",\"49,000\",\"50,500\",\"1.5\"";
        // Note: TryGetDecimal uses InvariantCulture and ko-KR. "50,000" might be parsed as 50000 or 50 depending on culture.
        // In InvariantCulture, it might fail or parse as 50. But CsvBarParser uses NumberStyles.Any.
        
        bool result = parser.TryParseBar(line, out Bar bar);
        
        Assert.True(result);
        Assert.Equal(50000m, bar.Open);
    }

    [Fact]
    public void TryParseBar_ShouldReturnFalse_WhenNotInitialized()
    {
        var parser = new CsvBarParser();
        string line = "2026-01-15T10:00:00Z,2026-01-15T19:00:00,50000,51000,49000,50500,1.5";
        
        bool result = parser.TryParseBar(line, out _);
        
        Assert.False(result);
    }

    [Fact]
    public void TryParseBar_ShouldHandleMissingColumns()
    {
        var parser = new CsvBarParser();
        parser.InitFromHeader("candle_date_time_utc,opening_price"); // missing other required columns
        
        string line = "2026-01-15T10:00:00Z,50000";
        bool result = parser.TryParseBar(line, out _);
        
        Assert.False(result);
    }
}
