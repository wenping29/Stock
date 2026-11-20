using FundAnalysis.Analysis;
using Microsoft.AspNetCore.Mvc;

namespace FundAnalysis.Controllers;

/// <summary>量化分析：选股预设、全市场筛选与单票试算（对齐 /api/screening）。</summary>
[ApiController]
[Route("api/screening")]
public sealed class AnalysisScreeningController(ScreeningService screening) : ControllerBase
{
    public sealed class RunRequest
    {
        public string? StockPool { get; set; }
        public RuleNode? Rules { get; set; }
        public string? StartDate { get; set; }
        public string? EndDate { get; set; }
        public string? Period { get; set; } = "daily";
    }

    public sealed class TestRequest
    {
        public string? Code { get; set; }
        public RuleNode? Rules { get; set; }
        public string? StartDate { get; set; }
        public string? EndDate { get; set; }
        public string? Period { get; set; } = "daily";
    }

    [HttpGet("presets")]
    public IActionResult Presets()
        => Ok(new { success = true, data = ScreeningPresets.List() });

    [HttpGet("presets/{key}")]
    public IActionResult Preset(string key)
    {
        var preset = ScreeningPresets.Get(key);
        return preset is null
            ? NotFound(new { success = false, error = "Preset not found" })
            : Ok(new { success = true, data = preset.Value });
    }

    [HttpPost("run")]
    public async Task<IActionResult> Run([FromBody] RunRequest? body, CancellationToken ct)
    {
        if (body is null)
            return BadRequest(new { success = false, error = "缺少请求体" });
        try
        {
            var (results, total) = await screening.RunAsync(
                body.StockPool, body.Rules, body.StartDate, body.EndDate, body.Period, ct);
            return Ok(new
            {
                success = true,
                data = results,
                meta = new { total, matched = results.Count },
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

    [HttpPost("test")]
    public async Task<IActionResult> Test([FromBody] TestRequest? body, CancellationToken ct)
    {
        if (body is null)
            return BadRequest(new { success = false, error = "缺少请求体" });
        try
        {
            return Ok(new
            {
                success = true,
                data = await screening.TestAsync(
                    body.Code, body.Rules, body.StartDate, body.EndDate, body.Period, ct),
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
}