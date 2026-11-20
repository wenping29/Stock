using System.Text.Json;
using System.Text.Json.Serialization;

namespace FundAnalysis.Analysis;

/// <summary>与量化分析服务一致的 K 线结构；Date 为 YYYYMMDD 或 YYYY-MM-DD。</summary>
public record AnalysisKline(
    string Date, double Open, double High, double Low, double Close, double Volume, double Amount)
{
    public double ChangePct { get; init; }
}

public sealed class RuleCondition
{
    public string Indicator { get; set; } = "";
    public string Operator { get; set; } = ">";
    public double Value { get; set; } = double.NaN;
    public Dictionary<string, double> Params { get; set; } = [];
}

public sealed class RuleGroup
{
    public string Logic { get; set; } = "AND";
    public List<object> Conditions { get; set; } = [];
}

/// <summary>
/// 规则节点：既可能是条件，也可能是嵌套的 AND/OR 组。
/// 前端契约没有类型判别字段，这里用自定义转换器按 "logic/conditions" 是否存在来识别。
/// </summary>
[JsonConverter(typeof(RuleNodeJsonConverter))]
public sealed class RuleNode
{
    public string? Logic { get; set; }
    public List<RuleNode>? Conditions { get; set; }
    public string? Indicator { get; set; }
    public string? Operator { get; set; }
    public double Value { get; set; } = double.NaN;
    public Dictionary<string, double> Params { get; set; } = [];

    public bool IsGroup => Logic is not null;

    public static RuleNode Condition(string indicator, string op, double value, Dictionary<string, double>? p = null)
        => new() { Indicator = indicator, Operator = op, Value = value, Params = p ?? [] };

    public static RuleNode Group(string logic, params RuleNode[] nodes)
        => new() { Logic = logic, Conditions = [.. nodes] };
}

public sealed class RuleNodeJsonConverter : JsonConverter<RuleNode>
{
    public override RuleNode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        return Parse(doc.RootElement);
    }

    public static RuleNode Parse(JsonElement e)
    {
        var node = new RuleNode();
        if (e.ValueKind != JsonValueKind.Object) return node;

        if (e.TryGetProperty("logic", out var logic) && logic.ValueKind == JsonValueKind.String)
        {
            node.Logic = logic.GetString();
            node.Conditions = [];
            if (e.TryGetProperty("conditions", out var conds) && conds.ValueKind == JsonValueKind.Array)
                foreach (var c in conds.EnumerateArray()) node.Conditions.Add(Parse(c));
            return node;
        }

        if (e.TryGetProperty("indicator", out var ind) && ind.ValueKind == JsonValueKind.String)
            node.Indicator = ind.GetString();
        if (e.TryGetProperty("operator", out var op) && op.ValueKind == JsonValueKind.String)
            node.Operator = op.GetString();
        if (e.TryGetProperty("value", out var val))
        {
            // 预设中存在 value:"EMA_20" 这种历史写法；Node 侧会隐式转成 NaN，这里保持一致。
            if (val.ValueKind == JsonValueKind.Number && val.TryGetDouble(out var d)) node.Value = d;
            else if (val.ValueKind == JsonValueKind.String && double.TryParse(val.GetString(), out var sd)) node.Value = sd;
        }
        if (e.TryGetProperty("params", out var ps) && ps.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in ps.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDouble(out var pv))
                    node.Params[p.Name] = pv;
        }
        return node;
    }

    public override void Write(Utf8JsonWriter writer, RuleNode value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.IsGroup)
        {
            writer.WriteString("logic", value.Logic);
            writer.WritePropertyName("conditions");
            writer.WriteStartArray();
            foreach (var c in value.Conditions ?? []) JsonSerializer.Serialize(writer, c, options);
            writer.WriteEndArray();
        }
        else
        {
            writer.WriteString("indicator", value.Indicator);
            writer.WriteString("operator", value.Operator);
            if (double.IsNaN(value.Value) || double.IsInfinity(value.Value)) writer.WriteNull("value");
            else writer.WriteNumber("value", value.Value);
            writer.WritePropertyName("params");
            JsonSerializer.Serialize(writer, value.Params, options);
        }
        writer.WriteEndObject();
    }
}

public sealed class ExitRulesDto
{
    public double? StopLossPct { get; set; }
    public double? StopLossAtrMultiplier { get; set; }
    public double? TakeProfitPct { get; set; }
    public RuleNode? ExitIndicator { get; set; }
}

public class BacktestBaseDto
{
    public double InitialCapital { get; set; } = 100000;
    public RuleNode EntryRules { get; set; } = RuleNode.Group("AND");
    public ExitRulesDto ExitRules { get; set; } = new();
    public string PositionMethod { get; set; } = "fixed";
    public double PositionSizing { get; set; } = 0.1;
    public double Commission { get; set; } = 0.0003;
    public double Slippage { get; set; } = 0.001;
}

public sealed class BacktestRequest : BacktestBaseDto
{
    public string Code { get; set; } = "";
    public string? Name { get; set; }
    public string StartDate { get; set; } = "";
    public string EndDate { get; set; } = "";
    public string? Period { get; set; } = "daily";
}

public sealed class ParamRangeDto
{
    public string Indicator { get; set; } = "";
    public string Param { get; set; } = "";
    public double Min { get; set; }
    public double Max { get; set; }
    public double Step { get; set; } = 1;
}

public class OptimizationRequest
{
    public string Code { get; set; } = "";
    public string? Name { get; set; }
    public string StartDate { get; set; } = "";
    public string EndDate { get; set; } = "";
    public string? Period { get; set; } = "daily";
    public BacktestBaseDto BaseConfig { get; set; } = new();
    public List<ParamRangeDto> ParamRanges { get; set; } = [];
    public string Metric { get; set; } = "sharpeRatio";
    public int TopN { get; set; } = 10;
}

public sealed class WalkForwardRequest : OptimizationRequest
{
    public int InsampleDays { get; set; } = 504;
    public int OutsampleDays { get; set; } = 252;
}

public sealed class AnalysisTrade
{
    public string Code { get; set; } = "";
    public string EntryDate { get; set; } = "";
    public double EntryPrice { get; set; }
    public string ExitDate { get; set; } = "";
    public double ExitPrice { get; set; }
    public double Shares { get; set; }
    public double Pnl { get; set; }
    public double PnlPct { get; set; }
    public string ExitReason { get; set; } = "";
}

public sealed class AnalysisMetrics
{
    public double TotalReturn { get; set; }
    public double AnnualizedReturn { get; set; }
    public double MaxDrawdown { get; set; }
    public int MaxDrawdownDuration { get; set; }
    public double SharpeRatio { get; set; }
    public double SortinoRatio { get; set; }
    public double WinRate { get; set; }
    /// <summary>无亏损且有盈利时为 Infinity；JSON 序列化为 null（与 Node 行为一致）。</summary>
    public double? ProfitFactor { get; set; }
    public double AvgWin { get; set; }
    public double AvgLoss { get; set; }
    public double CalmarRatio { get; set; }
    public int TotalTrades { get; set; }
}

public sealed class AnalysisEquityPoint
{
    public string Date { get; set; } = "";
    public double Value { get; set; }
}

public sealed class AnalysisSignal
{
    public string Date { get; set; } = "";
    public string Type { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed class IndicatorResultDto
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public Dictionary<string, double> Params { get; set; } = [];
    public List<string> Columns { get; set; } = [];
    public List<List<double?>> Values { get; set; } = [];
    public List<AnalysisSignal> Signals { get; set; } = [];
}

public sealed class OptimizationResultDto
{
    public Dictionary<string, double> Params { get; set; } = [];
    public AnalysisMetrics Metrics { get; set; } = new();
    public int TotalTrades { get; set; }
}

public sealed class WalkForwardWindowDto
{
    public string InsampleStart { get; set; } = "";
    public string InsampleEnd { get; set; } = "";
    public string OutsampleStart { get; set; } = "";
    public string OutsampleEnd { get; set; } = "";
}

public sealed class WalkForwardResultDto
{
    public WalkForwardWindowDto Window { get; set; } = new();
    public OptimizationResultDto InsampleBest { get; set; } = new();
    public AnalysisMetrics OutsampleMetrics { get; set; } = new();
    public int OutsampleTrades { get; set; }
    public bool Overfit { get; set; }
}

public sealed class BacktestResultDto
{
    public BacktestRequest Config { get; set; } = new();
    public List<AnalysisTrade> Trades { get; set; } = [];
    public List<AnalysisEquityPoint> EquityCurve { get; set; } = [];
    public AnalysisMetrics Metrics { get; set; } = new();
}

public sealed class ScreeningResultDto
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public double Close { get; set; }
    public double ChangePct { get; set; }
    public Dictionary<string, double> Indicators { get; set; } = [];
}

public sealed class WebhookConfigDto
{
    public string Type { get; set; } = "wecom";
    public string Url { get; set; } = "";
    public string? Secret { get; set; }
}

public sealed class MonitorJobDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Cron { get; set; } = "";
    public List<string> StockPool { get; set; } = [];
    public RuleNode EntryRules { get; set; } = RuleNode.Group("AND");
    public ExitRulesDto ExitRules { get; set; } = new();
    public List<string> Watchlist { get; set; } = [];
    public List<WebhookConfigDto> Webhooks { get; set; } = [];
    public bool Enabled { get; set; } = true;
}

public static class AnalysisJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}