using System.Globalization;
using System.Text.Json;
using FundAnalysis.Data;
using FundAnalysis.Models;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace FundAnalysis.Services;

/// <summary>
/// 市场情绪采集与查询。
///
/// 数据口径：
///   - 涨跌家数 / 两市成交额：东财指数快照（上证指数 + 深证成指的 f104/f105/f106/f6），盘中实时；
///   - 涨停 / 跌停 / 炸板 / 最高连板：东财涨停板行情池（push2ex），按交易日可取历史；
///   - 涨跌分布 / 行业冷暖 / 平均涨跌幅：库内全市场基本面快照（StockFundamental，收盘后由采集器更新）。
///
/// 情绪温度分 = 上涨占比 35% + 涨停跌停对比 25% + 连板高度 15% + 封板率 10% + 成交额相对 20 日均值 15%，
/// 缺失的分项自动剔除并重新归一化，避免某个上游接口失败时分数被拉偏。
/// </summary>
public class MarketSentimentService(
    AppDbContext db,
    IHttpClientFactory factory,
    ILogger<MarketSentimentService> logger)
{
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36";
    private const string Ut = "7eea3edcaed734bea9cbfc24409ed989";
    private static readonly CultureInfo Cn = CultureInfo.InvariantCulture;

    // ---------- 采集 ----------

    /// <summary>
    /// 同步当日情绪快照（盘中定时运行，收盘后再跑一次覆盖为最终值）。
    /// 交易日由指数快照时间戳确定，周末/节假日手动触发也不会写入错误日期。
    /// </summary>
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    [AutomaticRetry(Attempts = 3)]
    public async Task SyncAsync()
    {
        var breadth = await FetchBreadthAsync();

        // 先确定归属交易日再拉涨停池：盘前/周末的涨跌家数仍是上一交易日口径，
        // 若用今天去查涨停池会拿到空池，反而把昨天的封板数据覆盖成 0。
        var date = await ResolveTradeDateAsync(breadth?.Date.Date);
        var pools = await FetchPoolsAsync(date);

        var stocks = await LoadStockSnapshotAsync();
        var stockStats = ComputeStockStats(stocks);
        var basisDate = stocks.Count > 0 ? stocks.Max(x => x.UpdatedAt).Date : (DateTime?)null;

        if (breadth is null && pools is null && stocks.Count == 0)
        {
            logger.LogWarning("市场情绪采集失败：指数快照、涨停池与基本面快照均不可用");
            return;
        }

        var limitUp = pools?.LimitUp ?? stockStats.LimitUp;
        var limitDown = pools?.LimitDown ?? stockStats.LimitDown;
        var broken = pools?.Broken ?? 0;
        var maxStreak = pools?.MaxStreak ?? 0;
        var amount = breadth is { Amount: > 0 } ? breadth.Amount : await GetIndexAmountAsync(date);
        var avg20 = await GetAvgAmountAsync(date);

        var score = ComputeScore(
            breadth?.Up ?? stockStats.Up,
            breadth?.Down ?? stockStats.Down,
            limitUp, limitDown, pools?.Broken, maxStreak, amount, avg20);

        var row = await db.MarketSentimentDailies.FirstOrDefaultAsync(x => x.Date == date);
        if (row is null)
        {
            row = new MarketSentimentDaily { Date = date };
            db.MarketSentimentDailies.Add(row);
        }

        row.UpCount = breadth?.Up ?? stockStats.Up;
        row.DownCount = breadth?.Down ?? stockStats.Down;
        row.FlatCount = breadth?.Flat ?? stockStats.Flat;
        row.Up5Count = stockStats.Up5;
        row.Down5Count = stockStats.Down5;
        row.LimitUpCount = limitUp;
        row.LimitDownCount = limitDown;
        row.BrokenBoardCount = broken;
        row.MaxBoardStreak = maxStreak;
        row.TotalAmount = Math.Round(amount, 2);
        row.AvgChangePct = stockStats.AvgChangePct;
        row.MedianChangePct = stockStats.MedianChangePct;
        row.SentimentScore = score;
        row.BasisDate = basisDate ?? DateTime.MinValue;
        row.UpdatedAt = DateTime.Now;

        await db.SaveChangesAsync();
        logger.LogInformation(
            "市场情绪 {Date:yyyy-MM-dd} 入库：涨 {Up} 跌 {Down} 平 {Flat}，涨停 {LimitUp} 跌停 {LimitDown} 炸板 {Broken}，最高 {Streak} 连板，成交 {Amount:F0} 亿，情绪分 {Score}",
            date, row.UpCount, row.DownCount, row.FlatCount,
            row.LimitUpCount, row.LimitDownCount, row.BrokenBoardCount, row.MaxBoardStreak, row.TotalAmount, score);
    }

    // ---------- 查询 ----------

    public async Task<SentimentOverviewDto> GetOverviewAsync()
    {
        var rows = await db.MarketSentimentDailies
            .AsNoTracking()
            .OrderByDescending(x => x.Date)
            .Take(2)
            .ToListAsync();
        if (rows.Count == 0) return new SentimentOverviewDto(null);

        var latest = rows[0];
        var prev = rows.Count > 1 ? rows[1] : null;
        var (level, advice) = Describe(latest.SentimentScore);

        var dto = new SentimentSnapshotDto(
            latest.Date.ToString("yyyy-MM-dd"),
            latest.UpCount, latest.DownCount, latest.FlatCount,
            latest.Up5Count, latest.Down5Count,
            latest.LimitUpCount, latest.LimitDownCount, latest.BrokenBoardCount, latest.MaxBoardStreak,
            latest.TotalAmount, latest.AvgChangePct, latest.MedianChangePct,
            latest.SentimentScore, level, advice,
            latest.BasisDate == DateTime.MinValue ? null : latest.BasisDate.ToString("yyyy-MM-dd"),
            prev is null ? null : Math.Round(latest.SentimentScore - prev.SentimentScore, 1),
            prev is null || prev.TotalAmount <= 0
                ? null
                : Math.Round((latest.TotalAmount - prev.TotalAmount) / prev.TotalAmount * 100, 2));
        return new SentimentOverviewDto(dto);
    }

    public async Task<SentimentHistoryDto> GetHistoryAsync(int days)
    {
        var since = DateTime.Today.AddDays(-Math.Clamp(days, 7, 365));
        var rows = await db.MarketSentimentDailies
            .AsNoTracking()
            .Where(x => x.Date >= since)
            .OrderBy(x => x.Date)
            .ToListAsync();

        return new SentimentHistoryDto(rows.Select(r => new SentimentHistoryPointDto(
            r.Date.ToString("yyyy-MM-dd"), r.SentimentScore,
            r.UpCount, r.DownCount, r.FlatCount,
            r.LimitUpCount, r.LimitDownCount, r.BrokenBoardCount, r.MaxBoardStreak,
            r.TotalAmount, r.AvgChangePct)).ToList());
    }

    /// <summary>全市场行业冷暖与涨跌分布（基于最近一次基本面快照，不现拉第三方）。</summary>
    public async Task<SentimentStructureDto> GetStructureAsync()
    {
        var rows = await db.StockFundamentals
            .AsNoTracking()
            .Where(x => x.ChangePct != null)
            .Select(x => new { x.Symbol, x.Name, x.Industry, x.ChangePct, x.UpdatedAt })
            .ToListAsync();

        var stocks = rows
            .Select(x => new StockStatRow(x.Symbol, x.Name, x.Industry, x.ChangePct ?? 0, x.UpdatedAt))
            .ToList();

        var basisDate = stocks.Count > 0 ? stocks.Max(x => x.UpdatedAt).Date : (DateTime?)null;

        var industries = stocks
            .Where(x => !string.IsNullOrWhiteSpace(x.Industry) && x.Industry != "-")
            .GroupBy(x => x.Industry!)
            .Select(g => new SentimentIndustryDto(
                g.Key, g.Count(),
                Math.Round(g.Average(x => x.ChangePct), 2),
                Math.Round(g.Count(x => x.ChangePct > 0) * 100.0 / g.Count(), 1),
                g.Count(x => IsLimitUp(x)),
                g.Count(x => IsLimitDown(x))))
            .OrderByDescending(x => x.AvgChangePct)
            .ToList();

        // 样本过少的行业波动大，优先过滤噪音；行业数不足时保留全量，避免页面空列表
        var ranked = industries.Where(x => x.Count >= 3).ToList();
        if (ranked.Count < 6) ranked = industries;

        return new SentimentStructureDto(
            basisDate?.ToString("yyyy-MM-dd"),
            stocks.Count,
            ranked.Take(10).ToList(),
            ranked.OrderBy(x => x.AvgChangePct).Take(10).ToList(),
            BuildDistribution(stocks));
    }

    // ---------- 东财指数快照（涨跌家数 / 成交额） ----------

    private sealed record BreadthData(DateTime Date, int Up, int Down, int Flat, double Amount);

    private async Task<BreadthData?> FetchBreadthAsync()
    {
        try
        {
            var client = factory.CreateClient("eastmoney");
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            client.DefaultRequestHeaders.Referrer = new Uri("https://quote.eastmoney.com/");

            // f104/f105/f106 = 上涨/下跌/平盘家数，f6 = 成交额（元），f124 = 快照时间戳
            const string url = "https://push2.eastmoney.com/api/qt/ulist.np/get" +
                "?fltt=2&invt=2&fields=f3,f6,f12,f104,f105,f106,f124&secids=1.000001,0.399001";
            using var doc = JsonDocument.Parse(await client.GetStringAsync(url));
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
                return null;
            if (!data.TryGetProperty("diff", out var diff) || diff.ValueKind != JsonValueKind.Array)
                return null;

            var up = 0;
            var down = 0;
            var flat = 0;
            var amount = 0.0;
            DateTime? date = null;

            foreach (var row in diff.EnumerateArray())
            {
                up += (int)(GetNumber(row, "f104") ?? 0);
                down += (int)(GetNumber(row, "f105") ?? 0);
                flat += (int)(GetNumber(row, "f106") ?? 0);
                amount += GetNumber(row, "f6") ?? 0;

                if (date is null && GetNumber(row, "f124") is { } ts && ts > 0)
                    date = DateTimeOffset.FromUnixTimeSeconds((long)ts).LocalDateTime.Date;
            }

            if (up + down + flat == 0) return null;
            return new BreadthData(date ?? DateTime.Today, up, down, flat, amount / 1e8);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "指数涨跌家数快照获取失败");
            return null;
        }
    }

    // ---------- 东财涨停板行情池 ----------

    private sealed record PoolData(int LimitUp, int LimitDown, int Broken, int MaxStreak);

    /// <summary>获取指定交易日的涨停 / 跌停 / 炸板池统计；任一子池失败返回 null（整体不可用）。</summary>
    private async Task<PoolData?> FetchPoolsAsync(DateTime date)
    {
        try
        {
            var zt = await FetchPoolAsync("ZT", date);
            var dt = await FetchPoolAsync("DT", date);
            var zb = await FetchPoolAsync("ZB", date);
            if (zt is null || dt is null || zb is null) return null;
            return new PoolData(zt.Value.Count, dt.Value.Count, zb.Value.Count, zt.Value.MaxStreak);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "涨停板行情池获取失败（{Date:yyyy-MM-dd}）", date);
            return null;
        }
    }

    private async Task<(int Count, int MaxStreak)?> FetchPoolAsync(string type, DateTime date)
    {
        var client = factory.CreateClient("eastmoney");
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.Referrer = new Uri("https://quote.eastmoney.com/ztb");

        // 跌停池按封单资金排序（fbt 字段在 DT 池不适用，会返回空列表）
        var sort = type == "DT" ? "fund:asc" : "fbt:asc";
        var url = $"https://push2ex.eastmoney.com/getTopic{type}Pool" +
            $"?ut={Ut}&dpt=wz.ztzt&Pageindex=0&pagesize=10000&sort={sort}&date={date:yyyyMMdd}";
        using var doc = JsonDocument.Parse(await client.GetStringAsync(url));
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
            return null;

        var count = (int)(GetNumber(data, "tc") ?? 0);
        var maxStreak = 0;
        if (data.TryGetProperty("pool", out var pool) && pool.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in pool.EnumerateArray())
            {
                // ZT 池的 lbc = 连板数；DT/ZB 池没有该字段
                var boards = (int)(GetNumber(item, "lbc") ?? 0);
                if (boards > maxStreak) maxStreak = boards;
            }
        }
        return (count, maxStreak);
    }

    // ---------- 基本面快照统计 ----------

    internal sealed record StockStatRow(string Symbol, string Name, string? Industry, double ChangePct, DateTime UpdatedAt);

    private async Task<List<StockStatRow>> LoadStockSnapshotAsync()
        => (await db.StockFundamentals
                .AsNoTracking()
                .Where(x => x.ChangePct != null)
                .Select(x => new { x.Symbol, x.Name, x.Industry, x.ChangePct, x.UpdatedAt })
                .ToListAsync())
            .Select(x => new StockStatRow(x.Symbol, x.Name, x.Industry, x.ChangePct ?? 0, x.UpdatedAt))
            .ToList();

    private sealed record StockStats(
        int Up, int Down, int Flat, int Up5, int Down5,
        double AvgChangePct, double MedianChangePct, int LimitUp, int LimitDown);

    private static StockStats ComputeStockStats(List<StockStatRow> rows)
    {
        if (rows.Count == 0) return new StockStats(0, 0, 0, 0, 0, 0, 0, 0, 0);

        var values = rows.Select(x => x.ChangePct).OrderBy(x => x).ToList();
        var median = values.Count % 2 == 0
            ? (values[values.Count / 2 - 1] + values[values.Count / 2]) / 2
            : values[values.Count / 2];

        return new StockStats(
            Up: rows.Count(x => x.ChangePct > 0),
            Down: rows.Count(x => x.ChangePct < 0),
            Flat: rows.Count(x => x.ChangePct == 0),
            Up5: rows.Count(x => x.ChangePct >= 5),
            Down5: rows.Count(x => x.ChangePct <= -5),
            AvgChangePct: Math.Round(rows.Average(x => x.ChangePct), 2),
            MedianChangePct: Math.Round(median, 2),
            LimitUp: rows.Count(IsLimitUp),
            LimitDown: rows.Count(IsLimitDown));
    }

    /// <summary>按板块/ST 识别涨跌停幅度：主板 10%，创业板/科创板 20%，北交所 30%，ST 5%。</summary>
    private static double LimitThreshold(StockStatRow row)
    {
        if (row.Name.Contains("ST", StringComparison.OrdinalIgnoreCase)) return 5;
        if (row.Symbol.StartsWith("300") || row.Symbol.StartsWith("301")
            || row.Symbol.StartsWith("688") || row.Symbol.StartsWith("689")) return 20;
        if (row.Symbol.StartsWith('4') || row.Symbol.StartsWith('8')
            || row.Symbol.StartsWith("92", StringComparison.Ordinal)) return 30;
        return 10;
    }

    internal static bool IsLimitUp(StockStatRow row) => row.ChangePct >= LimitThreshold(row) - 0.1;

    internal static bool IsLimitDown(StockStatRow row) => row.ChangePct <= -LimitThreshold(row) + 0.1;

    internal static List<SentimentBucketDto> BuildDistribution(List<StockStatRow> stocks)
    {
        var labels = new[]
        {
            "≤ -7%", "-7% ~ -5%", "-5% ~ -3%", "-3% ~ -1%", "-1% ~ 0", "平盘",
            "0 ~ 1%", "1% ~ 3%", "3% ~ 5%", "5% ~ 7%", "≥ 7%",
        };
        var counts = new int[labels.Length];

        foreach (var s in stocks)
        {
            var p = s.ChangePct;
            var index = p switch
            {
                <= -7 => 0,
                < -5 => 1,
                < -3 => 2,
                < -1 => 3,
                < 0 => 4,
                0 => 5,
                < 1 => 6,
                < 3 => 7,
                < 5 => 8,
                < 7 => 9,
                _ => 10,
            };
            counts[index]++;
        }

        return labels.Select((label, i) => new SentimentBucketDto(label, counts[i])).ToList();
    }

    // ---------- 成交额与情绪分 ----------

    private async Task<DateTime?> GetLatestTradingDateAsync()
        => (await db.IndexDailies
            .Where(x => x.Code == "000001")
            .MaxAsync(x => (DateTime?)x.Date))?.Date;

    /// <summary>
    /// 把指数快照时间戳换算成数据真正归属的交易日：盘前（9:30 前）与周末的 f124 可能是当天，
    /// 但涨跌家数仍是上一交易日收盘口径，统一回退到最近交易日。
    /// </summary>
    private async Task<DateTime> ResolveTradeDateAsync(DateTime? snapshotDate)
    {
        var today = DateTime.Today;
        var date = snapshotDate?.Date ?? today;

        var isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        var isPreMarket = date == today && DateTime.Now.TimeOfDay < new TimeSpan(9, 30, 0);
        if (isWeekend || isPreMarket)
            date = await GetLatestTradingDateAsync() ?? date;

        // 库里还没有指数日线（新库首次启动）时，至少不要落到周末
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            date = date.AddDays(-(date.DayOfWeek == DayOfWeek.Saturday ? 1 : 2));

        return date;
    }

    /// <summary>两市成交额（亿元）：沪市用上证指数，深市用深证综指（覆盖全深市，深证成指只是成分股）。</summary>
    private async Task<double> GetIndexAmountAsync(DateTime date)
    {
        var rows = await db.IndexDailies
            .AsNoTracking()
            .Where(x => (x.Code == "000001" || x.Code == "399106") && x.Date == date)
            .Select(x => x.Amount)
            .ToListAsync();
        return rows.Sum() / 1e8;
    }

    /// <summary>近 20 个交易日两市成交额均值（亿元），用于判断放量/缩量。</summary>
    private async Task<double?> GetAvgAmountAsync(DateTime date)
    {
        var rows = await db.IndexDailies
            .AsNoTracking()
            .Where(x => (x.Code == "000001" || x.Code == "399106") && x.Date < date)
            .OrderByDescending(x => x.Date)
            .Take(80)
            .Select(x => new { x.Date, x.Amount })
            .ToListAsync();

        var byDate = rows
            .GroupBy(x => x.Date)
            .OrderByDescending(g => g.Key)
            .Take(20)
            .Select(g => g.Sum(x => x.Amount) / 1e8)
            .ToList();

        return byDate.Count > 0 ? byDate.Average() : null;
    }

    /// <summary>
    /// 情绪温度分（0-100）：上涨占比 35%、涨停跌停对比 25%、连板高度 15%、封板率 10%、量能 15%。
    /// 某个分项无数据时剔除其权重，按剩余权重归一化。
    /// </summary>
    internal static double ComputeScore(
        int up, int down, int limitUp, int limitDown, int? broken, int maxStreak,
        double amount, double? avg20Amount)
    {
        double score = 0;
        double weight = 0;

        if (up + down > 0)
        {
            score += up * 100.0 / (up + down) * 0.35;
            weight += 0.35;
        }
        if (limitUp + limitDown > 0)
        {
            score += limitUp * 100.0 / (limitUp + limitDown) * 0.25;
            weight += 0.25;
        }
        if (maxStreak > 0)
        {
            score += Math.Min(maxStreak, 6) / 6.0 * 100 * 0.15;
            weight += 0.15;
        }
        if (broken is { } brokenCount && limitUp + brokenCount > 0)
        {
            score += (1 - brokenCount * 1.0 / (limitUp + brokenCount)) * 100 * 0.10;
            weight += 0.10;
        }
        if (amount > 0 && avg20Amount is > 0)
        {
            var ratio = Math.Clamp(amount / avg20Amount.Value, 0.5, 1.5);
            score += (ratio - 0.5) * 100 * 0.15;
            weight += 0.15;
        }

        return weight > 0 ? Math.Round(score / weight, 1) : 0;
    }

    private static (string Level, string Advice) Describe(double score) => score switch
    {
        >= 80 => ("亢奋", "情绪过热，注意追高风险，可逐步兑现"),
        >= 60 => ("活跃", "赚钱效应较好，可顺势参与强势方向"),
        >= 40 => ("中性", "多空相对均衡，控制仓位、等待方向"),
        >= 20 => ("低迷", "赚钱效应偏弱，建议轻仓防守"),
        _ => ("冰点", "情绪极冷，关注超跌反弹与拐点信号"),
    };

    // ---------- JSON 辅助 ----------

    private static double? GetNumber(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Any, Cn, out var d) => d,
            _ => null,
        };
    }
}
