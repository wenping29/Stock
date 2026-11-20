namespace DataMigrator.Realtime;

/// <summary>全市场实时行情（字段与 patch /api/quote/* 响应保持一致）。</summary>
public sealed record RealtimeQuote(
    string Code,
    string Name,
    string Market,
    double Price,
    double ChangePct,
    double Volume,
    double Amount,
    double High,
    double Low);

public sealed record RealtimeStatus(
    int Count,
    long? LastUpdated,
    long? LastDurationMs,
    bool Refreshing);
