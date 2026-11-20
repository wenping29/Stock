using System.Globalization;
using System.Text.RegularExpressions;
using FundAnalysis.Data;
using FundAnalysis.Models;
using FundAnalysis.Services.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FundAnalysis.Analysis;

/// <summary>
/// 为量化分析接口提供统一 K 线读取：优先读库，缺失/过期时回源东财并落库。
/// 日线复用 StockDailies / IndexDailies，多周期在查询时聚合；60 分钟线按需拉取。
/// </summary>
public partial class AnalysisDataService(AppDbContext db, IQuoteProvider quotes, ILogger<AnalysisDataService> logger)
{
    public static readonly string[] Periods = ["60min", "daily", "weekly", "monthly", "quarterly", "yearly"];

    public static DateTime ParseDate(string? value, DateTime fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var formats = new[] { "yyyy-MM-dd", "yyyyMMdd", "yyyy-M-d", "yyyy/MM/dd" };
        if (DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.Date;
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d.Date : fallback;
    }

    public static string Format(AnalysisKline k) => k.Date;

    public static bool IsIndexCode(string code) => IndexCodeRegex().IsMatch(code);

    [GeneratedRegex(@"^(sh|sz|bj)\d{6}$", RegexOptions.IgnoreCase)]
    private static partial Regex IndexCodeRegex();

    public async Task<List<AnalysisKline>> GetKlineAsync(
        string code, string? start, string? end, string? period = "daily", CancellationToken ct = default)
    {
        var startDate = ParseDate(start, new DateTime(2024, 1, 1));
        var endDate = ParseDate(end, DateTime.Today);
        if (startDate > endDate) (startDate, endDate) = (endDate, startDate);
        period = NormalizePeriod(period);

        if (period == "60min")
            return await GetHourKlineAsync(code, startDate, endDate, ct);

        var daily = await GetDailyAsync(code, startDate, endDate, ct, allowFetch: true);
        return Aggregate(daily, period, startDate, endDate);
    }

    public async Task<List<AnalysisKline>> GetIndexKlineAsync(
        string code, string? start, string? end, string? period = "daily", int? limit = null, CancellationToken ct = default)
    {
        // 指定 limit 而未指定起始日时尽量多取历史，再截取最近 limit 根
        var startDate = ParseDate(start, limit is > 0 ? new DateTime(2000, 1, 1) : new DateTime(2024, 1, 1));
        var endDate = ParseDate(end, DateTime.Today);
        if (startDate > endDate) (startDate, endDate) = (endDate, startDate);
        period = NormalizePeriod(period);

        if (period == "60min")
        {
            if (!IsIndexCode(code))
                throw new AnalysisRequestException($"{code} 暂不支持 60 分钟周期");
            return await GetHourKlineAsync(code, startDate, endDate, ct);
        }

        var daily = await GetDailyAsync(code, startDate, endDate, ct, allowFetch: true, isIndex: true);
        var rows = Aggregate(daily, period, startDate, endDate);
        if (limit is > 0 && rows.Count > limit)
            rows = rows.Skip(rows.Count - limit.Value).ToList();
        return rows;
    }

    /// <summary>批量读取（选股/监控用）。DB 覆盖不足且标的数较少时才回源。</summary>
    public async Task<Dictionary<string, List<AnalysisKline>>> GetKlineBatchAsync(
        IReadOnlyCollection<string> codes, DateTime startDate, DateTime endDate, string period,
        bool allowFetch, CancellationToken ct = default)
    {
        period = NormalizePeriod(period);
        var result = new Dictionary<string, List<AnalysisKline>>(StringComparer.OrdinalIgnoreCase);

        if (period == "60min")
        {
            foreach (var code in codes)
            {
                var rows = await GetHourKlineAsync(code, startDate, endDate, ct);
                if (rows.Count > 0) result[code] = rows;
            }
            return result;
        }

        foreach (var chunk in codes.Chunk(800))
        {
            var symbols = chunk.ToArray();
            var rows = await db.StockDailies.AsNoTracking()
                .Where(x => symbols.Contains(x.Symbol) && x.Date >= startDate && x.Date <= endDate)
                .OrderBy(x => x.Symbol).ThenBy(x => x.Date)
                .ToListAsync(ct);
            foreach (var group in rows.GroupBy(x => x.Symbol))
            {
                var list = ToKlines(group.Select(x => (x.Date, x.Open, x.High, x.Low, x.Close, x.Volume, x.Amount)));
                result[group.Key] = Aggregate(list, period, startDate, endDate);
            }
        }

        if (allowFetch && codes.Count <= 500)
        {
            var missing = codes.Where(c => !result.ContainsKey(c)).ToArray();
            foreach (var code in missing)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var daily = await FetchAndPersistDailyAsync(code, startDate, endDate, isIndex: false, ct);
                    var agg = Aggregate(daily, period, startDate, endDate);
                    if (agg.Count > 0) result[code] = agg;
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "批量取 K 线失败 {Code}", code);
                }
            }
        }

        return result;
    }

    public static string NormalizePeriod(string? period)
    {
        var p = (period ?? "daily").Trim().ToLowerInvariant();
        return p switch
        {
            "60min" or "hour" or "hourly" => "60min",
            "day" or "daily" => "daily",
            "week" or "weekly" => "weekly",
            "month" or "monthly" => "monthly",
            "quarter" or "quarterly" => "quarterly",
            "year" or "yearly" => "yearly",
            _ => throw new ArgumentException($"Invalid period. Valid values: {string.Join(", ", Periods)}"),
        };
    }

    private async Task<List<AnalysisKline>> GetDailyAsync(
        string code, DateTime startDate, DateTime endDate, CancellationToken ct, bool allowFetch,
        bool? isIndex = null)
    {
        // 000001 既是上证指数也是平安银行，未加市场前缀时按个股处理，指数调用方显式传 isIndex
        var index = isIndex ?? IsIndexCode(code);
        List<AnalysisKline> rows;
        if (index)
        {
            var dbCode = IsIndexCode(code) ? code[2..] : code;
            var raw = await db.IndexDailies.AsNoTracking()
                .Where(x => x.Code == dbCode && x.Date >= startDate && x.Date <= endDate)
                .OrderBy(x => x.Date)
                .ToListAsync(ct);
            rows = ToKlines(raw.Select(x => (x.Date, x.Open, x.High, x.Low, x.Close, x.Volume, x.Amount)));
        }
        else
        {
            var raw = await db.StockDailies.AsNoTracking()
                .Where(x => x.Symbol == code && x.Date >= startDate && x.Date <= endDate)
                .OrderBy(x => x.Date)
                .ToListAsync(ct);
            rows = ToKlines(raw.Select(x => (x.Date, x.Open, x.High, x.Low, x.Close, x.Volume, x.Amount)));
        }

        var stale = rows.Count < 60 ||
                    string.CompareOrdinal(rows[^1].Date, endDate.AddDays(-3).ToString("yyyyMMdd")) < 0;
        if (allowFetch && stale)
        {
            try
            {
                var fetched = await FetchAndPersistDailyAsync(code, startDate, endDate, index, ct);
                if (fetched.Count > rows.Count) rows = fetched;
                else if (fetched.Count > 0 && rows.Count == 0) rows = fetched;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "回源 K 线失败 {Code}", code);
            }
        }

        return rows;
    }

    private async Task<List<AnalysisKline>> FetchAndPersistDailyAsync(
        string code, DateTime startDate, DateTime endDate, bool isIndex, CancellationToken ct)
    {
        var (rawCode, secMarket) = ResolveCode(code, isIndex);
        var items = await quotes.FetchKLineAsync(rawCode, secMarket, 101, 3000, isIndex ? 0 : 1, ct);
        var list = ToKlines(items.Select(x => (x.Time.Date, x.Open, x.High, x.Low, x.Close, x.Volume, x.Amount)))
            .Where(x => string.CompareOrdinal(x.Date, startDate.ToString("yyyyMMdd")) >= 0 &&
                        string.CompareOrdinal(x.Date, endDate.ToString("yyyyMMdd")) <= 0)
            .ToList();
        if (list.Count == 0) return list;

        if (isIndex)
        {
            var digits = rawCode;
            var existing = await db.IndexDailies
                .Where(x => x.Code == digits && x.Date >= startDate && x.Date <= endDate)
                .Select(x => x.Date).ToListAsync(ct);
            var set = existing.ToHashSet();
            foreach (var k in list.Where(k => !set.Contains(ParseCompact(k.Date))))
            {
                db.IndexDailies.Add(new IndexDaily
                {
                    Code = digits, Market = secMarket, Date = ParseCompact(k.Date),
                    Open = k.Open, High = k.High, Low = k.Low, Close = k.Close,
                    Volume = k.Volume, Amount = k.Amount, ChangePct = k.ChangePct,
                });
            }
        }
        else
        {
            var existing = await db.StockDailies
                .Where(x => x.Symbol == code && x.Date >= startDate && x.Date <= endDate)
                .Select(x => x.Date).ToListAsync(ct);
            var set = existing.ToHashSet();
            foreach (var k in list.Where(k => !set.Contains(ParseCompact(k.Date))))
            {
                db.StockDailies.Add(new StockDaily
                {
                    Symbol = code, Date = ParseCompact(k.Date),
                    Open = k.Open, High = k.High, Low = k.Low, Close = k.Close,
                    Volume = k.Volume, Amount = k.Amount, ChangePct = k.ChangePct,
                });
            }
        }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) { logger.LogDebug(ex, "K 线落库跳过 {Code}", code); }
        return list;
    }

    private async Task<List<AnalysisKline>> GetHourKlineAsync(string code, DateTime startDate, DateTime endDate, CancellationToken ct)
    {
        if (!IsIndexCode(code))
        {
            var raw = await db.StockMinutes.AsNoTracking()
                .Where(x => x.Symbol == code && x.Time >= startDate && x.Time <= endDate.AddDays(1))
                .OrderBy(x => x.Time)
                .ToListAsync(ct);
            if (raw.Count >= 60)
                return AggregateHour(raw.Select(x => (x.Time, x.Open, x.High, x.Low, x.Close, x.Volume, 0.0)), startDate, endDate);
        }

        var (rawCode, secMarket) = ResolveCode(code, IsIndexCode(code));
        var items = await quotes.FetchKLineAsync(rawCode, secMarket, 60, 2000, IsIndexCode(code) ? 0 : 1, ct);
        return ToKlines(items.Select(x => (x.Time, x.Open, x.High, x.Low, x.Close, x.Volume, x.Amount)))
            .Where(x => ParseKlineDate(x.Date) >= startDate && ParseKlineDate(x.Date) <= endDate)
            .ToList();
    }

    private static List<AnalysisKline> AggregateHour(
        IEnumerable<(DateTime Time, double Open, double High, double Low, double Close, double Volume, double Amount)> rows,
        DateTime startDate, DateTime endDate)
    {
        var result = new List<AnalysisKline>();
        foreach (var group in rows.GroupBy(x => new DateTime(x.Time.Year, x.Time.Month, x.Time.Day, x.Time.Hour, 0, 0)).OrderBy(g => g.Key))
        {
            if (group.Key.Date < startDate || group.Key.Date > endDate) continue;
            var list = group.ToList();
            result.Add(new AnalysisKline(
                group.Key.ToString("yyyy-MM-dd HH:mm"),
                list[0].Open, list.Max(x => x.High), list.Min(x => x.Low), list[^1].Close,
                list.Sum(x => x.Volume), list.Sum(x => x.Amount)));
        }
        return result;
    }

    public static List<AnalysisKline> Aggregate(
        IReadOnlyList<AnalysisKline> daily, string period, DateTime startDate, DateTime endDate)
    {
        if (period == "daily" || daily.Count == 0) return [.. daily];

        var keySelector = period switch
        {
            "weekly" => (Func<DateTime, string>)(d =>
                $"{ISOWeek.GetYear(d)}-{ISOWeek.GetWeekOfYear(d):00}"),
            "monthly" => d => d.ToString("yyyy-MM"),
            "quarterly" => d => $"{d.Year}-Q{(d.Month - 1) / 3 + 1}",
            "yearly" => d => d.Year.ToString(),
            _ => d => d.ToString("yyyyMMdd"),
        };

        var list = new List<AnalysisKline>();
        foreach (var group in daily.GroupBy(x => keySelector(ParseKlineDate(x.Date))).OrderBy(g => g.Min(x => x.Date)))
        {
            var rows = group.OrderBy(x => x.Date).ToList();
            var last = rows[^1];
            var first = rows[0];
            list.Add(new AnalysisKline(
                last.Date, first.Open, rows.Max(x => x.High), rows.Min(x => x.Low), last.Close,
                rows.Sum(x => x.Volume), rows.Sum(x => x.Amount)));
        }
        return list.Where(x => ParseKlineDate(x.Date) >= startDate && ParseKlineDate(x.Date) <= endDate).ToList();
    }

    private static List<AnalysisKline> ToKlines(
        IEnumerable<(DateTime Date, double Open, double High, double Low, double Close, double Volume, double Amount)> rows)
    {
        var result = new List<AnalysisKline>();
        double prev = 0;
        foreach (var r in rows.OrderBy(x => x.Date))
        {
            var change = prev > 0 ? (r.Close / prev - 1) * 100 : 0;
            result.Add(new AnalysisKline(
                r.Date.ToString("yyyyMMdd"), r.Open, r.High, r.Low, r.Close, r.Volume, r.Amount)
            { ChangePct = Math.Round(change, 2) });
            prev = r.Close;
        }
        return result;
    }

    public static DateTime ParseCompact(string date)
        => DateTime.TryParseExact(date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : ParseDate(date, DateTime.MinValue);

    public static DateTime ParseKlineDate(string date)
    {
        if (date.Length > 10 && DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt.Date;
        return ParseCompact(date);
    }

    private static (string Symbol, int SecMarket) ResolveCode(string code, bool isIndex)
    {
        if (isIndex)
        {
            if (!IsIndexCode(code))
                return (code, MarketIndices.List.FirstOrDefault(i =>
                    string.Equals(i.Code, code, StringComparison.OrdinalIgnoreCase)).Market);
            var prefix = code[..2].ToLowerInvariant();
            return (code[2..], prefix == "sh" ? 1 : 0);
        }
        var market = code.StartsWith('6') || code.StartsWith('9') ? 1 : 0;
        return (code, market);
    }

    /// <summary>通用行情序列（汇率/黄金/原油/中债）。</summary>
    public async Task<List<AnalysisKline>> GetMarketSeriesAsync(string key, string period, DateTime? start, DateTime? end, CancellationToken ct = default)
    {
        var p = NormalizePeriod(period);
        var query = db.MarketSeriesPoints.AsNoTracking().Where(x => x.Key == key && x.Period == p);
        if (start is not null) query = query.Where(x => x.Date >= start);
        if (end is not null) query = query.Where(x => x.Date <= end);
        var rows = await query.OrderBy(x => x.Date).ToListAsync(ct);
        return ToKlines(rows.Select(x => (x.Date, x.Open, x.High, x.Low, x.Close, 0.0, 0.0)));
    }
}