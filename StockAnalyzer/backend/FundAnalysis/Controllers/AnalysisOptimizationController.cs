using FundAnalysis.Analysis;
using Microsoft.AspNetCore.Mvc;

namespace FundAnalysis.Controllers;

/// <summary>量化分析：参数网格搜索与滚动窗口验证（对齐 /api/optimization）。</summary>
[ApiController]
[Route("api/analysis/optimization")]
public sealed class AnalysisOptimizationController(AnalysisDataService data) : ControllerBase
{
    [HttpPost("grid-search")]
    public async Task<IActionResult> GridSearch([FromBody] OptimizationRequest? config, CancellationToken ct)
    {
        if (config is null)
            return BadRequest(new { success = false, error = "缺少请求体" });
        if (string.IsNullOrWhiteSpace(config.Code) || config.ParamRanges.Count == 0)
            return BadRequest(new { success = false, error = "缺少必要参数: code, paramRanges" });

        try
        {
            var kline = await LoadAsync(config.Code, config.StartDate, config.EndDate, config.Period, ct);
            if (kline.Count < 60)
                return BadRequest(new { success = false, error = $"数据不足 ({kline.Count}条)" });

            var results = OptimizationEngine.GridSearch(config, kline);
            return Ok(new
            {
                success = true,
                data = results,
                meta = new { totalCombinations = results.Count, metric = config.Metric },
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

    [HttpPost("walk-forward")]
    public async Task<IActionResult> WalkForward([FromBody] WalkForwardRequest? config, CancellationToken ct)
    {
        if (config is null)
            return BadRequest(new { success = false, error = "缺少请求体" });
        if (string.IsNullOrWhiteSpace(config.Code) || config.ParamRanges.Count == 0)
            return BadRequest(new { success = false, error = "缺少必要参数: code, paramRanges" });

        try
        {
            var kline = await LoadAsync(config.Code, config.StartDate, config.EndDate, config.Period, ct);
            var required = config.InsampleDays + config.OutsampleDays;
            if (kline.Count < required)
                return BadRequest(new { success = false, error = $"数据不足 ({kline.Count}条)，需要至少 {required} 条" });

            var results = OptimizationEngine.WalkForward(config, kline);
            return Ok(new
            {
                success = true,
                data = results,
                meta = new
                {
                    totalWindows = results.Count,
                    overfitWindows = results.Count(x => x.Overfit),
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

    private Task<List<AnalysisKline>> LoadAsync(string code, string? start, string? end, string? period, CancellationToken ct)
        => AnalysisDataService.IsIndexCode(code)
            ? data.GetIndexKlineAsync(code, start, end, period, ct: ct)
            : data.GetKlineAsync(code, start, end, period, ct);
}