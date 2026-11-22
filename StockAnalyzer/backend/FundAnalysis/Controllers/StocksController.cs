using FundAnalysis.Data;
using FundAnalysis.Models;
using FundAnalysis.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FundAnalysis.Controllers;

/// <summary>个股浏览：全市场 A 股按板块（沪市主板/科创板/深市主板/创业板/北交所）浏览。登录即可使用。</summary>
[ApiController]
[Route("api/stocks")]
public class StocksController(FundamentalService fundamental, FinancialReportService financials, AppDbContext db) : ControllerBase
{
    private static string BoardOf(string symbol) =>
        FundamentalService.Boards
            .FirstOrDefault(b => b.Prefixes.Any(p => symbol.StartsWith(p)))
            is { Key: var k } ? k : "other";

    /// <summary>各板块股票数量。</summary>
    [Authorize]
    [HttpGet("boards")]
    public async Task<IActionResult> Boards()
    {
        var stats = await fundamental.GetBoardStatsAsync();
        var boards = new List<BoardStatDto> { new("all", "全部", stats.GetValueOrDefault("all")) };
        boards.AddRange(FundamentalService.Boards.Select(b => new BoardStatDto(b.Key, b.Name, stats.GetValueOrDefault(b.Key))));
        return Ok(boards);
    }

    /// <summary>个股列表：板块 + 关键字 + 行业 + 数值区间筛选 + 排序分页。</summary>
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string board = "all",
        [FromQuery] string? keyword = null,
        [FromQuery] string? industry = null,
        [FromQuery] string sort = "marketCap",
        [FromQuery] bool desc = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] StockFilterQuery? filter = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);

        // 上证指数按总市值加权且覆盖全部沪市 A 股，故权重 = 个股总市值 / 沪市 A 股总市值
        var shTotalCap = await db.StockFundamentals
            .Where(x => x.Symbol.StartsWith("6") && x.MarketCap > 0)
            .SumAsync(x => x.MarketCap ?? 0);

        var (total, items) = await fundamental.GetStocksAsync(
            board, keyword, industry, sort, desc, page, pageSize, filter, shTotalCap);

        return Ok(new StockListResponse(total, page, pageSize, items.Select(x =>
        {
            var weight = shTotalCap > 0 && x.Symbol.StartsWith("6") && x.MarketCap > 0
                ? Math.Round(x.MarketCap!.Value / shTotalCap * 100, 4)
                : (double?)null;
            return new StockListItemDto(
                x.Symbol, x.Name, x.Industry, BoardOf(x.Symbol),
                x.ClosePrice, x.ChangePct, x.PeTtm, x.PbMrq,
                x.MarketCap, x.Roe, x.RevenueYoY, x.NetProfitYoY, weight);
        }).ToList()));
    }

    /// <summary>个股多期财务摘要（报告期倒序，最多 40 期）。</summary>
    [Authorize]
    [HttpGet("{symbol}/financials")]
    public async Task<IActionResult> Financials(string symbol, [FromQuery] int limit = 20)
    {
        symbol = (symbol ?? string.Empty).Trim();
        if (symbol.Length != 6 || !symbol.All(char.IsDigit))
            return BadRequest(new { message = "标的代码格式不正确" });

        var rows = await financials.GetAsync(symbol, Math.Clamp(limit, 1, 40));
        return Ok(rows.Select(x => new FinancialReportDto(
            x.Symbol, x.Name, x.ReportDate.ToString("yyyy-MM-dd"), x.ReportType,
            x.Eps, x.Bps, x.Roe,
            x.Revenue, x.RevenueYoY, x.NetProfit, x.NetProfitYoY,
            x.GrossMargin, x.NetMargin, x.DebtRatio, x.OcfPerShare)).ToList());
    }
}
