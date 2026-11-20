namespace DataMigrator.Realtime;

/// <summary>启动即抓取一次，之后按间隔（默认 60s，对齐 patch）刷新。</summary>
public sealed class RealtimeRefreshWorker(
    RealtimeQuoteCache cache,
    IConfiguration config,
    ILogger<RealtimeRefreshWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = config.GetValue("Realtime:RefreshSeconds", 60);
        if (seconds < 10) seconds = 10;
        logger.LogInformation("实时行情服务启动：每 {Seconds}s 刷新全市场", seconds);

        try
        {
            await cache.RefreshAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "实时行情首次刷新失败，等待下一轮");
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await cache.RefreshAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "实时行情刷新失败，保留上一份快照");
            }
        }
    }
}
