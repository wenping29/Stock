using FundAnalysis.Analysis;
using Microsoft.AspNetCore.Mvc;

namespace FundAnalysis.Controllers;

/// <summary>量化分析：策略回测执行与历史结果（对齐 /api/backtesting）。</summary>
[ApiController]
[Route("api/backtesting")]
public sealed class AnalysisBacktestController(BacktestService backtest) : ControllerBase
{
    [HttpPost("run")]
    public async Task<IActionResult> Run([FromBody] BacktestRequest? config, CancellationToken ct)
    {
        if (config is null)
            return BadRequest(new { success = false, error = "缺少请求体" });
        try
        {
            var (id, result) = await backtest.RunAsync(config, ct);
            return Ok(new
            {
                success = true,
                data = new
                {
                    id,
                    config = result.Config,
                    trades = result.Trades,
                    equityCurve = result.EquityCurve,
                    metrics = result.Metrics,
                },
            });
        }
        catch (AnalysisRequestException ex)
        {
            return StatusCode(ex.StatusCode, new { success = false, error = ex.Message });
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

    [HttpGet("results")]
    public async Task<IActionResult> Results(CancellationToken ct)
        => Ok(new { success = true, data = await backtest.ListAsync(ct) });

    [HttpGet("results/{id}")]
    public async Task<IActionResult> Result(string id, CancellationToken ct)
    {
        var result = await backtest.GetAsync(id, ct);
        return result is null
            ? NotFound(new { success = false, error = "回测结果不存在" })
            : Ok(new { success = true, data = result });
    }
}