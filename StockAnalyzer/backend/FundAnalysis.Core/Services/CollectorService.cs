using System.Globalization;
using FundAnalysis.Data;
using FundAnalysis.Models;
using FundAnalysis.Services.Providers;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace FundAnalysis.Services;

/// <summary>从数据源拉取行情并幂等入库。所有方法可重复执行，已存在的数据跳过、当日数据更新。</summary>
public class CollectorService(
    AppDbContext db, IQuoteProvider provider, IHttpClientFactory factory,
    IConfiguration config, ILogger<CollectorService> logger)
{
    private const int DailyBars = 500;      // 约两年日线
    private const int MoneyFlowDays = 120;  // 约半年资金流
    private const int MinuteBars = 100;     // 约 2 个交易日的 5 分钟线

    /// <summary>首次启动的初始化任务：注册示例标的并拉取其全部数据。</summary>
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task InitialSyncAsync()
    {
        foreach (var symbol in GetSampleSymbols())
        {
            var basic = await EnsureBasicAsync(symbol);
            if (basic is null)
            {
                logger.LogWarning("示例标的 {Symbol} 未能通过搜索接口注册，跳过", symbol);
                continue;
            }
            await SyncSymbolAsync(symbol);
        }
    }

    /// <summary>确保标的已注册到 StockBasic。本地没有时通过搜索接口按代码精确查找并入库。</summary>
    public async Task<StockBasic?> EnsureBasicAsync(string symbol)
    {
        var local = await db.StockBasics.FirstOrDefaultAsync(x => x.Symbol == symbol);
        if (local is not null) return local;

        var suggestions = await provider.FetchSuggestAsync(symbol);
        var match = suggestions.FirstOrDefault(x => x.Symbol == symbol);
        if (match is null) return null;

        db.StockBasics.Add(match);
        await db.SaveChangesAsync();
        logger.LogInformation("标的 {Symbol} {Name} 已注册", match.Symbol, match.Name);
        return match;
    }

    /// <summary>
    /// 同步内置指数清单的日线（IndexDaily）。逐个请求且间隔 2 秒——
    /// 东财 push2his 对同 IP 高频连续请求会触发断连风控，必须低频慢速拉取。
    /// 风控惩罚可持续数小时，重试次数放大到 30 次（按递增间隔覆盖大半天）。
    /// </summary>
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 30)]
    public async Task SyncIndicesAsync()
    {
        var consecutiveFailures = 0;

        foreach (var (code, name, _, market) in MarketIndices.List)
        {
            try
            {
                // 1500 根约 6 年日线，支撑成交量年粒度聚合
                var bars = await provider.FetchKLineAsync(code, market, 101, 1500, fqt: 0);
                var existingDates = (await db.IndexDailies
                    .Where(x => x.Code == code)
                    .Select(x => x.Date)
                    .ToListAsync()).ToHashSet();

                var added = 0;
                foreach (var b in bars)
                {
                    if (existingDates.Contains(b.Time)) continue;
                    db.IndexDailies.Add(new IndexDaily
                    {
                        Code = code,
                        Market = market,
                        Date = b.Time,
                        Open = b.Open,
                        High = b.High,
                        Low = b.Low,
                        Close = b.Close,
                        Volume = b.Volume,
                        Amount = b.Amount,
                        ChangePct = b.ChangePct,
                    });
                    added++;
                }
                await db.SaveChangesAsync();
                logger.LogInformation("指数 {Code} {Name} 同步完成，新增 {Count} 根", code, name, added);
                consecutiveFailures = 0;
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                logger.LogError(ex, "指数 {Code} {Name} 同步失败（连续失败 {Count}）", code, name, consecutiveFailures);
                // 连续 3 个失败视为触发东财 IP 风控，中止本轮（Hangfire 稍后自动重试整个任务）
                if (consecutiveFailures >= 3)
                    throw new InvalidOperationException("连续 3 个指数同步失败，疑似触发东财 IP 频率风控，中止本轮同步");
            }

            // 低频请求防止触发东财 IP 风控（长惩罚刚解除时 2 秒间隔仍会再次触发，放宽到 6 秒）
            await Task.Delay(6000);
        }
    }

    /// <summary>同步全市场股票/ETF 基础列表。</summary>
    public async Task SyncStockListAsync()
    {
        try
        {
            var remote = await provider.FetchStockListAsync();
            var existing = await db.StockBasics.ToDictionaryAsync(x => x.Symbol);

            var added = 0;
            foreach (var item in remote)
            {
                if (existing.TryGetValue(item.Symbol, out var local))
                {
                    local.Name = item.Name;
                    local.SecMarket = item.SecMarket;
                    local.Type = item.Type;
                    local.UpdatedAt = DateTime.Now;
                }
                else
                {
                    db.StockBasics.Add(item);
                    added++;
                }
            }
            await db.SaveChangesAsync();
            logger.LogInformation("标的列表同步完成：共 {Total} 条，新增 {Added} 条", remote.Count, added);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "标的列表同步失败");
        }
    }

    /// <summary>定时任务：同步所有用户自选 + 示例标的的日线、资金流、基金净值。</summary>
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task SyncWatchlistDailyAsync()
    {
        foreach (var symbol in GetAllSymbols())
            await SyncSymbolAsync(symbol, includeMinutes: false);
    }

    /// <summary>定时任务：交易时段同步自选标的的 5 分钟线（用于小时趋势图）。</summary>
    public async Task SyncWatchlistMinutesAsync()
    {
        var basics = await GetBasicsAsync(GetAllSymbols());
        foreach (var (symbol, basic) in basics)
        {
            if (basic.Type == SymbolTypes.Fund) continue;
            await SyncMinutesAsync(basic);
        }
    }

    /// <summary>同步单个标的的全部数据（添加自选时立即调用，让数据尽快可见）。</summary>
    public async Task SyncSymbolAsync(string symbol)
        => await SyncSymbolAsync(symbol, includeMinutes: true);

    private async Task SyncSymbolAsync(string symbol, bool includeMinutes)
    {
        var basics = await GetBasicsAsync([symbol]);
        if (!basics.TryGetValue(symbol, out var basic))
        {
            logger.LogWarning("未找到标的 {Symbol}，跳过同步（可能标的列表尚未同步）", symbol);
            return;
        }

        try
        {
            if (basic.Type == SymbolTypes.Fund)
            {
                await SyncFundNavAsync(basic);
            }
            else
            {
                await SyncDailyAsync(basic);
                await SyncMoneyFlowAsync(basic);
                if (includeMinutes)
                    await SyncMinutesAsync(basic);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "标的 {Symbol} 同步失败", symbol);
        }
    }

    private async Task SyncDailyAsync(StockBasic basic)
    {
        var items = await provider.FetchKLineAsync(basic.Symbol, basic.SecMarket, 101, DailyBars);
        var existingDates = (await db.StockDailies
            .Where(x => x.Symbol == basic.Symbol)
            .Select(x => x.Date)
            .ToListAsync()).ToHashSet();

        var latest = items.Count > 0 ? items[^1] : null;
        StockDaily? latestEntity = null;
        if (latest != null && existingDates.Contains(latest.Time))
            latestEntity = await db.StockDailies.FirstAsync(x => x.Symbol == basic.Symbol && x.Date == latest.Time);

        foreach (var item in items)
        {
            if (existingDates.Contains(item.Time))
            {
                // 已有数据只更新最新一根（盘中收盘价会变）
                if (latest != null && item.Time == latest.Time && latestEntity != null)
                {
                    latestEntity.Open = item.Open;
                    latestEntity.High = item.High;
                    latestEntity.Low = item.Low;
                    latestEntity.Close = item.Close;
                    latestEntity.Volume = item.Volume;
                    latestEntity.Amount = item.Amount;
                    latestEntity.ChangePct = item.ChangePct;
                }
                continue;
            }
            db.StockDailies.Add(new StockDaily
            {
                Symbol = basic.Symbol,
                Date = item.Time,
                Open = item.Open,
                High = item.High,
                Low = item.Low,
                Close = item.Close,
                Volume = item.Volume,
                Amount = item.Amount,
                ChangePct = item.ChangePct,
            });
        }
        await db.SaveChangesAsync();
        logger.LogInformation("{Symbol} {Name} 日线同步完成 {Count} 根", basic.Symbol, basic.Name, items.Count);
    }

    private async Task SyncMinutesAsync(StockBasic basic)
    {
        var items = await provider.FetchKLineAsync(basic.Symbol, basic.SecMarket, 5, MinuteBars);
        var existingTimes = (await db.StockMinutes
            .Where(x => x.Symbol == basic.Symbol)
            .Select(x => x.Time)
            .ToListAsync()).ToHashSet();

        foreach (var item in items)
        {
            if (existingTimes.Contains(item.Time)) continue;
            db.StockMinutes.Add(new StockMinute
            {
                Symbol = basic.Symbol,
                Time = item.Time,
                Open = item.Open,
                High = item.High,
                Low = item.Low,
                Close = item.Close,
                Volume = item.Volume,
            });
        }
        await db.SaveChangesAsync();
    }

    private async Task SyncMoneyFlowAsync(StockBasic basic)
    {
        var items = await provider.FetchMoneyFlowAsync(basic.Symbol, basic.SecMarket, MoneyFlowDays);
        var existingDates = (await db.MoneyFlowDailies
            .Where(x => x.Symbol == basic.Symbol)
            .Select(x => x.Date)
            .ToListAsync()).ToHashSet();

        foreach (var item in items)
        {
            if (existingDates.Contains(item.Date)) continue;
            db.MoneyFlowDailies.Add(new MoneyFlowDaily
            {
                Symbol = basic.Symbol,
                Date = item.Date,
                MainNet = item.MainNet,
                LargeNet = item.LargeNet,
                MidNet = item.MidNet,
                SmallNet = item.SmallNet,
            });
        }
        await db.SaveChangesAsync();
    }

    private async Task SyncFundNavAsync(StockBasic basic)
    {
        // 基金在 StockBasics 中的 Symbol 带 ".F" 后缀（与股票代码空间区分），查净值用裸代码
        var fundCode = basic.Symbol.EndsWith(".F") ? basic.Symbol[..^2] : basic.Symbol;
        var items = await provider.FetchFundNavAsync(fundCode, DailyBars);
        var existingDates = (await db.FundNavs
            .Where(x => x.FundCode == basic.Symbol)
            .Select(x => x.Date)
            .ToListAsync()).ToHashSet();

        foreach (var item in items)
        {
            if (existingDates.Contains(item.Date)) continue;
            db.FundNavs.Add(new FundNav
            {
                FundCode = basic.Symbol,
                Date = item.Date,
                Nav = item.Nav,
                AccNav = item.AccNav,
                ChangePct = item.ChangePct,
            });
        }
        await db.SaveChangesAsync();
        logger.LogInformation("{Symbol} {Name} 基金净值同步完成 {Count} 条", basic.Symbol, basic.Name, items.Count);
    }

    /// <summary>
    /// 同步宏观 / 外汇日度序列（离岸人民币 USDCNH、在岸中间价 USDCNYC、美债 US3M/US6M/US10Y、国债 CN5Y/CN10Y/CN30Y）入库；
    /// 同时把汇率（fx_cny_usd）、中债（cn10y）、黄金（COMEX）、原油（NYMEX）日线追加到 MarketSeriesPoints，
    /// 供大盘页多周期曲线与宏观速览使用（多周期在查询时聚合）。
    /// 全部走 push2his/push2，上游不可达时单序列失败仅记录告警，不影响其余序列与后续任务。
    /// </summary>
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task SyncMarketIndicatorsAsync()
    {
        var series = new (string Symbol, int SecMarket, int Klt, int Limit, int Fqt)[]
        {
            ("USDCNH", 133, 101, 365, 1),
            ("US3M", 171, 101, 365, 0),
            ("US6M", 171, 101, 365, 0),
            ("US10Y", 171, 101, 365, 0),
            ("CN5Y", 171, 101, 365, 0),
            ("CN10Y", 171, 101, 365, 0),
            ("CN30Y", 171, 101, 365, 0),
        };

        var barsBySymbol = new Dictionary<string, List<KLineItem>>();
        foreach (var s in series)
        {
            try
            {
                var bars = await provider.FetchKLineAsync(s.Symbol, s.SecMarket, s.Klt, s.Limit, s.Fqt);
                barsBySymbol[s.Symbol] = bars;
                await UpsertIndicatorSeriesAsync(s.Symbol,
                    bars.Select(b => (b.Time.Date, b.Close, (double?)null)));
                logger.LogInformation("宏观序列 {Symbol} 入库 {Count} 条", s.Symbol, bars.Count);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "宏观序列 {Symbol} 同步失败（上游不可达？）", s.Symbol);
            }
        }

        // 汇率 / 中债曲线：复用上面已拉取的日线，写入通用序列表
        await UpsertMarketSeriesAsync("fx_cny_usd", barsBySymbol.GetValueOrDefault("USDCNH"));
        await UpsertMarketSeriesAsync("cn10y", barsBySymbol.GetValueOrDefault("CN10Y"));

        // 全球商品日线（东财全球期货主连）：gold_price = COMEX 黄金，crude_oil = NYMEX 原油
        var commodities = new (string Key, string Symbol, int SecMarket)[]
        {
            ("gold_price", "GC00Y", 101),
            ("crude_oil", "CL00Y", 102),
        };
        foreach (var c in commodities)
        {
            try
            {
                var bars = await provider.FetchKLineAsync(c.Symbol, c.SecMarket, 101, 4000, fqt: 0);
                await UpsertMarketSeriesAsync(c.Key, bars);
                logger.LogInformation("商品序列 {Key}（{Symbol}）同步完成，{Count} 条", c.Key, c.Symbol, bars.Count);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "商品序列 {Key}（{Symbol}）同步失败（上游不可达？）", c.Key, c.Symbol);
            }
        }

        // 美元利率：SOFR（东财无该指标，改读 FRED 公共 CSV；失败只告警）
        try
        {
            await SyncUsdRateAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "美元利率（SOFR）同步失败（上游不可达？）");
        }

        // 在岸中间价（push2 实时单点，本机可达）
        try
        {
            var (value, chg) = await provider.FetchParityAsync();
            if (value > 0)
            {
                await UpsertIndicatorAsync("USDCNYC", DateTime.Now.Date, value, chg);
                logger.LogInformation("在岸中间价 {Value} 入库", value);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "在岸中间价同步失败");
        }
    }

    /// <summary>
    /// 交易时段同步内置指数日内分时（分钟级）入库。使用 push2 trends2（本机可达），
    /// Web 端改为读库后，请求路径彻底不再现拉第三方接口。
    /// </summary>
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task SyncIndexIntradayAsync()
    {
        foreach (var (code, _, _, market) in MarketIndices.List)
        {
            try
            {
                var trend = await provider.FetchTrendAsync(code, market);
                if (trend.Count == 0) continue;

                var existing = (await db.IndexIntradays
                    .Where(x => x.Code == code)
                    .Select(x => x.Time)
                    .ToListAsync()).ToHashSet();

                foreach (var t in trend)
                {
                    if (existing.Contains(t.Time)) continue;
                    db.IndexIntradays.Add(new IndexIntraday
                    {
                        Code = code,
                        Time = t.Time,
                        Price = t.Price,
                        Volume = t.Volume,
                        Avg = t.Avg,
                        Amount = t.Amount,
                    });
                }
                await db.SaveChangesAsync();
                logger.LogInformation("指数 {Code} 日内分时入库 {Count} 根", code, trend.Count);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "指数 {Code} 日内分时同步失败", code);
            }
        }
    }

    /// <summary>
    /// 一键全量采集（前端"全部刷新"）：按顺序执行主要分类，后台运行、失败不自动重试。
    /// 各子方法自带 <see cref="Hangfire.DisableConcurrentExecutionAttribute"/> 防并发。
    /// </summary>
    [DisableConcurrentExecution(timeoutInSeconds: 900)]
    [AutomaticRetry(Attempts = 0)]
    public async Task SyncAllCategoriesAsync()
    {
        await SyncIndicesAsync();
        await SyncMarketIndicatorsAsync();
        await SyncIndexIntradayAsync();
        await SyncWatchlistDailyAsync();
        await SyncWatchlistMinutesAsync();
        await SyncSectorMoneyFlowAsync();
    }

    /// <summary>行情序列日线追加（仅新增缺失日期，历史导入数据保持原值）。</summary>
    private async Task UpsertMarketSeriesAsync(string key, IReadOnlyList<KLineItem>? bars)
    {
        if (bars is null || bars.Count == 0) return;

        var existing = (await db.MarketSeriesPoints
            .Where(x => x.Key == key && x.Period == "daily")
            .Select(x => x.Date)
            .ToListAsync()).ToHashSet();

        var added = 0;
        foreach (var b in bars)
        {
            if (!existing.Add(b.Time.Date)) continue;
            db.MarketSeriesPoints.Add(new MarketSeriesPoint
            {
                Key = key,
                Period = "daily",
                Date = b.Time.Date,
                Open = b.Open,
                High = b.High,
                Low = b.Low,
                Close = b.Close,
            });
            added++;
        }
        if (added > 0) await db.SaveChangesAsync();
        logger.LogInformation("行情序列 {Key} 日线新增 {Count} 条", key, added);
    }

    /// <summary>美元利率（SOFR）：FRED 公共 CSV，全量拉取后仅补缺失日期。</summary>
    private async Task SyncUsdRateAsync()
    {
        var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);
        var csv = await client.GetStringAsync("https://fred.stlouisfed.org/graph/fredgraph.csv?id=SOFR");
        var rows = ParseFredCsv(csv);
        await UpsertIndicatorSeriesAsync("SOFR", rows.Select(r => (r.Date, r.Value, (double?)null)));
        logger.LogInformation("美元利率 SOFR 入库 {Count} 条", rows.Count);
    }

    /// <summary>解析 FRED fredgraph.csv（date,value；缺值行为空或 "."）。</summary>
    internal static List<(DateTime Date, double Value)> ParseFredCsv(string csv)
    {
        var rows = new List<(DateTime, double)>();
        foreach (var raw in csv.Split('\n'))
        {
            var parts = raw.Trim().Split(',');
            if (parts.Length != 2) continue;
            if (!DateTime.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) continue;
            rows.Add((date, value));
        }
        return rows;
    }

    private async Task UpsertIndicatorSeriesAsync(string symbol, IEnumerable<(DateTime Date, double Value, double? ChangePct)> rows)
    {
        var existing = (await db.MarketIndicators
            .Where(x => x.Symbol == symbol)
            .Select(x => x.Date)
            .ToListAsync()).ToHashSet();
        foreach (var r in rows)
        {
            if (existing.Contains(r.Date)) continue;
            db.MarketIndicators.Add(new MarketIndicator
            {
                Symbol = symbol,
                Date = r.Date,
                Value = r.Value,
                ChangePct = r.ChangePct,
            });
        }
        await db.SaveChangesAsync();
    }

    private async Task UpsertIndicatorAsync(string symbol, DateTime date, double value, double? changePct)
    {
        var existing = await db.MarketIndicators
            .FirstOrDefaultAsync(x => x.Symbol == symbol && x.Date == date);
        if (existing is null)
        {
            db.MarketIndicators.Add(new MarketIndicator
            {
                Symbol = symbol,
                Date = date,
                Value = value,
                ChangePct = changePct,
            });
        }
        else
        {
            existing.Value = value;
            existing.ChangePct = changePct;
        }
        await db.SaveChangesAsync();
    }

    /// <summary>定时任务：同步全市场行业板块主力资金流（当日快照，按板块代码覆盖写入）。</summary>
    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    public async Task SyncSectorMoneyFlowAsync()
    {
        try
        {
            var items = await provider.FetchSectorMoneyFlowAsync();
            if (items.Count == 0)
            {
                logger.LogWarning("行业板块资金流为空，跳过写入");
                return;
            }

            var date = DateTime.Now.Date;
            var existing = (await db.SectorMoneyFlows
                .Where(x => x.Date == date)
                .ToListAsync()).ToDictionary(x => x.Code);

            foreach (var item in items)
            {
                if (existing.TryGetValue(item.Code, out var row))
                {
                    row.Name = item.Name;
                    row.MainNet = item.MainNet;
                    row.Amount = item.Amount;
                    row.ChangePct = item.ChangePct;
                }
                else
                {
                    db.SectorMoneyFlows.Add(new SectorMoneyFlow
                    {
                        Code = item.Code,
                        Name = item.Name,
                        Date = date,
                        MainNet = item.MainNet,
                        Amount = item.Amount,
                        ChangePct = item.ChangePct,
                    });
                }
            }
            await db.SaveChangesAsync();
            logger.LogInformation("行业板块资金流同步完成：{Count} 个板块", items.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "行业板块资金流同步失败");
        }
    }

    /// <summary>所有用户自选 ∪ 配置中的示例标的。</summary>
    private string[] GetAllSymbols()
    {
        var watchlist = db.WatchlistItems.Select(x => x.Symbol).Distinct().ToList();
        return watchlist.Union(GetSampleSymbols()).Distinct().ToArray();
    }

    private string[] GetSampleSymbols()
        => config.GetSection("Collector:SampleSymbols").Get<string[]>() ?? [];

    private async Task<Dictionary<string, StockBasic>> GetBasicsAsync(IEnumerable<string> symbols)
        => await db.StockBasics.Where(x => symbols.Contains(x.Symbol)).ToDictionaryAsync(x => x.Symbol);
}
