using System.Text;
using System.Text.Json;
using FundAnalysis.Data;
using FundAnalysis.Models;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace FundAnalysis.Analysis;

/// <summary>
/// 量化监控任务：CRUD + Hangfire 定时注册 + 扫描执行 + webhook 通知。
/// 任务配置以 Node 版 JSON 结构整体保存在 MonitoringJobs.ConfigJson。
/// </summary>
public sealed class MonitoringService(
    AppDbContext db,
    AnalysisDataService data,
    IRecurringJobManager recurring,
    IBackgroundJobClient background,
    IHttpClientFactory httpFactory,
    ILogger<MonitoringService> logger)
{
    public const string RecurringPrefix = "analysis-monitor:";

    public record EntrySignal(string Code, string Name, double Close, double ChangePct);

    public record ExitSignal(string Code, string Signal);

    public record WebhookMessage(
        string Title, string Timestamp, List<EntrySignal> EntrySignals, List<ExitSignal> ExitSignals);

    public async Task<List<object>> ListAsync(CancellationToken ct = default)
    {
        var jobs = await db.MonitoringJobs.AsNoTracking().OrderBy(x => x.CreatedAt).ToListAsync(ct);
        return jobs.Select(j => (object)new { id = j.Id, name = j.Name, cron = j.Cron, enabled = j.Enabled }).ToList();
    }

    public async Task<string> SaveAsync(MonitorJobDto config, CancellationToken ct = default)
    {
        var id = string.IsNullOrWhiteSpace(config.Id) ? $"monitor_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}" : config.Id;
        config.Id = id;

        var entity = await db.MonitoringJobs.FirstOrDefaultAsync(x => x.Id == id, ct);
        var json = JsonSerializer.Serialize(config, AnalysisJson.Options);
        if (entity is null)
        {
            db.MonitoringJobs.Add(new MonitoringJob
            {
                Id = id,
                Name = config.Name,
                Cron = config.Cron,
                ConfigJson = json,
                Enabled = config.Enabled,
                CreatedAt = DateTime.Now,
            });
        }
        else
        {
            entity.Name = config.Name;
            entity.Cron = config.Cron;
            entity.ConfigJson = json;
            entity.Enabled = config.Enabled;
        }
        await db.SaveChangesAsync(ct);

        if (config.Enabled) Register(config);
        else RemoveRecurring(id);
        return id;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        var entity = await db.MonitoringJobs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return false;
        db.MonitoringJobs.Remove(entity);
        await db.SaveChangesAsync(ct);
        RemoveRecurring(id);
        return true;
    }

    public async Task TriggerAsync(string id, CancellationToken ct = default)
    {
        var exists = await db.MonitoringJobs.AsNoTracking().AnyAsync(x => x.Id == id, ct);
        if (!exists) throw new AnalysisRequestException("监控任务不存在", 404);
        background.Enqueue<MonitoringService>(s => s.ExecuteAsync(id));
    }

    public void Register(MonitorJobDto config)
    {
        try
        {
            recurring.AddOrUpdate<MonitoringService>(
                RecurringPrefix + config.Id,
                s => s.ExecuteAsync(config.Id),
                config.Cron);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "监控任务注册失败 {JobId} ({Cron})", config.Id, config.Cron);
            throw new AnalysisRequestException($"Cron 表达式无效: {config.Cron}");
        }
    }

    public async Task RegisterAllAsync(CancellationToken ct = default)
    {
        var jobs = await db.MonitoringJobs.AsNoTracking().Where(x => x.Enabled).ToListAsync(ct);
        foreach (var job in jobs)
        {
            try
            {
                recurring.AddOrUpdate<MonitoringService>(
                    RecurringPrefix + job.Id,
                    s => s.ExecuteAsync(job.Id),
                    job.Cron);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "监控任务注册失败 {JobId}", job.Id);
            }
        }
        if (jobs.Count > 0) logger.LogInformation("已注册 {Count} 个量化监控任务", jobs.Count);
    }

    private void RemoveRecurring(string id) => recurring.RemoveIfExists(RecurringPrefix + id);

    // ---------- 执行 ----------

    public Task ExecuteAsync(string id) => ExecuteAsync(id, CancellationToken.None);

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task ExecuteAsync(string id, CancellationToken ct)
    {
        var entity = await db.MonitoringJobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return;

        var config = JsonSerializer.Deserialize<MonitorJobDto>(entity.ConfigJson, AnalysisJson.Options);
        if (config is null) return;

        logger.LogInformation("开始执行监控任务 {Name}", config.Name);

        var pool = await ResolvePoolAsync(config, ct);
        var scanPool = pool.Take(200).ToList();
        var start = new DateTime(2024, 1, 1);
        var end = DateTime.Today;
        var batch = await data.GetKlineBatchAsync(scanPool, start, end, "daily", allowFetch: false, ct);

        var entrySignals = new List<EntrySignal>();
        foreach (var code in scanPool)
        {
            if (!batch.TryGetValue(code, out var rows) || rows.Count < 60) continue;
            if (!RuleEvaluator.Evaluate(rows, config.EntryRules)) continue;
            var last = rows[^1];
            var prev = rows[^2];
            entrySignals.Add(new EntrySignal(
                code,
                await NameOfAsync(code, ct),
                last.Close,
                prev.Close == 0 ? 0 : Math.Round((last.Close / prev.Close - 1) * 100, 2)));
        }

        var exitSignals = new List<ExitSignal>();
        foreach (var code in config.Watchlist)
        {
            try
            {
                var rows = await data.GetKlineAsync(code, "2024-01-01", end.ToString("yyyy-MM-dd"), "daily", ct);
                if (rows.Count < 60) continue;
                var entryIndex = Math.Max(0, rows.Count - 60);
                var exit = BacktestEngine.EvaluateExit(rows, entryIndex, rows.Count - 1, rows[entryIndex].Close, config.ExitRules);
                if (exit.Exit) exitSignals.Add(new ExitSignal(code, exit.Reason));
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "监控卖出信号扫描失败 {Code}", code);
            }
        }

        db.MonitoringRuns.Add(new MonitoringRun
        {
            JobId = id,
            RunAt = DateTime.Now,
            EntrySignalsJson = JsonSerializer.Serialize(entrySignals, AnalysisJson.Options),
            ExitSignalsJson = JsonSerializer.Serialize(exitSignals, AnalysisJson.Options),
        });
        await db.SaveChangesAsync(ct);

        var message = new WebhookMessage(
            $"{config.Name} - 扫描结果",
            DateTime.Now.ToString("yyyy/M/d HH:mm:ss"),
            entrySignals,
            exitSignals);

        foreach (var webhook in config.Webhooks)
        {
            var ok = await SendWebhookAsync(webhook.Url, webhook.Type, message, ct);
            if (!ok) logger.LogWarning("监控 webhook 发送失败 {Url}", webhook.Url);
        }

        logger.LogInformation("监控任务 {Name} 完成：{Entries} 个买入信号，{Exits} 个卖出信号",
            config.Name, entrySignals.Count, exitSignals.Count);
    }

    private async Task<List<string>> ResolvePoolAsync(MonitorJobDto config, CancellationToken ct)
    {
        if (config.StockPool.Count > 0) return config.StockPool;
        return await db.StockBasics.AsNoTracking()
            .Where(x => x.Type == SymbolTypes.Stock && !x.Name.Contains("ST") && !x.Name.Contains("退"))
            .OrderBy(x => x.Symbol)
            .Select(x => x.Symbol)
            .ToListAsync(ct);
    }

    private async Task<string> NameOfAsync(string code, CancellationToken ct)
        => await db.StockBasics.AsNoTracking()
               .Where(x => x.Symbol == code)
               .Select(x => x.Name)
               .FirstOrDefaultAsync(ct) ?? code;

    // ---------- Webhook ----------

    public async Task<bool> TestWebhookAsync(string url, string? type, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) throw new AnalysisRequestException("缺少 webhook URL");
        var msg = new WebhookMessage(
            "测试消息",
            DateTime.Now.ToString("yyyy/M/d HH:mm:ss"),
            [new EntrySignal("000001", "测试股票", 10.50, 2.5)],
            []);
        return await SendWebhookAsync(url, type ?? "wecom", msg, ct);
    }

    private async Task<bool> SendWebhookAsync(string url, string type, WebhookMessage msg, CancellationToken ct)
    {
        try
        {
            var client = httpFactory.CreateClient("webhook");
            object body = type.ToLowerInvariant() switch
            {
                "wecom" => new { msgtype = "markdown", markdown = new { content = FormatWecomMarkdown(msg) } },
                "dingtalk" => new { msgtype = "markdown", markdown = new { title = msg.Title, text = FormatDingtalkMarkdown(msg) } },
                "generic" => msg,
                _ => throw new AnalysisRequestException($"不支持的 webhook 类型: {type}"),
            };

            var json = JsonSerializer.Serialize(body, AnalysisJson.Options);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await client.PostAsync(url, content, ct);
            if (!resp.IsSuccessStatusCode) return false;

            if (type.Equals("generic", StringComparison.OrdinalIgnoreCase)) return true;
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return doc.RootElement.TryGetProperty("errcode", out var code) && code.TryGetInt32(out var v) && v == 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "webhook 发送异常 ({Type})", type);
            return false;
        }
    }

    private static string FormatWecomMarkdown(WebhookMessage msg)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## {msg.Title}");
        sb.AppendLine($"> 时间: {msg.Timestamp}");
        sb.AppendLine();

        if (msg.EntrySignals.Count > 0)
        {
            sb.AppendLine("### 买入信号");
            sb.AppendLine("| 代码 | 名称 | 收盘价 | 涨跌幅 |");
            sb.AppendLine("|------|------|--------|--------|");
            foreach (var s in msg.EntrySignals)
            {
                var color = s.ChangePct >= 0 ? "info" : "warning";
                var sign = s.ChangePct >= 0 ? "+" : "";
                sb.AppendLine($"| {s.Code} | {s.Name} | {s.Close:F2} | <font color=\"{color}\">{sign}{s.ChangePct:F2}%</font> |");
            }
        }
        else
        {
            sb.AppendLine("### 买入信号");
            sb.AppendLine("暂无符合条件的股票");
        }

        if (msg.ExitSignals.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### 卖出信号");
            foreach (var s in msg.ExitSignals)
                sb.AppendLine($"- **{s.Code}**: {s.Signal}");
        }
        return sb.ToString();
    }

    private static string FormatDingtalkMarkdown(WebhookMessage msg)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## {msg.Title}");
        sb.AppendLine();
        sb.AppendLine($"时间: {msg.Timestamp}");
        sb.AppendLine();

        if (msg.EntrySignals.Count > 0)
        {
            sb.AppendLine("### 买入信号");
            sb.AppendLine();
            sb.AppendLine("| 代码 | 名称 | 收盘价 | 涨跌幅 |");
            sb.AppendLine("|------|------|--------|--------|");
            foreach (var s in msg.EntrySignals)
            {
                var sign = s.ChangePct >= 0 ? "+" : "";
                sb.AppendLine($"| {s.Code} | {s.Name} | {s.Close:F2} | {sign}{s.ChangePct:F2}% |");
            }
        }

        if (msg.ExitSignals.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### 卖出信号");
            sb.AppendLine();
            foreach (var s in msg.ExitSignals) sb.AppendLine($"- {s.Code}: {s.Signal}");
        }
        return sb.ToString();
    }
}