using System.Text.Json;
using FundAnalysis.Data;
using FundAnalysis.Models;
using Microsoft.EntityFrameworkCore;

namespace FundAnalysis.Analysis;

public sealed class BacktestService(AppDbContext db, AnalysisDataService data)
{
    public async Task<(string Id, BacktestResultDto Result)> RunAsync(BacktestRequest config, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(config.Code) || string.IsNullOrWhiteSpace(config.StartDate) ||
            string.IsNullOrWhiteSpace(config.EndDate))
            throw new AnalysisRequestException("缺少必要参数: code, startDate, endDate, entryRules");

        var kline = AnalysisDataService.IsIndexCode(config.Code)
            ? await data.GetIndexKlineAsync(config.Code, config.StartDate, config.EndDate, config.Period, ct: ct)
            : await data.GetKlineAsync(config.Code, config.StartDate, config.EndDate, config.Period, ct);

        if (kline.Count < 60)
            throw new AnalysisRequestException($"数据不足 ({kline.Count}条)，至少需要60个交易日");

        var result = BacktestEngine.Run(config, kline);
        var id = $"bt_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{config.Code}";
        var name = $"{config.Name ?? config.Code} {config.StartDate}~{config.EndDate}";

        var record = new BacktestRecord
        {
            Id = id,
            Name = name,
            ConfigJson = JsonSerializer.Serialize(config, AnalysisJson.Options),
            MetricsJson = JsonSerializer.Serialize(result.Metrics, AnalysisJson.Options),
            EquityCurveJson = JsonSerializer.Serialize(result.EquityCurve, AnalysisJson.Options),
            CreatedAt = DateTime.Now,
            Trades = result.Trades.Select(t => new BacktestTradeRecord
            {
                BacktestId = id,
                Code = t.Code,
                EntryDate = AnalysisDataService.ParseKlineDate(t.EntryDate),
                EntryPrice = t.EntryPrice,
                ExitDate = AnalysisDataService.ParseKlineDate(t.ExitDate),
                ExitPrice = t.ExitPrice,
                Shares = t.Shares,
                Pnl = t.Pnl,
                PnlPct = t.PnlPct,
                ExitReason = t.ExitReason,
            }).ToList(),
        };
        db.BacktestRecords.Add(record);
        await db.SaveChangesAsync(ct);
        return (id, result);
    }

    public async Task<List<object>> ListAsync(CancellationToken ct = default)
    {
        var rows = await db.BacktestRecords.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new { x.Id, x.Name, x.CreatedAt })
            .ToListAsync(ct);
        return rows.Select(x => (object)new { id = x.Id, name = x.Name, created_at = x.CreatedAt }).ToList();
    }

    public async Task<object?> GetAsync(string id, CancellationToken ct = default)
    {
        var record = await db.BacktestRecords.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (record is null) return null;

        var trades = await db.BacktestTradeRecords.AsNoTracking()
            .Where(x => x.BacktestId == id)
            .OrderBy(x => x.EntryDate)
            .Select(x => new
            {
                code = x.Code,
                entryDate = x.EntryDate.ToString("yyyyMMdd"),
                entryPrice = x.EntryPrice,
                exitDate = x.ExitDate.ToString("yyyyMMdd"),
                exitPrice = x.ExitPrice,
                shares = x.Shares,
                pnl = x.Pnl,
                pnlPct = x.PnlPct,
                exitReason = x.ExitReason,
            })
            .ToListAsync(ct);

        return new
        {
            id = record.Id,
            name = record.Name,
            config = ParseJson(record.ConfigJson),
            metrics = ParseJson(record.MetricsJson),
            equityCurve = ParseJson(record.EquityCurveJson),
            trades,
        };
    }

    private static JsonElement ParseJson(string json)
    {
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch { return JsonSerializer.Deserialize<JsonElement>("{}"); }
    }
}