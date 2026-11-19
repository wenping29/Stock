using FundAnalysis.Services;

namespace FundAnalysis.Tests;

/// <summary>
/// 市场情绪纯计算逻辑测试：情绪温度分权重归一化、涨跌停幅度识别、涨跌幅分布分桶。
/// 采集与查询依赖第三方接口/数据库，不在此处覆盖。
/// </summary>
public class MarketSentimentTests
{
    private static MarketSentimentService.StockStatRow Row(
        string symbol, double changePct, string name = "测试股份")
        => new(symbol, name, "测试行业", changePct, DateTime.Today);

    [Fact]
    public void ComputeScore_AllComponentsPresent_WeightsSumTo100()
    {
        // 上涨占比 60% × 35 + 涨停占比 50/60 × 25 + 连板 4/6 × 15
        // + 封板率 50/60 × 10 + 量能 1.25 倍 × 15 = 71.4
        var score = MarketSentimentService.ComputeScore(
            up: 3000, down: 2000,
            limitUp: 50, limitDown: 10, broken: 10, maxStreak: 4,
            amount: 10000, avg20Amount: 8000);

        Assert.Equal(71.4, score);
    }

    [Theory]
    // 只有涨跌家数：25% 上涨 → 归一化后仍是 25 分
    [InlineData(1000, 3000, 0, 0, null, 0, 0.0, null, 25.0)]
    // 只有涨停跌停：20/100 → 20 分
    [InlineData(0, 0, 20, 80, null, 0, 0.0, null, 20.0)]
    // 只有连板：6 板及以上按满分 100 计
    [InlineData(0, 0, 0, 0, null, 6, 0.0, null, 100.0)]
    // 只有量能：放量至均值 1.5 倍封顶 → 100 分
    [InlineData(0, 0, 0, 0, null, 0, 100000, 1000.0, 100.0)]
    // 全部缺失 → 0 分，不抛异常
    [InlineData(0, 0, 0, 0, null, 0, 0.0, null, 0.0)]
    public void ComputeScore_MissingComponents_AreRenormalized(
        int up, int down, int limitUp, int limitDown, int? broken, int maxStreak,
        double amount, double? avg20, double expected)
    {
        var score = MarketSentimentService.ComputeScore(
            up, down, limitUp, limitDown, broken, maxStreak, amount, avg20);

        Assert.Equal(expected, score);
    }

    [Fact]
    public void ComputeScore_SealRate_OnlyCountsWhenBrokenBoardAvailable()
    {
        // 涨停 50、炸板 10：封板率 83.3%，与涨停跌停对比一起归一化
        Assert.Equal(83.3, MarketSentimentService.ComputeScore(
            0, 0, 50, 10, broken: 10, maxStreak: 0, amount: 0, avg20Amount: null));

        // 报爆炸板数据缺失（null）时剔除封板率分项，避免被当成 100% 封板
        Assert.Equal(20.0, MarketSentimentService.ComputeScore(
            0, 0, 20, 80, broken: null, maxStreak: 0, amount: 0, avg20Amount: null));

        // 涨停池有数据且炸板为 0 时，封板 100% 应计入
        Assert.Equal(42.9, MarketSentimentService.ComputeScore(
            0, 0, 20, 80, broken: 0, maxStreak: 0, amount: 0, avg20Amount: null));
    }

    [Fact]
    public void ComputeScore_VolumeRatio_IsClampedToHalfAndOneAndHalf()
    {
        // 缩量到均值 40%（低于下限 0.5 按 0.5 计）→ 量能分 0
        Assert.Equal(0, MarketSentimentService.ComputeScore(
            0, 0, 0, 0, 0, 0, amount: 400, avg20Amount: 1000));
        // 放量到均值 2 倍（高于上限 1.5 按 1.5 计）→ 量能分 100
        Assert.Equal(100, MarketSentimentService.ComputeScore(
            0, 0, 0, 0, 0, 0, amount: 2000, avg20Amount: 1000));
    }

    [Theory]
    [InlineData("600519", 10.0, "贵州茅台", true)]   // 主板 10%
    [InlineData("600519", 9.85, "贵州茅台", false)]
    [InlineData("300750", 20.0, "宁德时代", true)]   // 创业板 20%
    [InlineData("300750", 19.5, "宁德时代", false)]
    [InlineData("688981", 19.95, "中芯国际", true)]  // 科创板 20%
    [InlineData("830799", 29.95, "艾融软件", true)]  // 北交所 30%
    [InlineData("830799", 29.0, "艾融软件", false)]
    [InlineData("000002", 5.0, "ST万科", true)]      // ST 5%
    [InlineData("000002", 4.8, "ST万科", false)]
    [InlineData("000002", 20.0, "万科A", true)]      // 非 ST 按主板 10% 判定
    public void IsLimitUp_UsesBoardSpecificThreshold(string symbol, double changePct, string name, bool expected)
    {
        Assert.Equal(expected, MarketSentimentService.IsLimitUp(Row(symbol, changePct, name)));
    }

    [Theory]
    [InlineData("600519", -10.0, "贵州茅台", true)]
    [InlineData("600519", -9.8, "贵州茅台", false)]
    [InlineData("300750", -19.95, "宁德时代", true)]
    [InlineData("000002", -5.0, "ST万科", true)]
    public void IsLimitDown_IsSymmetric(string symbol, double changePct, string name, bool expected)
    {
        Assert.Equal(expected, MarketSentimentService.IsLimitDown(Row(symbol, changePct, name)));
    }

    [Fact]
    public void BuildDistribution_BucketsByChangePctBoundaries()
    {
        var stocks = new List<MarketSentimentService.StockStatRow>
        {
            Row("000001", -8),   // ≤ -7%
            Row("000002", -6),   // -7% ~ -5%
            Row("000003", -4),   // -5% ~ -3%
            Row("000004", -2),   // -3% ~ -1%
            Row("000005", -0.5), // -1% ~ 0
            Row("000006", 0),    // 平盘
            Row("000007", 0.5),  // 0 ~ 1%
            Row("000008", 2),    // 1% ~ 3%
            Row("000009", 4),    // 3% ~ 5%
            Row("000010", 6),    // 5% ~ 7%
            Row("000011", 8),    // ≥ 7%
            Row("000012", 7),    // ≥ 7%（边界值归入最高档）
        };

        var buckets = MarketSentimentService.BuildDistribution(stocks);

        Assert.Equal(11, buckets.Count);
        Assert.All(buckets.Take(10), b => Assert.Equal(1, b.Count));
        Assert.Equal(2, buckets[^1].Count);
        Assert.Equal("≥ 7%", buckets[^1].Label);
    }
}
