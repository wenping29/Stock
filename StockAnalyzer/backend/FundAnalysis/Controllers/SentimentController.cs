using FundAnalysis.Services;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FundAnalysis.Controllers;

/// <summary>市场情绪：涨跌家数、涨停跌停、连板高度、涨跌分布与行业冷暖。登录即可访问。</summary>
[ApiController]
[Route("api/sentiment")]
[Authorize]
public class SentimentController(MarketSentimentService sentiment, IBackgroundJobClient backgroundJobs) : ControllerBase
{
    /// <summary>最新交易日情绪快照（含情绪温度分、等级与较上日变化）。</summary>
    [HttpGet("overview")]
    public async Task<IActionResult> Overview()
        => Ok(await sentiment.GetOverviewAsync());

    /// <summary>情绪历史趋势（默认近 60 天）。</summary>
    [HttpGet("history")]
    public async Task<IActionResult> History([FromQuery] int days = 60)
        => Ok(await sentiment.GetHistoryAsync(days));

    /// <summary>全市场行业冷暖与涨跌分布（基于最近一次基本面快照）。</summary>
    [HttpGet("structure")]
    public async Task<IActionResult> Structure()
        => Ok(await sentiment.GetStructureAsync());

    /// <summary>手动触发情绪数据采集（盘中可随时刷新当日快照）。</summary>
    [HttpPost("sync")]
    public IActionResult Sync()
    {
        backgroundJobs.Enqueue<MarketSentimentService>(s => s.SyncAsync());
        return Ok(new { message = "已提交：同步市场情绪数据" });
    }
}
