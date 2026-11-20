using System.Globalization;
using FundAnalysis.Data;
using FundAnalysis.Models;
using Microsoft.EntityFrameworkCore;

namespace FundAnalysis.Analysis;

/// <summary>
/// 量化分析模块的大盘/宏观 API 数据源。与 Node 版不同，这里优先读采集库，
/// 避免 Web 请求路径依赖第三方的实时接口。
/// </summary>
public sealed class MarketAnalysisService(AppDbContext db, AnalysisDataService data)
{
    public static readonly string[] Periods = ["daily", "weekly", "monthly", "quarterly", "yearly"];

    public static readonly string[] UsMaturities = ["3M", "6M", "1Y", "10Y"];

    public static readonly string[] CnRateTypes =
        ["SHIBOR_ON", "SHIBOR_1W", "SHIBOR_2W", "SHIBOR_1M", "SHIBOR_3M", "SHIBOR_6M", "SHIBOR_9M", "SHIBOR_1Y", "CN_10Y"];

    // ---------- 大盘指数 ----------

    public async Task<List<AnalysisKline>> GetIndexKlineAsync(
        string code, string? start, string? end, string period, int? limit = null, CancellationToken ct = default)
        => await data.GetIndexKlineAsync(NormalizeIndexCode(code), start, end, period, limit, ct);

    public async Task<Dictionary<string, object>> GetIndexPeriodDataAsync(
        string period, string[]? codes, string? start, string? end, CancellationToken ct = default)
    {
        var requested = codes is { Length: > 0 } ? codes : MarketIndices.MarketOverviewCodes;
        var result = new Dictionary<string, object>();
        foreach (var code in requested)
        {
            var rows = await data.GetIndexKlineAsync(NormalizeIndexCode(code), start, end, period, ct: ct);
            result[code] = new
            {
                name = CatalogName(code),
                data = rows.Select(ToSeriesPoint).ToList(),
            };
        }
        return result;
    }

    // ---------- 宏观指标 ----------

    public async Task<List<object>> GetMacroAsync(CancellationToken ct = default)
    {
        var list = new List<object>();

        async Task AddIndicatorAsync(string key, string name, string unit, int precision, string symbol)
        {
            var rows = await db.MarketIndicators.AsNoTracking()
                .Where(x => x.Symbol == symbol)
                .OrderByDescending(x => x.Date)
                .Take(2)
                .ToListAsync(ct);
            if (rows.Count == 0) return;
            var last = rows[0];
            var prev = rows.Count > 1 ? rows[1] : null;
            var changePct = prev is null || prev.Value == 0 ? 0 : (last.Value / prev.Value - 1) * 100;
            list.Add(new
            {
                key,
                name,
                value = last.Value,
                changePct = Math.Round(changePct, 2),
                unit,
                precision,
                time = last.Date.ToString("yyyy-MM-dd"),
            });
        }

        await AddIndicatorAsync("us10y", "美债10Y", "%", 2, "US10Y");
        await AddIndicatorAsync("us3m", "美债3M", "%", 2, "US3M");
        await AddIndicatorAsync("usdcny", "美元兑人民币", "", 4, "USDCNH");
        await AddIndicatorAsync("cn10y", "中债10Y", "%", 2, "CN10Y");

        var oil = await db.MarketSeriesPoints.AsNoTracking()
            .Where(x => x.Key == "crude_oil" && x.Period == "daily")
            .OrderByDescending(x => x.Date)
            .Take(2)
            .ToListAsync(ct);
        if (oil.Count > 0)
        {
            var last = oil[0];
            var prev = oil.Count > 1 ? oil[1] : null;
            list.Add(new
            {
                key = "oil",
                name = "WTI原油",
                value = last.Close,
                changePct = prev is null || prev.Close == 0 ? 0 : Math.Round((last.Close / prev.Close - 1) * 100, 2),
                unit = "$",
                precision = 2,
                time = last.Date.ToString("yyyy-MM-dd"),
            });
        }

        var shibor = await LatestShiborAsync("SHIBOR_3M", ct);
        if (shibor is not null)
        {
            list.Add(new
            {
                key = "cnrate",
                name = "SHIBOR 3M",
                value = shibor.Rate,
                changePct = Math.Round(shibor.ChangeBp / 100, 2),
                unit = "%",
                precision = 2,
                time = shibor.Date.ToString("yyyy-MM-dd"),
            });
        }

        return list;
    }

    // ---------- 利率 ----------

    public async Task<List<object>> GetUsRatesAsync(
        string maturity, string? start, string? end, CancellationToken ct = default)
    {
        var symbol = "US" + maturity.ToUpperInvariant();
        var query = db.MarketIndicators.AsNoTracking().Where(x => x.Symbol == symbol);
        if (AnalysisDataService.ParseDate(start, DateTime.MinValue) is var s && s > DateTime.MinValue)
            query = query.Where(x => x.Date >= s);
        if (AnalysisDataService.ParseDate(end, DateTime.MinValue) is var e && e > DateTime.MinValue)
            query = query.Where(x => x.Date <= e);

        var rows = await query.OrderBy(x => x.Date).ToListAsync(ct);
        return rows.Select(r => (object)new { date = r.Date.ToString("yyyy-MM-dd"), rate = r.Value }).ToList();
    }

    public async Task<List<object>> GetCnRatesAsync(
        string type, string? start, string? end, CancellationToken ct = default)
    {
        var startDate = AnalysisDataService.ParseDate(start, DateTime.MinValue);
        var endDate = AnalysisDataService.ParseDate(end, DateTime.MinValue);

        if (type.Equals("CN_10Y", StringComparison.OrdinalIgnoreCase))
        {
            var query = db.MarketIndicators.AsNoTracking().Where(x => x.Symbol == "CN10Y");
            if (startDate > DateTime.MinValue) query = query.Where(x => x.Date >= startDate);
            if (endDate > DateTime.MinValue) query = query.Where(x => x.Date <= endDate);
            var rows = await query.OrderBy(x => x.Date).ToListAsync(ct);
            return rows.Select(r => (object)new { date = r.Date.ToString("yyyy-MM-dd"), rate = r.Value }).ToList();
        }

        var period = ShiborPeriodOf(type);
        var q2 = db.ShiborRates.AsNoTracking().Where(x => x.Period.Contains(period));
        if (startDate > DateTime.MinValue) q2 = q2.Where(x => x.Date >= startDate);
        if (endDate > DateTime.MinValue) q2 = q2.Where(x => x.Date <= endDate);
        var result = await q2.OrderBy(x => x.Date).ToListAsync(ct);
        return result.Select(r => (object)new { date = r.Date.ToString("yyyy-MM-dd"), rate = r.Rate }).ToList();
    }

    private async Task<Models.ShiborRate?> LatestShiborAsync(string type, CancellationToken ct)
    {
        var period = ShiborPeriodOf(type);
        return await db.ShiborRates.AsNoTracking()
            .Where(x => x.Period.Contains(period))
            .OrderByDescending(x => x.Date)
            .FirstOrDefaultAsync(ct);
    }

    private static string ShiborPeriodOf(string type) => type.ToUpperInvariant() switch
    {
        "SHIBOR_ON" => "隔夜",
        "SHIBOR_1W" => "1周",
        "SHIBOR_2W" => "2周",
        "SHIBOR_1M" => "1月",
        "SHIBOR_3M" => "3月",
        "SHIBOR_6M" => "6月",
        "SHIBOR_9M" => "9月",
        "SHIBOR_1Y" => "1年",
        _ => throw new AnalysisRequestException($"Invalid type. Valid values: {string.Join(", ", CnRateTypes)}"),
    };

    // ---------- 商品 / 汇率序列 ----------

    public async Task<List<object>> GetCrudeOilAsync(string period, string? start, string? end, CancellationToken ct = default)
        => await GetSeriesAsync("crude_oil", period, start, end, ct);

    public async Task<List<object>> GetGoldAsync(string period, string? start, string? end, CancellationToken ct = default)
        => await GetSeriesAsync("gold_price", period, start, end, ct);

    public async Task<List<object>> GetCn10yAsync(string period, string? start, string? end, CancellationToken ct = default)
    {
        var rows = await GetSeriesAsync("cn10y", period, start, end, ct);
        if (rows.Count > 0) return rows;

        // 兼容：中债 10Y 也由采集器写入 MarketIndicator(CN10Y)
        var startDate = AnalysisDataService.ParseDate(start, new DateTime(2000, 1, 1));
        var endDate = AnalysisDataService.ParseDate(end, DateTime.Today);
        var points = await db.MarketIndicators.AsNoTracking()
            .Where(x => x.Symbol == "CN10Y" && x.Date >= startDate && x.Date <= endDate)
            .OrderBy(x => x.Date)
            .ToListAsync(ct);
        var daily = points.Select(p => new AnalysisKline(
            p.Date.ToString("yyyyMMdd"), p.Value, p.Value, p.Value, p.Value, 0, 0)).ToList();
        return AnalysisDataService.Aggregate(daily, AnalysisDataService.NormalizePeriod(period), startDate, endDate)
            .Select(ToSeriesPoint).Cast<object>().ToList();
    }

    public async Task<List<object>> GetFxAsync(string period, string? start, string? end, CancellationToken ct = default)
    {
        var rows = await GetSeriesAsync("fx_cny_usd", period, start, end, ct);
        if (rows.Count > 0) return rows;

        // 兼容：人民币汇率由采集器写入 MarketIndicator(USDCNH)
        var startDate = AnalysisDataService.ParseDate(start, new DateTime(2000, 1, 1));
        var endDate = AnalysisDataService.ParseDate(end, DateTime.Today);
        var points = await db.MarketIndicators.AsNoTracking()
            .Where(x => x.Symbol == "USDCNH" && x.Date >= startDate && x.Date <= endDate)
            .OrderBy(x => x.Date)
            .ToListAsync(ct);
        var daily = points.Select(p => new AnalysisKline(
            p.Date.ToString("yyyyMMdd"), p.Value, p.Value, p.Value, p.Value, 0, 0)).ToList();
        return AnalysisDataService.Aggregate(daily, AnalysisDataService.NormalizePeriod(period), startDate, endDate)
            .Select(ToSeriesPoint).Cast<object>().ToList();
    }

    private async Task<List<object>> GetSeriesAsync(
        string key, string period, string? start, string? end, CancellationToken ct)
    {
        var startDate = AnalysisDataService.ParseDate(start, DateTime.MinValue);
        var endDate = AnalysisDataService.ParseDate(end, DateTime.MinValue);
        var rows = await data.GetMarketSeriesAsync(
            key, period, startDate > DateTime.MinValue ? startDate : null,
            endDate > DateTime.MinValue ? endDate : null, ct);
        return rows.Select(ToSeriesPoint).Cast<object>().ToList();
    }

    // ---------- 工具 ----------

    /// <summary>按目录查指数名（兼容 sh000001 前缀写法）。</summary>
    private static string CatalogName(string code)
    {
        var digits = code.Length == 8 && IsPrefixed(code) ? code[2..] : code;
        var hit = MarketIndices.List.FirstOrDefault(i => string.Equals(i.Code, digits, StringComparison.OrdinalIgnoreCase));
        return hit.Name ?? code;
    }

    private static bool IsPrefixed(string code) => code.Length == 8 &&
        (code.StartsWith("sh", StringComparison.OrdinalIgnoreCase) ||
         code.StartsWith("sz", StringComparison.OrdinalIgnoreCase) ||
         code.StartsWith("bj", StringComparison.OrdinalIgnoreCase));

    private static object ToSeriesPoint(AnalysisKline k) => new
    {
        date = k.Date,
        open = k.Open,
        high = k.High,
        low = k.Low,
        close = k.Close,
        amount = k.Amount,
    };

    public static string NormalizeIndexCode(string code)
    {
        code = code.Trim();
        if (AnalysisDataService.IsIndexCode(code)) return code.ToLowerInvariant();

        // 内置指数清单：纯数字代码补市场前缀（60min 周期依赖前缀判定为指数），跨境代码原样返回
        var known = MarketIndices.List.FirstOrDefault(i =>
            string.Equals(i.Code, code, StringComparison.OrdinalIgnoreCase));
        if (known.Code is not null)
        {
            if (!known.Code.All(char.IsDigit)) return known.Code;
            return known.Market switch
            {
                1 => "sh" + known.Code,
                0 => "sz" + known.Code,
                _ => known.Code,
            };
        }

        if (code.Length != 6 || !code.All(char.IsDigit))
            throw new AnalysisRequestException($"未知指数 {code}");

        var prefix = code.StartsWith('6') || code.StartsWith("000") ? "sh"
            : code.StartsWith('8') || code.StartsWith("920") ? "bj"
            : "sz";
        return prefix + code;
    }

    public static bool IsValidPeriod(string? period)
        => Periods.Contains((period ?? "").Trim().ToLowerInvariant());

    public static bool IsValidMaturity(string maturity)
        => UsMaturities.Contains(maturity.ToUpperInvariant(), StringComparer.OrdinalIgnoreCase);

    public static bool IsValidCnRateType(string type)
        => CnRateTypes.Contains(type.ToUpperInvariant(), StringComparer.Ordinal);

    public static string CultureDate(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}