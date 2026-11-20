using System.Diagnostics;

namespace DataMigrator.Realtime;

/// <summary>全市场实时行情内存缓存，单次刷新失败保留上一份快照。</summary>
public sealed class RealtimeQuoteCache(EastmoneyRealtimeSource source, ILogger<RealtimeQuoteCache> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile Dictionary<string, RealtimeQuote> _quotes = new();
    private volatile RealtimeStatus _status = new(0, null, null, false);

    public RealtimeStatus Status => _status;

    public IReadOnlyCollection<RealtimeQuote> GetAll() => _quotes.Values;

    public RealtimeQuote? Get(string code) => _quotes.GetValueOrDefault(code);

    public async Task<int> RefreshAsync(CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct)) return _quotes.Count;

        _status = _status with { Refreshing = true };
        var started = Stopwatch.GetTimestamp();
        try
        {
            var list = await source.FetchAllAsync(ct);
            var map = new Dictionary<string, RealtimeQuote>(list.Count, StringComparer.Ordinal);
            foreach (var q in list) map[q.Code] = q;
            _quotes = map;

            var elapsed = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            _status = new RealtimeStatus(map.Count, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), elapsed, false);
            logger.LogInformation("实时行情刷新完成：{Count} 只，用时 {Elapsed}ms", map.Count, elapsed);
            return map.Count;
        }
        finally
        {
            _status = _status with { Refreshing = false };
            _gate.Release();
        }
    }
}
