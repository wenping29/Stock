using FundAnalysis.Analysis;
using FundAnalysis.Data;
using FundAnalysis.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FundAnalysis.Controllers;

/// <summary>量化分析：股票列表 / 搜索 / 详情 / K 线（对齐 stock-analyzer/server 的 /api/stock）。</summary>
[ApiController]
[Route("api/analysis/stock")]
public sealed class AnalysisStockController(AppDbContext db, AnalysisDataService data) : ControllerBase
{
    private static readonly Dictionary<string, Func<string, bool>> TypeMatchers = new()
    {
        ["all"] = _ => true,
        ["a"] = c => c.StartsWith('0') || c.StartsWith('3') || c.StartsWith('6'),
        ["b"] = c => c.StartsWith("900") || c.StartsWith("200"),
        ["sh"] = c => c.StartsWith('6'),
        ["sz"] = c => c.StartsWith('0') || c.StartsWith('3'),
        ["bj"] = c => c.StartsWith("92") || c.StartsWith('8'),
        ["chinext"] = c => c.StartsWith("30"),
        ["star"] = c => c.StartsWith("68"),
        ["neeq"] = c => c.StartsWith('4'),
    };

    [HttpGet("list")]
    public async Task<IActionResult> List([FromQuery] string type = "all", CancellationToken ct = default)
    {
        var stocks = await db.StockBasics.AsNoTracking()
            .Where(x => x.Type == SymbolTypes.Stock)
            .OrderBy(x => x.Symbol)
            .ToListAsync(ct);

        var matcher = TypeMatchers.GetValueOrDefault(type) ?? (_ => true);
        var result = stocks
            .Where(x => ScreeningService.IsListable(x.Name) && matcher(x.Symbol))
            .Select(ToInfo)
            .ToList();
        return Ok(new { success = true, data = result });
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q = "", CancellationToken ct = default)
    {
        var stocks = await db.StockBasics.AsNoTracking()
            .Where(x => x.Type == SymbolTypes.Stock)
            .OrderBy(x => x.Symbol)
            .ToListAsync(ct);

        var result = stocks
            .Where(x => ScreeningService.IsListable(x.Name) &&
                        (x.Symbol.Contains(q) || x.Name.Contains(q)))
            .Take(20)
            .Select(ToInfo)
            .ToList();
        return Ok(new { success = true, data = result });
    }

    [HttpGet("{code}/kline")]
    public async Task<IActionResult> Kline(
        string code, [FromQuery] string? start, [FromQuery] string? end,
        [FromQuery] string period = "daily", [FromQuery] string adjust = "qfq", CancellationToken ct = default)
    {
        try
        {
            var rows = await data.GetKlineAsync(code, start, end, period, ct);
            return Ok(new { success = true, data = rows });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

    [HttpGet("{code}")]
    public async Task<IActionResult> Detail(string code, CancellationToken ct = default)
    {
        var stock = await db.StockBasics.AsNoTracking().FirstOrDefaultAsync(x => x.Symbol == code, ct);
        object info = stock is null
            ? new { code, name = code, market = (string?)null, industry = (string?)null, board = ScreeningService.BoardOf(code) }
            : new
            {
                code = stock.Symbol,
                name = stock.Name,
                market = ScreeningService.MarketOf(stock.Symbol),
                industry = stock.Industry,
                board = ScreeningService.BoardOf(stock.Symbol),
            };

        var latest = await db.StockDailies.AsNoTracking()
            .Where(x => x.Symbol == code)
            .OrderByDescending(x => x.Date)
            .Take(2)
            .ToListAsync(ct);

        object? quote = null;
        if (latest.Count > 0)
        {
            var last = latest[0];
            var pct = latest.Count > 1 && latest[1].Close != 0
                ? (last.Close / latest[1].Close - 1) * 100
                : last.ChangePct;
            quote = new
            {
                code,
                name = stock?.Name ?? code,
                market = ScreeningService.MarketOf(code),
                price = last.Close,
                changePct = Math.Round(pct, 2),
                volume = last.Volume,
                high = last.High,
                low = last.Low,
                amount = last.Amount,
            };
        }
        return Ok(new { success = true, data = new { info, quote } });
    }

    private static object ToInfo(StockBasic s) => new
    {
        code = s.Symbol,
        name = s.Name,
        market = ScreeningService.MarketOf(s.Symbol),
        board = ScreeningService.BoardOf(s.Symbol),
        industry = s.Industry,
    };
}