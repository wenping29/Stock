using FundAnalysis.Analysis;
using FundAnalysis.Data;
using Microsoft.EntityFrameworkCore;
using NCrontab;

namespace DataMigrator.Monitor;

/// <summary>
/// --monitor：按 MonitoringJobs.Cron（5 或 6 段）调度监控任务，执行逻辑复用 Core 的 MonitoringService。
/// 每 20s 同步一次数据库中的任务定义，cron 变更无需重启。
/// </summary>
public sealed class MonitorCronWorker(
    IServiceProvider services,
    ILogger<MonitorCronWorker> logger) : BackgroundService
{
    private sealed record JobState(string Cron, CrontabSchedule? Schedule, DateTime Next);

    private readonly object _lock = new();
    private readonly Dictionary<string, JobState> _jobs = new(StringComparer.Ordinal);
    private readonly HashSet<string> _running = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("监控 cron 已启用（复用 MonitoringService，按数据库任务定义调度）");
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "监控 cron 调度检查失败");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task<List<object>> GetJobsAsync(CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.MonitoringJobs.AsNoTracking()
            .OrderBy(x => x.CreatedAt)
            .Select(x => new { x.Id, x.Name, x.Cron, x.Enabled })
            .ToListAsync(ct);
        return rows.Select(x => (object)new { id = x.Id, name = x.Name, cron = x.Cron, enabled = x.Enabled }).ToList();
    }

    private async Task TickAsync(CancellationToken ct)
    {
        List<(string Id, string Name, string Cron)> enabled;
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            enabled = (await db.MonitoringJobs.AsNoTracking()
                    .Where(x => x.Enabled)
                    .Select(x => new { x.Id, x.Name, x.Cron })
                    .ToListAsync(ct))
                .Select(x => (x.Id, x.Name, x.Cron))
                .ToList();
        }

        var now = DateTime.Now;
        var due = new List<string>();
        lock (_lock)
        {
            var ids = enabled.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var id in _jobs.Keys.Where(id => !ids.Contains(id)).ToList())
                _jobs.Remove(id);

            foreach (var (id, name, cron) in enabled)
            {
                if (_jobs.TryGetValue(id, out var state) && state.Cron == cron) continue;

                var schedule = TryParse(cron, id);
                _jobs[id] = new JobState(cron, schedule, schedule?.GetNextOccurrence(now) ?? DateTime.MaxValue);
            }

            foreach (var (id, state) in _jobs)
            {
                if (state.Schedule is null || state.Next > now) continue;
                _jobs[id] = state with { Next = state.Schedule.GetNextOccurrence(now) };
                if (_running.Add(id)) due.Add(id);
            }
        }

        foreach (var id in due)
            _ = RunAsync(id);
    }

    private async Task RunAsync(string id)
    {
        try
        {
            using var scope = services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<MonitoringService>()
                .ExecuteAsync(id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "监控任务执行失败 {JobId}", id);
        }
        finally
        {
            lock (_lock) _running.Remove(id);
        }
    }

    private CrontabSchedule? TryParse(string cron, string id)
    {
        try
        {
            return CrontabSchedule.Parse(cron);
        }
        catch (Exception)
        {
            try
            {
                return CrontabSchedule.Parse(cron, new CrontabSchedule.ParseOptions { IncludingSeconds = true });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "监控任务 {JobId} 的 cron 无效：{Cron}", id, cron);
                return null;
            }
        }
    }
}
