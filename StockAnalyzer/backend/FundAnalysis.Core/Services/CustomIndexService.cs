using System.Collections.Concurrent;
using System.Text.Json;
using FundAnalysis.Analysis;
using FundAnalysis.Data;
using FundAnalysis.Models;
using Microsoft.EntityFrameworkCore;

namespace FundAnalysis.Services;

/// <summary>
/// 自定义指数：按用户设定的成分股权重，用成分股日线合成指数点位。
/// 采用"价格相对值加权"：每日点位 = 基准点位 × Σ(权重 × 当日价 / 首日收盘价) / Σ权重，
/// 因此不同查询区间看到的历史点位一致；停牌或缺失数据时沿用最近收盘价，保持权重完整。
/// </summary>
public class CustomIndexService(AppDbContext db, AnalysisDataService data, IHttpClientFactory httpFactory)
{
    private const int MaxNameLength = 40;

    // 预设指数：东财数据中心 RPT_INDEX_TS_COMPONENT 的 TYPE 与成分数（已核对）
    private static readonly (string Key, string Name, string Type, int Count)[] PresetDefs =
    [
        ("sse50", "上证50", "2", 50),
        ("hs300", "沪深300", "1", 300),
        ("csi500", "中证500", "3", 500),
        ("csi1000", "中证1000", "7", 1000),
        ("star50", "科创50", "4", 50),
        ("sse180", "上证180", "11", 180),
        ("szse", "深证成指", "6", 500),
        ("szse100", "深证100", "12", 100),
        ("bse50", "北证50", "10", 50),
    ];

    // 成分股变动频率低，缓存 6 小时，避免每次打开弹窗都请求外网
    private static readonly ConcurrentDictionary<string, (DateTime At, List<CustomIndexComponentDto> Items)> PresetCache = new();
    private static readonly SemaphoreSlim PresetLock = new(1, 1);

    public async Task<List<CustomIndex>> ListAsync(string userId, CancellationToken ct = default)
        => await db.CustomIndices.AsNoTracking()
            .Include(x => x.Components)
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

    public Task<CustomIndex?> FindAsync(string userId, long id, CancellationToken ct = default)
        => db.CustomIndices.Include(x => x.Components)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);

    /// <summary>指数页卡片清单：最新点位、涨跌幅与近 30 个交易日点位走势。</summary>
    public async Task<List<CustomIndexItemDto>> GetSnapshotsAsync(string userId, CancellationToken ct = default)
    {
        var list = await ListAsync(userId, ct);
        var result = new List<CustomIndexItemDto>();
        foreach (var index in list)
        {
            ct.ThrowIfCancellationRequested();
            var rows = await BuildKlinesAsync(index, null, null, "daily", 31, allowFetch: false, ct);
            var last = rows.Count > 0 ? rows[^1] : null;
            result.Add(new CustomIndexItemDto(
                index.Id, index.Name, index.BaseValue, index.CreatedAt,
                index.Components.OrderBy(c => c.Id)
                    .Select(c => new CustomIndexComponentDto(c.Symbol, c.Name, c.Weight)).ToList(),
                last is null ? 0 : Math.Round(last.Close, 2),
                last?.ChangePct ?? 0,
                rows.Select(r => Math.Round(r.Close, 2)).ToList()));
        }
        return result;
    }

    /// <summary>合成指定周期的 K 线（60 分钟线不支持：成分股分钟线覆盖不足）。</summary>
    public async Task<List<AnalysisKline>> GetKlineAsync(
        CustomIndex index, string? start, string? end, string period, int? limit,
        CancellationToken ct = default)
    {
        period = AnalysisDataService.NormalizePeriod(period);
        if (period == "60min")
            throw new AnalysisRequestException("自定义指数暂不支持 60 分钟周期");
        return await BuildKlinesAsync(index, start, end, period, limit, allowFetch: true, ct);
    }

    /// <summary>可快速导入的预设指数列表。</summary>
    public static List<CustomIndexPresetDto> GetPresets()
        => [.. PresetDefs.Select(p => new CustomIndexPresetDto(p.Key, p.Name, p.Count))];

    /// <summary>预设指数成分股及权重（官方权重优先，缺失时整体等权）。</summary>
    public async Task<CustomIndexPresetDetailDto> GetPresetAsync(string key, CancellationToken ct = default)
    {
        var def = PresetDefs.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (def.Key is null) throw new AnalysisRequestException("未知的预设指数", 404);

        if (TryGetCachedPreset(def.Key, out var cached))
            return new CustomIndexPresetDetailDto(def.Key, def.Name, cached);

        await PresetLock.WaitAsync(ct);
        try
        {
            if (TryGetCachedPreset(def.Key, out cached))
                return new CustomIndexPresetDetailDto(def.Key, def.Name, cached);

            var items = await FetchPresetComponentsAsync(def.Type, ct);
            if (items.Count == 0)
                throw new AnalysisRequestException($"未获取到{def.Name}成分股，请稍后重试", 502);

            PresetCache[def.Key] = (DateTime.Now, items);
            return new CustomIndexPresetDetailDto(def.Key, def.Name, items);
        }
        finally
        {
            PresetLock.Release();
        }
    }

    private static bool TryGetCachedPreset(string key, out List<CustomIndexComponentDto> items)
    {
        if (PresetCache.TryGetValue(key, out var entry) && DateTime.Now - entry.At < TimeSpan.FromHours(6))
        {
            items = entry.Items;
            return true;
        }
        items = [];
        return false;
    }

    private async Task<List<CustomIndexComponentDto>> FetchPresetComponentsAsync(string type, CancellationToken ct)
    {
        using var client = httpFactory.CreateClient("eastmoney");
        client.Timeout = TimeSpan.FromSeconds(20);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        client.DefaultRequestHeaders.Referrer = new Uri("https://quote.eastmoney.com/");

        const int pageSize = 500;
        var rows = new List<(string Symbol, string Name, double Weight)>();
        try
        {
            var filter = Uri.EscapeDataString($"(TYPE=\"{type}\")");
            for (var page = 1; page <= 20; page++)
            {
                var url = "https://datacenter-web.eastmoney.com/api/data/v1/get"
                          + "?reportName=RPT_INDEX_TS_COMPONENT&columns=SECURITY_CODE,SECURITY_NAME_ABBR,WEIGHT"
                          + $"&filter={filter}&pageNumber={page}&pageSize={pageSize}"
                          + "&sortColumns=SECURITY_CODE&sortTypes=1";
                using var doc = JsonDocument.Parse(await client.GetStringAsync(url, ct));
                if (!doc.RootElement.TryGetProperty("result", out var result) ||
                    result.ValueKind != JsonValueKind.Object ||
                    !result.TryGetProperty("data", out var arr) || arr.ValueKind != JsonValueKind.Array)
                    break;

                var count = 0;
                foreach (var row in arr.EnumerateArray())
                {
                    var symbol = GetString(row, "SECURITY_CODE");
                    if (string.IsNullOrWhiteSpace(symbol)) continue;

                    var weight = 0d;
                    if (row.TryGetProperty("WEIGHT", out var w) && w.ValueKind == JsonValueKind.Number)
                        weight = w.GetDouble();

                    rows.Add((symbol, GetString(row, "SECURITY_NAME_ABBR") ?? symbol, weight));
                    count++;
                }

                if (count < pageSize) break;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new AnalysisRequestException("获取预设指数成分失败，请稍后重试", 502);
        }

        if (rows.Count == 0) return [];

        // 官方权重可用则使用官方权重，否则整体等权
        var useOfficial = rows.All(r => r.Weight > 0);
        var equal = Math.Round(100d / rows.Count, 4);
        return [.. rows
            .DistinctBy(r => r.Symbol)
            .Select(r => new CustomIndexComponentDto(
                r.Symbol, r.Name, useOfficial ? Math.Round(r.Weight, 4) : equal))];
    }

    private static string? GetString(JsonElement row, string name)
        => row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>校验并整理保存请求；返回错误信息，null 表示通过。</summary>
    public static string? NormalizeRequest(
        CustomIndexSaveRequest request, out string name, out double baseValue,
        out List<CustomIndexComponent> components)
    {
        name = (request.Name ?? "").Trim();
        baseValue = request.BaseValue is > 0 ? request.BaseValue.Value : 1000;
        components = [];

        if (name.Length == 0) return "请填写指数名称";
        if (name.Length > MaxNameLength) return $"指数名称最多 {MaxNameLength} 个字";

        var inputs = (request.Components ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c.Symbol))
            .ToList();
        if (inputs.Count == 0) return "请至少添加一只成分股";

        var duplicated = inputs.GroupBy(c => c.Symbol.Trim()).FirstOrDefault(g => g.Count() > 1);
        if (duplicated is not null) return $"成分股 {duplicated.Key} 重复";

        if (inputs.Any(c => c.Weight <= 0)) return "权重必须大于 0";

        components = [.. inputs.Select(c => new CustomIndexComponent
        {
            Symbol = c.Symbol.Trim(),
            Name = (c.Name ?? "").Trim(),
            Weight = c.Weight,
        })];
        return null;
    }

    private async Task<List<AnalysisKline>> BuildKlinesAsync(
        CustomIndex index, string? start, string? end, string period, int? limit,
        bool allowFetch, CancellationToken ct)
    {
        var components = index.Components
            .GroupBy(c => c.Symbol)
            .Select(g => (Symbol: g.Key, Weight: g.Sum(x => x.Weight)))
            .Where(x => x.Weight > 0)
            .ToList();
        if (components.Count == 0) return [];

        // 成分股日线采集约 2 年，这里取 3 年窗口，允许回源补齐
        var historyStart = DateTime.Today.AddYears(-3).Date;
        var historyEnd = DateTime.Today;
        var batch = await data.GetKlineBatchAsync(
            components.Select(c => c.Symbol).ToArray(), historyStart, historyEnd,
            "daily", allowFetch, ct);
        if (batch.Count == 0) return [];

        // 逐只股票整理：共同起点后的日期 → 日线，以及首日收盘价（归一化基准）
        var series = new Dictionary<string, List<AnalysisKline>>();
        foreach (var (symbol, _) in components)
            if (batch.TryGetValue(symbol, out var klineRows) && klineRows.Count > 0)
                series[symbol] = klineRows;
        if (series.Count == 0) return [];

        // 指数起点取所有有效成分股都已有数据的最晚首日，保证首日权重完整
        var commonStart = series.Values.Max(list => AnalysisDataService.ParseKlineDate(list[0].Date));

        var bases = new Dictionary<string, double>();
        var maps = new Dictionary<string, Dictionary<DateTime, AnalysisKline>>();
        foreach (var (symbol, list) in series)
        {
            var ordered = list
                .Where(r => AnalysisDataService.ParseKlineDate(r.Date) >= commonStart)
                .ToList();
            if (ordered.Count == 0) continue;
            var firstClose = ordered[0].Close;
            if (firstClose <= 0) continue;
            bases[symbol] = firstClose;
            maps[symbol] = ordered.ToDictionary(r => AnalysisDataService.ParseKlineDate(r.Date));
        }
        if (bases.Count == 0) return [];

        var totalWeight = components.Where(c => bases.ContainsKey(c.Symbol)).Sum(c => c.Weight);
        if (totalWeight <= 0) return [];
        var weights = components
            .Where(c => bases.ContainsKey(c.Symbol))
            .ToDictionary(c => c.Symbol, c => c.Weight / totalWeight);

        var dates = maps.Values
            .SelectMany(m => m.Keys)
            .Where(d => d >= commonStart)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        var daily = new List<AnalysisKline>(dates.Count);
        var lastClose = new Dictionary<string, double>();
        double prevLevel = 0;
        foreach (var date in dates)
        {
            double open = 0, high = 0, low = 0, close = 0, weightSum = 0;
            foreach (var (symbol, weight) in weights)
            {
                var baseClose = bases[symbol];
                if (maps[symbol].TryGetValue(date, out var row))
                {
                    lastClose[symbol] = row.Close;
                    open += weight * row.Open / baseClose;
                    high += weight * row.High / baseClose;
                    low += weight * row.Low / baseClose;
                }
                else if (lastClose.TryGetValue(symbol, out var prev))
                {
                    // 停牌 / 当日缺数据：沿用最近收盘价
                    open += weight * prev / baseClose;
                    high += weight * prev / baseClose;
                    low += weight * prev / baseClose;
                }
                else
                {
                    continue;
                }
                close += weight * lastClose[symbol] / baseClose;
                weightSum += weight;
            }
            if (weightSum <= 0) continue;

            var levelOpen = index.BaseValue * open / weightSum;
            var levelHigh = index.BaseValue * high / weightSum;
            var levelLow = index.BaseValue * low / weightSum;
            var levelClose = index.BaseValue * close / weightSum;
            var changePct = prevLevel > 0 ? (levelClose / prevLevel - 1) * 100 : 0;

            daily.Add(new AnalysisKline(
                date.ToString("yyyyMMdd"),
                Math.Round(levelOpen, 2),
                Math.Round(Math.Max(levelHigh, Math.Max(levelOpen, levelClose)), 2),
                Math.Round(Math.Min(levelLow, Math.Min(levelOpen, levelClose)), 2),
                Math.Round(levelClose, 2),
                0, 0)
            { ChangePct = Math.Round(changePct, 2) });
            prevLevel = levelClose;
        }
        if (daily.Count == 0) return [];

        var startDate = AnalysisDataService.ParseDate(start, new DateTime(2000, 1, 1));
        var endDate = AnalysisDataService.ParseDate(end, DateTime.Today);
        if (startDate > endDate) (startDate, endDate) = (endDate, startDate);

        // Aggregate 对 daily 周期不做区间过滤（内置指数在取数时已按区间查询），
        // 这里合成数据来自固定 3 年窗口，需先按请求区间裁剪。
        daily = daily
            .Where(r =>
            {
                var d = AnalysisDataService.ParseKlineDate(r.Date);
                return d >= startDate && d <= endDate;
            })
            .ToList();
        if (daily.Count == 0) return [];

        var rows = AnalysisDataService.Aggregate(daily, period, startDate, endDate);
        if (limit is > 0 && rows.Count > limit.Value)
            rows = rows.Skip(rows.Count - limit.Value).ToList();
        return rows;
    }
}
