using System.Security.Claims;
using FundAnalysis.Analysis;
using FundAnalysis.Data;
using FundAnalysis.Models;
using FundAnalysis.Services;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FundAnalysis.Controllers;

/// <summary>自定义指数：用户按自选股票与权重合成指数，指数页展示卡片与 K 线。</summary>
[ApiController]
[Route("api/custom-index")]
[Authorize]
public class CustomIndexController(
    AppDbContext db, CustomIndexService service, IBackgroundJobClient backgroundJobs) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>当前用户的自定义指数清单（含最新点位与近 30 日走势）。</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await service.GetSnapshotsAsync(UserId, ct));

    /// <summary>可快速导入的预设指数（上证50 / 沪深300 / 中证500 等官方成分股）。</summary>
    [HttpGet("presets")]
    public IActionResult Presets() => Ok(CustomIndexService.GetPresets());

    /// <summary>预设指数成分股与权重。</summary>
    [HttpGet("presets/{key}")]
    public async Task<IActionResult> Preset(string key, CancellationToken ct)
    {
        try
        {
            return Ok(await service.GetPresetAsync(key, ct));
        }
        catch (AnalysisRequestException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    /// <summary>自定义指数合成 K 线（日/周/月/季/年）。</summary>
    [HttpGet("{id:long}/kline")]
    public async Task<IActionResult> Kline(
        long id, [FromQuery] string? start, [FromQuery] string? end,
        [FromQuery] string period = "daily", [FromQuery] int? limit = null,
        CancellationToken ct = default)
    {
        var index = await service.FindAsync(UserId, id, ct);
        if (index is null) return NotFound(new { message = "自定义指数不存在" });

        try
        {
            return Ok(await service.GetKlineAsync(index, start, end, period, limit, ct));
        }
        catch (AnalysisRequestException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CustomIndexSaveRequest request, CancellationToken ct)
    {
        var error = CustomIndexService.NormalizeRequest(request, out var name, out var baseValue, out var components);
        if (error is not null) return BadRequest(new { message = error });

        var index = new CustomIndex
        {
            UserId = UserId,
            Name = name,
            BaseValue = baseValue,
            Components = components,
        };
        db.CustomIndices.Add(index);
        await db.SaveChangesAsync(ct);

        await EnsureBasicsAndSyncAsync(components);
        return Ok(new { id = index.Id });
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(
        long id, [FromBody] CustomIndexSaveRequest request, CancellationToken ct)
    {
        var index = await service.FindAsync(UserId, id, ct);
        if (index is null) return NotFound(new { message = "自定义指数不存在" });

        var error = CustomIndexService.NormalizeRequest(request, out var name, out var baseValue, out var components);
        if (error is not null) return BadRequest(new { message = error });

        db.CustomIndexComponents.RemoveRange(index.Components);
        index.Components = components;
        index.Name = name;
        index.BaseValue = baseValue;
        await db.SaveChangesAsync(ct);

        await EnsureBasicsAndSyncAsync(components);
        return Ok(new { id = index.Id });
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var index = await service.FindAsync(UserId, id, ct);
        if (index is null) return NotFound(new { message = "自定义指数不存在" });

        db.CustomIndices.Remove(index);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "已删除" });
    }

    /// <summary>
    /// 成分股未在 StockBasic 注册时自动补注册（名称优先取基本面表），
    /// 并入队一次即时采集，使日线与自选一样由定时任务持续更新。
    /// </summary>
    private async Task EnsureBasicsAndSyncAsync(List<CustomIndexComponent> components)
    {
        var items = components
            .GroupBy(c => c.Symbol)
            .Select(g => (Symbol: g.Key, Name: g.First().Name))
            .ToList();

        var symbols = items.Select(x => x.Symbol).ToList();

        // 成分股可能多达上千只，按批查询避免 SQL 参数超限（SQLite 上限 999）
        var existing = new HashSet<string>();
        var fundamentals = new Dictionary<string, StockFundamental>();
        foreach (var chunk in symbols.Chunk(500))
        {
            existing.UnionWith(await db.StockBasics
                .Where(x => chunk.Contains(x.Symbol))
                .Select(x => x.Symbol)
                .ToListAsync());
            foreach (var kv in await db.StockFundamentals
                .Where(x => chunk.Contains(x.Symbol))
                .ToDictionaryAsync(x => x.Symbol))
                fundamentals[kv.Key] = kv.Value;
        }

        var missing = items.Where(x => !existing.Contains(x.Symbol)).ToList();

        if (missing.Count > 0)
        {
            foreach (var (symbol, name) in missing)
            {
                fundamentals.TryGetValue(symbol, out var fund);
                db.StockBasics.Add(new StockBasic
                {
                    Symbol = symbol,
                    Name = string.IsNullOrWhiteSpace(fund?.Name)
                        ? (string.IsNullOrWhiteSpace(name) ? symbol : name)
                        : fund!.Name,
                    SecMarket = symbol.StartsWith('6') || symbol.StartsWith('9') ? 1 : 0,
                    Type = SymbolTypes.Stock,
                });
            }
            await db.SaveChangesAsync();
        }

        foreach (var symbol in symbols.Distinct())
            backgroundJobs.Enqueue<CollectorService>(s => s.SyncSymbolAsync(symbol));
    }
}
