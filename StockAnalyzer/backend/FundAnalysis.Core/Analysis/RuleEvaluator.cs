using System.Text.Json;

namespace FundAnalysis.Analysis;

public static class RuleEvaluator
{
    public static bool Evaluate(IReadOnlyList<AnalysisKline> data, RuleNode? rule)
    {
        if (rule is null) return false;
        if (rule.IsGroup)
        {
            var conditions = rule.Conditions ?? [];
            return string.Equals(rule.Logic, "OR", StringComparison.OrdinalIgnoreCase)
                ? conditions.Any(c => Evaluate(data, c))
                : conditions.All(c => Evaluate(data, c));
        }

        if (string.IsNullOrEmpty(rule.Indicator) || data.Count == 0) return false;
        var indicator = IndicatorEngine.Compute(rule.Indicator, rule.Params, data);
        if (indicator is null || indicator.Values.Count == 0) return false;

        var values = indicator.Values[0];
        var last = data.Count - 1;
        var val = values[last];
        if (double.IsNaN(val)) return false;

        return rule.Operator switch
        {
            ">" => val > rule.Value,
            "<" => val < rule.Value,
            ">=" => val >= rule.Value,
            "<=" => val <= rule.Value,
            "==" => Math.Abs(val - rule.Value) < 0.01,
            "cross_above" => Crossed(values, last, rule.Value, above: true),
            "cross_below" => Crossed(values, last, rule.Value, above: false),
            _ => false,
        };
    }

    private static bool Crossed(double[] values, int last, double threshold, bool above)
    {
        for (var i = Math.Max(1, last - 2); i <= last; i++)
        {
            var v = values[i];
            var prev = values[i - 1];
            if (double.IsNaN(v) || double.IsNaN(prev)) continue;
            if (above ? v > threshold && prev <= threshold : v < threshold && prev >= threshold)
                return true;
        }
        return false;
    }

    public static (bool Valid, List<string> Errors) Validate(RuleNode? rules)
    {
        var errors = new List<string>();
        void Check(RuleNode? node)
        {
            if (node is null)
            {
                errors.Add("规则为空");
                return;
            }
            if (node.IsGroup)
            {
                if ((node.Conditions?.Count ?? 0) == 0) errors.Add("规则组为空");
                foreach (var c in node.Conditions ?? []) Check(c);
                return;
            }
            if (string.IsNullOrEmpty(node.Indicator) || !IndicatorEngine.Defaults.ContainsKey(node.Indicator))
                errors.Add($"未知指标: {node.Indicator}");
        }
        Check(rules);
        return (errors.Count == 0, errors);
    }
}

public static class ScreeningPresets
{
    private static readonly Dictionary<string, JsonElement> Roots = new()
    {
        ["trend_following"] = Parse("""
        {
          "name": "趋势追踪",
          "description": "短期上涨趋势明确且量能支撑",
          "rules": {
            "logic": "AND",
            "conditions": [
              { "indicator": "EMA", "operator": ">", "value": "EMA_20", "params": { "period": 5 } },
              { "indicator": "MACD", "operator": "cross_above", "value": 0, "params": {} },
              { "indicator": "VOL", "operator": ">", "value": 1.2, "params": { "period": 5 } }
            ]
          }
        }
        """),
        ["oversold_rebound"] = Parse("""
        {
          "name": "超跌反弹",
          "description": "超卖且资金开始流入",
          "rules": {
            "logic": "AND",
            "conditions": [
              { "indicator": "RSI", "operator": "<", "value": 25, "params": { "period": 12 } },
              { "indicator": "OBV", "operator": "cross_above", "value": 0, "params": { "maPeriod": 20 } },
              { "indicator": "WR", "operator": ">", "value": 80, "params": { "period": 14 } }
            ]
          }
        }
        """),
        ["breakout"] = Parse("""
        {
          "name": "放量突破",
          "description": "价格突破布林带上轨且放量",
          "rules": {
            "logic": "AND",
            "conditions": [
              { "indicator": "BOLL", "operator": ">", "value": 0, "params": { "period": 20, "stddev": 2 } },
              { "indicator": "VolumeRatio", "operator": ">", "value": 2, "params": { "period": 5 } }
            ]
          }
        }
        """),
    };

    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public static List<object> List()
        => Roots.Select(kv => (object)new
        {
            key = kv.Key,
            name = kv.Value.GetProperty("name").GetString(),
            description = kv.Value.GetProperty("description").GetString(),
        }).ToList();

    public static JsonElement? Get(string key)
        => Roots.TryGetValue(key, out var root) ? root : null;

    public static RuleNode? GetRules(string key)
    {
        if (!Roots.TryGetValue(key, out var root) || !root.TryGetProperty("rules", out var rules)) return null;
        return RuleNodeJsonConverter.Parse(rules);
    }
}