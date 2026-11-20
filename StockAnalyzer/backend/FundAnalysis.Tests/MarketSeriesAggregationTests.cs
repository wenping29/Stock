using FundAnalysis.Analysis;
using FundAnalysis.Data;
using FundAnalysis.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FundAnalysis.Tests;

/// <summary>
/// 行情序列读取：非日线周期按日线即时聚合（历史导入的 weekly/monthly 等聚合行不再参与）。
/// </summary>
public class MarketSeriesAggregationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;

    public MarketSeriesAggregationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task 非日线周期按日线聚合_忽略历史聚合行()
    {
        // 2026-11-02 ~ 11-06 为一周，收盘价 1..5
        for (var i = 0; i < 5; i++)
        {
            _db.MarketSeriesPoints.Add(new MarketSeriesPoint
            {
                Key = "gold_price",
                Period = "daily",
                Date = new DateTime(2026, 11, 2).AddDays(i),
                Open = i + 1,
                High = i + 2,
                Low = i,
                Close = i + 1,
            });
        }
        // 历史聚合行（旧导入数据），不应被读取
        _db.MarketSeriesPoints.Add(new MarketSeriesPoint
        {
            Key = "gold_price",
            Period = "weekly",
            Date = new DateTime(2026, 10, 30),
            Open = 888,
            High = 999,
            Low = 777,
            Close = 999,
        });
        await _db.SaveChangesAsync();

        var service = new AnalysisDataService(_db, null!, NullLogger<AnalysisDataService>.Instance);

        var daily = await service.GetMarketSeriesAsync("gold_price", "daily", null, null);
        Assert.Equal(5, daily.Count);
        Assert.DoesNotContain(daily, x => x.Close == 999);

        var weekly = await service.GetMarketSeriesAsync("gold_price", "weekly", null, null);
        var bar = Assert.Single(weekly);
        Assert.Equal(1, bar.Open);
        Assert.Equal(6, bar.High);
        Assert.Equal(0, bar.Low);
        Assert.Equal(5, bar.Close);

        // 跨月：12-01 落在新月份，月度聚合应产生第二根
        _db.MarketSeriesPoints.Add(new MarketSeriesPoint
        {
            Key = "gold_price",
            Period = "daily",
            Date = new DateTime(2026, 12, 1),
            Open = 10,
            High = 11,
            Low = 9,
            Close = 10,
        });
        await _db.SaveChangesAsync();

        var monthly = await service.GetMarketSeriesAsync("gold_price", "monthly", null, null);
        Assert.Equal(2, monthly.Count);
        Assert.Equal(5, monthly[0].Close);
        Assert.Equal(10, monthly[1].Close);
    }
}