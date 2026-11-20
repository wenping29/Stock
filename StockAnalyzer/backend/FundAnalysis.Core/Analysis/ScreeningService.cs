using FundAnalysis.Data;
using FundAnalysis.Models;
using Microsoft.EntityFrameworkCore;

namespace FundAnalysis.Analysis;

public sealed class ScreeningService(AppDbContext db, AnalysisDataService data)
{
    public static string BoardOf(string code)
    {
        if (code.StartsWith("688") || code.StartsWith("689")) return "科创板";
        if (code.StartsWith("300") || code.StartsWith("301")) return "创业板";
        if (code.StartsWith('6')) return "沪市A股";
        if (code.StartsWith('0') || code.StartsWith('3')) return "深市A股";
        if (code.StartsWith("920") || code.StartsWith('8')) return "北交所";
        if (code.StartsWith("900")) return "沪市B股";
        if (code.StartsWith("200")) return "深市B股";
        if (code.StartsWith('4')) return "新三板";
        return "其他";
    }

    public static string MarketOf(string code)
    {
        if (code.StartsWith('6') || code.StartsWith("900")) return "SH";
        if (code.StartsWith("920") || code.StartsWith('8') || code.StartsWith('4')) return "BJ";
        return "SZ";
    }

    public static bool IsListable(string name)
        => !name.Contains("ST", StringComparison.OrdinalIgnoreCase) && !name.Contains('退');

    public async Task<List<StockBasic>> GetStockPoolAsync(string? pool, CancellationToken ct = default)
    {
        var query = db.StockBasics.AsNoTracking()
            .Where(x => x.Type == SymbolTypes.Stock)
            .OrderBy(x => x.Symbol);
        var stocks = await query.ToListAsync(ct);
        stocks = stocks.Where(x => IsListable(x.Name)).ToList();

        if (pool is "沪深300" or "中证500" or "创业板")
        {
            if (pool == "创业板") stocks = stocks.Where(x => x.Symbol.StartsWith('3')).ToList();
            stocks = stocks.Take(500).ToList();
        }
        return stocks;
    }

    public async Task<(List<ScreeningResultDto> Results, int Total)> RunAsync(
        string? stockPool, RuleNode? rules, string? startDate, string? endDate, string? period, CancellationToken ct = default)
    {
        var (valid, errors) = RuleEvaluator.Validate(rules);
        if (!valid) throw new AnalysisRequestException(string.Join("; ", errors));

        var stocks = await GetStockPoolAsync(stockPool, ct);
        var start = AnalysisDataService.ParseDate(startDate, new DateTime(2024, 1, 1));
        var end = AnalysisDataService.ParseDate(endDate, DateTime.Today);
        var p = AnalysisDataService.NormalizePeriod(period);
        // 全市场筛选只读库内数据，逐票回源会导致数百次串行请求。
        var klines = await data.GetKlineBatchAsync(stocks.Select(x => x.Symbol).ToList(), start, end, p, false, ct);
        var results = new List<ScreeningResultDto>();
        foreach (var stock in stocks)
        {
            ct.ThrowIfCancellationRequested();
            if (!klines.TryGetValue(stock.Symbol, out var rows) || rows.Count < 60) continue;
            if (!RuleEvaluator.Evaluate(rows, rules)) continue;

            var last = rows[^1];
            var prev = rows[^2];
            results.Add(new ScreeningResultDto
            {
                Code = stock.Symbol,
                Name = stock.Name,
                Close = last.Close,
                ChangePct = prev.Close == 0 ? 0 : (last.Close - prev.Close) / prev.Close * 100,
            });
        }

        results = results.OrderByDescending(x => x.ChangePct).ToList();
        return (results, stocks.Count);
    }

    public async Task<object> TestAsync(
        string? code, RuleNode? rules, string? startDate, string? endDate, string? period, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new AnalysisRequestException("缺少必要参数: code");
        var start = AnalysisDataService.ParseDate(startDate, new DateTime(2024, 1, 1));
        var end = AnalysisDataService.ParseDate(endDate, DateTime.Today);
        var p = AnalysisDataService.NormalizePeriod(period);
        var rows = AnalysisDataService.IsIndexCode(code)
            ? await data.GetIndexKlineAsync(code, startDate, endDate, p, ct)
            : await data.GetKlineAsync(code, startDate, endDate, p, ct);

        if (rows.Count < 30) return new { passed = false, reason = "数据不足" };
        return new { passed = RuleEvaluator.Evaluate(rows, rules), dataLength = rows.Count };
    }
}