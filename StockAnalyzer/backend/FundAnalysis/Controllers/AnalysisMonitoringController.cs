using FundAnalysis.Analysis;
using Microsoft.AspNetCore.Mvc;

namespace FundAnalysis.Controllers;

/// <summary>量化分析：监控任务与 webhook 测试（对齐 /api/monitoring）。</summary>
[ApiController]
[Route("api/analysis/monitoring")]
public sealed class AnalysisMonitoringController(MonitoringService monitoring) : ControllerBase
{
    public sealed class TestWebhookRequest
    {
        public string? Url { get; set; }
        public string? Type { get; set; }
    }

    [HttpGet("jobs")]
    public async Task<IActionResult> Jobs(CancellationToken ct)
        => Ok(new { success = true, data = await monitoring.ListAsync(ct) });

    [HttpPost("jobs")]
    public async Task<IActionResult> Create([FromBody] MonitorJobDto? job, CancellationToken ct)
    {
        if (job is null || string.IsNullOrWhiteSpace(job.Name) || string.IsNullOrWhiteSpace(job.Cron))
            return BadRequest(new { success = false, error = "缺少必要参数: name, cron" });
        try
        {
            var id = await monitoring.SaveAsync(job, ct);
            return Ok(new { success = true, data = new { id } });
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

    [HttpDelete("jobs/{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await monitoring.DeleteAsync(id, ct);
        return Ok(new { success = true, data = (object?)null });
    }

    [HttpPost("jobs/{id}/run")]
    public async Task<IActionResult> Run(string id, CancellationToken ct)
    {
        try
        {
            await monitoring.TriggerAsync(id, ct);
            return Ok(new { success = true, data = new { message = "已触发执行" } });
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

    [HttpPost("test-webhook")]
    public async Task<IActionResult> TestWebhook([FromBody] TestWebhookRequest? body, CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Url))
            return BadRequest(new { success = false, error = "缺少 webhook URL" });
        try
        {
            var ok = await monitoring.TestWebhookAsync(body.Url, body.Type, ct);
            return ok
                ? Ok(new { success = true, data = new { message = "测试消息已发送" } })
                : StatusCode(500, new { success = false, error = "发送失败，请检查URL和权限" });
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