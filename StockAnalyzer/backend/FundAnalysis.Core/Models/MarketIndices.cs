namespace FundAnalysis.Models;

/// <summary>指数日线（A 股宽基 / 风格 / 跨境）。Code + Market 唯一。</summary>
public class IndexDaily
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    /// <summary>东财 secid 市场前缀（1=沪，0=深，100=全球）。</summary>
    public int Market { get; set; }
    public DateTime Date { get; set; }
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public double Volume { get; set; }
    public double Amount { get; set; }
    public double ChangePct { get; set; }
}

/// <summary>系统内置指数清单（指数页展示 + 采集范围）。</summary>
public static class MarketIndices
{
    public static readonly (string Code, string Name, string Group, int Market)[] List =
    [
        ("000001", "上证指数", "A股宽基", 1),
        ("399001", "深证成指", "A股宽基", 0),
        ("000300", "沪深300", "A股宽基", 1),
        ("000905", "中证500", "A股宽基", 1),
        ("000852", "中证1000", "A股宽基", 1),
        ("000016", "上证50", "A股宽基", 1),
        ("399330", "深证100", "A股宽基", 0),
        ("399006", "创业板指", "A股宽基", 0),
        ("000688", "科创50", "A股宽基", 1),
        ("899050", "北证50", "A股宽基", 0),
        ("000922", "中证红利", "风格与主题", 1),
        ("000933", "医药", "行业指数", 1),
        ("399997", "白酒", "行业指数", 0),
        ("980017", "半导体", "行业指数", 0),
        ("399986", "银行", "行业指数", 0),
        ("399393", "地产", "行业指数", 0),
        ("HSI", "恒生指数", "跨境", 100),
        ("NDX", "纳斯达克", "跨境", 100),
        ("DJIA", "道琼斯", "跨境", 100),
        ("SPX", "标普500", "跨境", 100),
        // 辅助指标：不展示在指数页，用于两市成交量统计（深市全量成交额）
        ("399106", "深证综指", "辅助指标", 0),
    ];

    /// <summary>大盘行情页展示的指数子集（核心宽基 7 + 行业 5），顺序即展示顺序。</summary>
    public static readonly string[] MarketOverviewCodes =
    [
        "000001", "399001", "399006", "000300", "000016", "000905", "000688",
        "000933", "399997", "980017", "399986", "399393",
    ];

    /// <summary>行业指数 → 东财行业板块名（与 SectorMoneyFlow.Name 对齐，用于匹配主力净流入）。</summary>
    public static readonly Dictionary<string, string> SectorNames = new()
    {
        ["000933"] = "医药生物",
        ["399997"] = "白酒Ⅱ",
        ["980017"] = "半导体",
        ["399986"] = "银行",
        ["399393"] = "房地产",
    };
}
