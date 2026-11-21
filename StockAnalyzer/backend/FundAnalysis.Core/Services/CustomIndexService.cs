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
public class CustomIndexService(AppDbContext db, AnalysisDataService data)
{
    public const int MaxComponents = 30;
    private const int MaxNameLength = 40;

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
        if (inputs.Count > MaxComponents) return $"成分股最多 {MaxComponents} 只";

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
