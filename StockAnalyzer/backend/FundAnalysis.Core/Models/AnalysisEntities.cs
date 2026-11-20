namespace FundAnalysis.Models;

/// <summary>通用行情序列：汇率 / 黄金 / 原油 / 中债收益率等非股票标的的 OHLC。</summary>
public class MarketSeriesPoint
{
    public long Id { get; set; }
    /// <summary>序列键：FX_CNY_USD / GOLD / CRUDE_OIL / CN10Y。</summary>
    public string Key { get; set; } = "";
    /// <summary>daily / weekly / monthly / quarterly / yearly。</summary>
    public string Period { get; set; } = "daily";
    public DateTime Date { get; set; }
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
}

/// <summary>回测结果（配置/指标/资金曲线以 JSON 存储，与量化分析服务契约一致）。</summary>
public class BacktestRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ConfigJson { get; set; } = "{}";
    public string MetricsJson { get; set; } = "{}";
    public string EquityCurveJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<BacktestTradeRecord> Trades { get; set; } = [];
}

public class BacktestTradeRecord
{
    public long Id { get; set; }
    public string BacktestId { get; set; } = "";
    public string Code { get; set; } = "";
    public DateTime EntryDate { get; set; }
    public double EntryPrice { get; set; }
    public DateTime ExitDate { get; set; }
    public double ExitPrice { get; set; }
    public double Shares { get; set; }
    public double Pnl { get; set; }
    public double PnlPct { get; set; }
    public string ExitReason { get; set; } = "";
}

/// <summary>监控任务；ConfigJson 保存完整 MonitorJob（股票池/规则/自选/Webhook）。</summary>
public class MonitoringJob
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Cron { get; set; } = "";
    public string ConfigJson { get; set; } = "{}";
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class MonitoringRun
{
    public long Id { get; set; }
    public string JobId { get; set; } = "";
    public DateTime RunAt { get; set; } = DateTime.Now;
    public string EntrySignalsJson { get; set; } = "[]";
    public string ExitSignalsJson { get; set; } = "[]";
}