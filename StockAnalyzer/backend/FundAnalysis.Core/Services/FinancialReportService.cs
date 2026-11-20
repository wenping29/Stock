using System.Globalization;
using System.Text.Json;
using FundAnalysis.Data;
using FundAnalysis.Models;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace FundAnalysis.Services;

/// <summary>
/// 个股多期财务摘要采集（移植自旧 stock-analyzer/scraper 的财务报表爬虫）：
///   数据源 = 东财 F10 主要财务指标 RPT_F10_FINANCE_MAINFINADATA（datacenter 域名，单票一次取最近 20 期）。
///   任务粒度：单票按需 / 自选（定时） / 全市场（手动，约 5000 票 × 1 秒，耗时约 1.5 小时）。
/// </summary>
public class FinancialReportService(
    AppDbContext db, IHttpClientFactory factory, IConfiguration config, ILogger<FinancialReportService> logger)
{
    private const string BaseUrl = "https://datacenter.eastmoney.com/securities/api/data/v1/get";
    private const int PeriodsPerStock = 20;
    private const int RequestIntervalMs = 1000;

    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    // ---------- 采集 ----------

    /// <summary>定时任务：同步自选 + 示例标的的多期财务数据（每票一次请求）。</summary>
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    [AutomaticRetry(Attempts = 3)]
    public async Task SyncWatchlistAsync()
    {
        var symbols = await GetWatchlistSymbolsAsync();
        if (symbols.Count == 0)
        {
            logger.LogInformation("财务数据同步跳过：自选/示例标的中没有股票");
            return;
        }
        await SyncSymbolsAsync(symbols, RequestIntervalMs);
    }

    /// <summary>手动任务：全市场 A 股多期财务数据（逐票慢速拉取，默认不进入定时调度）。</summary>
    [DisableConcurrentExecution(timeoutInSeconds: 14400)]
    [AutomaticRetry(Attempts = 1)]
    public async Task SyncAllAsync()
    {
        var symbols = await db.StockFundamentals
            .Where(x => x.Symbol.Length == 6)
            .Select(x => x.Symbol)
            .Distinct()
            .ToListAsync();
        if (symbols.Count == 0)
        {
            logger.LogWarning("财务数据全量同步跳过：基本面表为空，请先同步基本面");
            return;
        }
        await SyncSymbolsAsync(symbols, RequestIntervalMs);
    }

    /// <summary>单票同步（个股详情页按需刷新），不追加请求间隔。</summary>
    public async Task<(int Added, int Updated)> SyncSymbolAsync(string symbol)
        => await SyncSymbolCoreAsync(symbol.Trim(), CancellationToken.None);

    private async Task SyncSymbolsAsync(List<string> symbols, int intervalMs)
    {
        var added = 0;
        var updated = 0;
        var failed = 0;
        for (var i = 0; i < symbols.Count; i++)
        {
            try
            {
                var (a, u) = await SyncSymbolCoreAsync(symbols[i], CancellationToken.None);
                added += a;
                updated += u;
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogWarning(ex, "财务数据同步失败：{Symbol}", symbols[i]);
            }

            if ((i + 1) % 100 == 0)
                logger.LogInformation("财务数据进度 {Done}/{Total}（新增 {Added}，更新 {Updated}，失败 {Failed}）",
                    i + 1, symbols.Count, added, updated, failed);
            await Task.Delay(intervalMs);
        }

        logger.LogInformation("财务数据同步完成：{Total} 只，新增 {Added} 期，更新 {Updated} 期，失败 {Failed} 只",
            symbols.Count, added, updated, failed);
    }

    private async Task<(int Added, int Updated)> SyncSymbolCoreAsync(string symbol, CancellationToken ct)
    {
        if (symbol.Length != 6 || !symbol.All(char.IsDigit))
            throw new ArgumentException($"标的代码格式不正确：{symbol}");

        var body = await FetchAsync(symbol, ct);
        if (body is null) return (0, 0);

        var rows = ParseReports(body, symbol);
        if (rows.Count == 0)
        {
            logger.LogDebug("财务数据 {Symbol} 无记录（可能为基金/新上市标的）", symbol);
            return (0, 0);
        }

        var existing = await db.StockFinancialReports
            .Where(x => x.Symbol == symbol)
            .ToDictionaryAsync(x => x.ReportDate, ct);

        var added = 0;
        var updated = 0;
        foreach (var row in rows)
        {
            if (existing.TryGetValue(row.ReportDate, out var local))
            {
                CopyMetrics(row, local);
                local.UpdatedAt = DateTime.Now;
                updated++;
            }
            else
            {
                db.StockFinancialReports.Add(row);
                added++;
            }
        }
        await db.SaveChangesAsync(ct);
        return (added, updated);
    }

    private static void CopyMetrics(StockFinancialReport src, StockFinancialReport dst)
    {
        dst.Name = src.Name;
        dst.ReportType = src.ReportType;
        dst.Eps = src.Eps;
        dst.Bps = src.Bps;
        dst.Roe = src.Roe;
        dst.Revenue = src.Revenue;
        dst.RevenueYoY = src.RevenueYoY;
        dst.NetProfit = src.NetProfit;
        dst.NetProfitYoY = src.NetProfitYoY;
        dst.GrossMargin = src.GrossMargin;
        dst.NetMargin = src.NetMargin;
        dst.DebtRatio = src.DebtRatio;
        dst.OcfPerShare = src.OcfPerShare;
        dst.DataJson = src.DataJson;
    }

    private async Task<string?> FetchAsync(string symbol, CancellationToken ct)
    {
        var secucode = ToSecuCode(symbol);
        var url = $"{BaseUrl}?reportName=RPT_F10_FINANCE_MAINFINADATA&columns=ALL" +
                  $"&filter=(SECUCODE%3D%22{secucode}%22)&pageNumber=1&pageSize={PeriodsPerStock}" +
                  "&sortTypes=-1&sortColumns=REPORT_DATE";

        var client = factory.CreateClient("eastmoney");
        // datacenter 对缺少 Referer 的请求可能返回空或断连，统一带 F10 站点 Referer
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.Referrer = new Uri("https://emweb.securities.eastmoney.com/");

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await client.GetStringAsync(url, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < 3)
            {
                logger.LogWarning("财务数据 {Symbol} 拉取失败（第 {Attempt} 次），稍后重试：{Error}",
                    symbol, attempt, ex.Message);
                await Task.Delay(2000 * attempt, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning("财务数据 {Symbol} 拉取失败，放弃本轮：{Error}", symbol, ex.Message);
                return null;
            }
        }
    }

    // ---------- 查询 ----------

    public async Task<List<StockFinancialReport>> GetAsync(string symbol, int limit, CancellationToken ct = default)
        => await db.StockFinancialReports.AsNoTracking()
            .Where(x => x.Symbol == symbol)
            .OrderByDescending(x => x.ReportDate)
            .Take(limit)
            .ToListAsync(ct);

    // ---------- 解析 ----------

    /// <summary>解析 F10 主要财务指标响应（internal 供单元测试直接验证）。金额元 → 亿元。</summary>
    internal static List<StockFinancialReport> ParseReports(string body, string fallbackSymbol)
    {
        var list = new List<StockFinancialReport>();
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("result", out var res) || res.ValueKind != JsonValueKind.Object)
            return list;
        if (!res.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var row in data.EnumerateArray())
        {
            var dateText = Str(row, "REPORT_DATE")?.Split(' ')[0];
            if (!DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var reportDate))
                continue;

            // 原始数值字段整体留存（旧 scraper 的 data JSON 口径）
            var raw = new Dictionary<string, double>();
            foreach (var prop in row.EnumerateObject())
            {
                double? n = prop.Value.ValueKind switch
                {
                    JsonValueKind.Number => prop.Value.GetDouble(),
                    JsonValueKind.String => double.TryParse(prop.Value.GetString(), NumberStyles.Any,
                        CultureInfo.InvariantCulture, out var d) ? d : null,
                    _ => null,
                };
                if (n is not null) raw[prop.Name] = n.Value;
            }
            double? Get(string key) => raw.TryGetValue(key, out var v) ? v : null;
            double? Yi(string key) => Get(key) is { } v ? Math.Round(v / 1e8, 2) : null;

            list.Add(new StockFinancialReport
            {
                Symbol = Str(row, "SECURITY_CODE") ?? fallbackSymbol,
                Name = Str(row, "SECURITY_NAME_ABBR") ?? fallbackSymbol,
                ReportDate = reportDate,
                ReportType = Str(row, "REPORT_TYPE") ?? "",
                Eps = Get("EPSJB"),
                Bps = Get("BPS"),
                Roe = Get("ROEJQ"),
                Revenue = Yi("TOTALOPERATEREVE"),
                RevenueYoY = Get("TOTALOPERATEREVETZ"),
                NetProfit = Yi("PARENTNETPROFIT"),
                NetProfitYoY = Get("PARENTNETPROFITTZ"),
                GrossMargin = Get("XSMLL"),
                NetMargin = Get("XSJLL"),
                DebtRatio = Get("ZCFZL"),
                OcfPerShare = Get("MGJYXJJE"),
                DataJson = JsonSerializer.Serialize(raw),
                UpdatedAt = DateTime.Now,
            });
        }

        // 同一响应内去重（按报告期），保持最新在前
        return list
            .GroupBy(x => x.ReportDate)
            .Select(g => g.First())
            .OrderByDescending(x => x.ReportDate)
            .ToList();
    }

    /// <summary>东财 SECUCODE 市场后缀：6=沪，4/8/92=北交所，其余=深。</summary>
    internal static string ToSecuCode(string symbol) => symbol switch
    {
        _ when symbol.StartsWith('6') => symbol + ".SH",
        _ when symbol.StartsWith('4') || symbol.StartsWith('8') || symbol.StartsWith("92") => symbol + ".BJ",
        _ => symbol + ".SZ",
    };

    private static string? Str(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private async Task<List<string>> GetWatchlistSymbolsAsync()
    {
        var symbols = await db.WatchlistItems.Select(x => x.Symbol).Distinct().ToListAsync();
        var samples = config.GetSection("Collector:SampleSymbols").Get<string[]>() ?? [];
        var merged = symbols.Concat(samples)
            .Where(s => s.Length == 6 && s.All(char.IsDigit))
            .Distinct()
            .ToList();
        if (merged.Count == 0) return merged;

        // 只有股票有 F10 财务数据；已知为 ETF/基金的代码剔除
        var types = await db.StockBasics
            .Where(x => merged.Contains(x.Symbol))
            .ToDictionaryAsync(x => x.Symbol, x => x.Type);
        return merged
            .Where(s => !types.TryGetValue(s, out var type) || type == SymbolTypes.Stock)
            .ToList();
    }
}