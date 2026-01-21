using System.Globalization;
using System.Text;
using SimpleCoinTrading.Core.Data;
using SimpleCoinTrading.Data;

namespace SimpleCoinTrading;

// ---------------- CSV Parsing ----------------
    // Supports: bithumb collector-like columns:
    // candle_date_time_utc, candle_date_time_kst, opening_price, high_price, low_price, trade_price,
    // candle_acc_trade_volume (or volume-like)
    public sealed class CsvBarParser
    {
        private readonly Dictionary<string, int> _col = new(StringComparer.OrdinalIgnoreCase);
        private bool _initialized;

        public void InitFromHeader(string headerLine)
        {
            _col.Clear();
            var cols = SplitCsvLine(headerLine);
            for (int i = 0; i < cols.Count; i++)
            {
                var name = cols[i].Trim();
                if (!_col.ContainsKey(name)) _col[name] = i;
            }
            _initialized = true;
        }

        public bool TryParseBar(string line, out Bar bar)
        {
            bar = default;
            if (!_initialized) return false;

            var cols = SplitCsvLine(line);
            if (cols.Count == 0) return false;

            // time: prefer candle_date_time_utc, else parse kst and convert to utc (assume +09:00 if no tz)
            DateTime timeUtc;
            if (TryGet(cols, "candle_date_time_utc", out var tUtcStr) && TryParseAnyDateTimeToUtc(tUtcStr, out timeUtc))
            {
                // ok
            }
            else if (TryGet(cols, "candle_date_time_kst", out var tKstStr) && TryParseKstToUtc(tKstStr, out timeUtc))
            {
                // ok
            }
            else
            {
                return false;
            }

            if (!TryGetDecimal(cols, "opening_price", out var open)) return false;
            if (!TryGetDecimal(cols, "high_price", out var high)) return false;
            if (!TryGetDecimal(cols, "low_price", out var low)) return false;
            if (!TryGetDecimal(cols, "trade_price", out var close)) return false;

            decimal vol = 0m;
            // prefer candle_acc_trade_volume
            if (!TryGetDecimal(cols, "candle_acc_trade_volume", out vol))
            {
                // fallback common names
                TryGetDecimal(cols, "volume", out vol);
            }

            bar = new Bar(timeUtc, open, high, low, close, vol);
            return true;
        }

        private bool TryGet(IReadOnlyList<string> cols, string name, out string value)
        {
            value = "";
            if (!_col.TryGetValue(name, out var idx)) return false;
            if (idx < 0 || idx >= cols.Count) return false;
            value = cols[idx].Trim();
            return !string.IsNullOrEmpty(value);
        }

        private bool TryGetDecimal(IReadOnlyList<string> cols, string name, out decimal value)
        {
            value = 0m;
            if (!TryGet(cols, name, out var s)) return false;
            return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value)
                   || decimal.TryParse(s, NumberStyles.Any, CultureInfo.GetCultureInfo("ko-KR"), out value);
        }

        private static bool TryParseAnyDateTimeToUtc(string s, out DateTime utc)
        {
            // ISO8601 or "yyyy-MM-dd HH:mm:ss" etc.
            utc = default;

            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
            {
                utc = dto.UtcDateTime;
                return true;
            }

            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
            {
                // if no tz, treat as UTC
                utc = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                return true;
            }

            return false;
        }

        private static bool TryParseKstToUtc(string s, out DateTime utc)
        {
            utc = default;

            // If it has offset, DateTimeOffset.Parse handles it.
            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
            {
                // If string had no offset, dto may assume local; we want KST
                // Safer: detect offset presence
                if (s.Contains("+") || s.Contains("Z"))
                {
                    utc = dto.UtcDateTime;
                    return true;
                }
            }

            // No timezone included -> assume KST (+09:00)
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                var kst = new DateTimeOffset(dt, TimeSpan.FromHours(9));
                utc = kst.UtcDateTime;
                return true;
            }

            return false;
        }

        private static List<string> SplitCsvLine(string line)
        {
            // minimal CSV splitter supporting quoted fields
            var result = new List<string>();
            if (string.IsNullOrEmpty(line)) return result;

            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }

            result.Add(sb.ToString());
            return result;
        }
    }
