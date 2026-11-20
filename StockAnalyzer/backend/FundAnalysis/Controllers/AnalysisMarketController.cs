using FundAnalysis.Analysis;
using Microsoft.AspNetCore.Mvc;

namespace FundAnalysis.Controllers;

/// <summary>量化分析：大盘指数 / 宏观 / 利率 / 商品与汇率序列（对齐 /api/market）。</summary>
[ApiController]
[Route("api/analysis/market")]
public sealed class AnalysisMarketController(MarketAnalysisService market) : ControllerBase
{
    [HttpGet("macro")]
    public async Task<IActionResult> Macro(CancellationToken ct)
        => Ok(new { success = true, data = await market.GetMacroAsync(ct) });

    [HttpGet("indexes/{code}/kline")]
    public async Task<IActionResult> IndexKline(
        string code, [FromQuery] string? start, [FromQuery] string? end,
        [FromQuery] string period = "daily", [FromQuery] int? limit = null,
        CancellationToken ct = default)
    {
        try
        {
            return Ok(new
            {
                success = true,
                data = await market.GetIndexKlineAsync(code, start, end, period, limit, ct),
            });
        }
        catch (AnalysisRequestException ex)
        {
            return StatusCode(ex.StatusCode, new { success = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

    [HttpGet("us-rates/{maturity}")]
    public async Task<IActionResult> UsRates(
        string maturity, [FromQuery] string? start, [FromQuery] string? end, CancellationToken ct)
    {
        if (!MarketAnalysisService.IsValidMaturity(maturity))
            return BadRequest(new { success = false, error = $"Invalid maturity. Valid values: {string.Join(", ", MarketAnalysisService.UsMaturities)}" });
        return Ok(new { success = true, data = await market.GetUsRatesAsync(maturity, start, end, ct) });
    }

    [HttpGet("cn-rates/{type}")]
    public async Task<IActionResult> CnRates(
        string type, [FromQuery] string? start, [FromQuery] string? end, CancellationToken ct)
    {
        if (!MarketAnalysisService.IsValidCnRateType(type))
            return BadRequest(new { success = false, error = $"Invalid type. Valid values: {string.Join(", ", MarketAnalysisService.CnRateTypes)}" });
        return Ok(new { success = true, data = await market.GetCnRatesAsync(type, start, end, ct) });
    }

    [HttpGet("index-data/{period}")]
    public async Task<IActionResult> IndexData(
        string period, [FromQuery] string? codes, [FromQuery] string? start, [FromQuery] string? end,
        CancellationToken ct)
    {
        if (!MarketAnalysisService.IsValidPeriod(period))
            return BadRequest(new { success = false, error = $"Invalid period. Valid values: {string.Join(", ", MarketAnalysisService.Periods)}" });

        var list = string.IsNullOrWhiteSpace(codes)
            ? null
            : codes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Ok(new { success = true, data = await market.GetIndexPeriodDataAsync(period, list, start, end, ct) });
    }

    [HttpGet("fx-cny-usd/{period}")]
    public async Task<IActionResult> Fx(
        string period, [FromQuery] string? start, [FromQuery] string? end, CancellationToken ct)
        => await Series(period, start, end, (p, s, e) => market.GetFxAsync(p, s, e, ct));

    [HttpGet("gold-price/{period}")]
    public async Task<IActionResult> Gold(
        string period, [FromQuery] string? start, [FromQuery] string? end, CancellationToken ct)
        => await Series(period, start, end, (p, s, e) => market.GetGoldAsync(p, s, e, ct));

    [HttpGet("crude-oil/{period}")]
    public async Task<IActionResult> CrudeOil(
        string period, [FromQuery] string? start, [FromQuery] string? end, CancellationToken ct)
        => await Series(period, start, end, (p, s, e) => market.GetCrudeOilAsync(p, s, e, ct));

    [HttpGet("cn10y/{period}")]
    public async Task<IActionResult> Cn10y(
        string period, [FromQuery] string? start, [FromQuery] string? end, CancellationToken ct)
        => await Series(period, start, end, (p, s, e) => market.GetCn10yAsync(p, s, e, ct));

    private async Task<IActionResult> Series(
        string period, string? start, string? end, Func<string, string?, string?, Task<List<object>>> load)
    {
        if (!MarketAnalysisService.IsValidPeriod(period))
            return BadRequest(new { success = false, error = $"Invalid period. Valid values: {string.Join(", ", MarketAnalysisService.Periods)}" });
        try
        {
            return Ok(new { success = true, data = await load(period, start, end) });
        }
        catch (AnalysisRequestException ex)
        {
            return StatusCode(ex.StatusCode, new { success = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }
}