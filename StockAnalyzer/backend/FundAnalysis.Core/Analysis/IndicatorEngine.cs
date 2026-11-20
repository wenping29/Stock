namespace FundAnalysis.Analysis;

public sealed class IndicatorComputation
{
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public Dictionary<string, double> Params { get; init; } = [];
    public List<string> Columns { get; init; } = [];
    public List<double[]> Values { get; init; } = [];
    public List<AnalysisSignal> Signals { get; init; } = [];

    public IndicatorResultDto ToDto()
    {
        return new IndicatorResultDto
        {
            Name = Name,
            Category = Category,
            Params = Params,
            Columns = Columns,
            Values = Values.Select(v => v.Select(x => double.IsNaN(x) || double.IsInfinity(x) ? (double?)null : x).ToList()).ToList(),
            Signals = Signals,
        };
    }
}

/// <summary>技术指标库，逐项对齐 stock-analyzer/server/src/indicators。</summary>
public static class IndicatorEngine
{
    public static readonly Dictionary<string, Dictionary<string, double>> Defaults = new()
    {
        ["MA"] = new() { ["period"] = 20 },
        ["EMA"] = new() { ["period"] = 20 },
        ["MACD"] = new() { ["fast"] = 12, ["slow"] = 26, ["signal"] = 9 },
        ["BOLL"] = new() { ["period"] = 20, ["stddev"] = 2 },
        ["RSI"] = new() { ["period"] = 14 },
        ["KDJ"] = new() { ["n"] = 9, ["k"] = 3, ["d"] = 3 },
        ["WR"] = new() { ["period"] = 14 },
        ["VOL"] = new() { ["period"] = 5 },
        ["OBV"] = new() { ["maPeriod"] = 20 },
        ["VolumeRatio"] = new() { ["period"] = 5 },
        ["ATR"] = new() { ["period"] = 14 },
    };

    public static List<object> List()
        => Defaults.Select(kv => (object)new
        {
            name = kv.Key,
            category = CategoryOf(kv.Key),
            defaultParams = kv.Value,
        }).ToList();

    public static string CategoryOf(string name) => name switch
    {
        "MA" or "EMA" or "MACD" or "BOLL" => "trend",
        "RSI" or "KDJ" or "WR" => "oscillator",
        "VOL" or "OBV" or "VolumeRatio" => "volume",
        "ATR" => "volatility",
        _ => "",
    };

    public static IndicatorComputation? Compute(string name, Dictionary<string, double>? overrides, IReadOnlyList<AnalysisKline> data)
    {
        if (!Defaults.TryGetValue(name, out var defaults)) return null;
        var p = new Dictionary<string, double>(defaults);
        if (overrides is not null)
            foreach (var kv in overrides) p[kv.Key] = kv.Value;

        var closes = data.Select(d => d.Close).ToArray();
        return name switch
        {
            "MA" => Ma(data, closes, p),
            "EMA" => EmaIndicator(data, closes, p),
            "MACD" => Macd(data, closes, p),
            "BOLL" => Boll(data, closes, p),
            "RSI" => Rsi(data, closes, p),
            "KDJ" => Kdj(data, p),
            "WR" => Wr(data, p),
            "VOL" => Vol(data, p),
            "OBV" => Obv(data, p),
            "VolumeRatio" => VolumeRatio(data, p),
            "ATR" => Atr(data, p),
            _ => null,
        };
    }

    private static int I(Dictionary<string, double> p, string key, int fallback)
        => p.TryGetValue(key, out var v) && !double.IsNaN(v) ? (int)v : fallback;

    private static IndicatorComputation Build(string name, Dictionary<string, double> p, string[] columns,
        double[][] values, List<AnalysisSignal> signals)
        => new()
        {
            Name = name,
            Category = CategoryOf(name),
            Params = p,
            Columns = [.. columns],
            Values = [.. values],
            Signals = signals,
        };

    // ---------- helpers ----------

    public static double[] Sma(IReadOnlyList<double> values, int period)
    {
        var result = new double[values.Count];
        double sum = 0;
        for (var i = 0; i < values.Count; i++)
        {
            sum += values[i];
            if (i >= period) sum -= values[i - period];
            result[i] = i >= period - 1 ? sum / period : double.NaN;
        }
        return result;
    }

    public static double[] Ema(IReadOnlyList<double> values, int period)
    {
        var result = new double[values.Count];
        if (values.Count == 0) return result;
        // 跳过前导 NaN（如 MACD 对 DIF 再求 EMA，DIF 前段为空），否则种子被 NaN 污染
        var start = 0;
        while (start < values.Count && double.IsNaN(values[start])) start++;
        if (start >= values.Count)
        {
            Array.Fill(result, double.NaN);
            return result;
        }
        var seedCount = Math.Min(period, values.Count - start);
        var seed = 0.0;
        for (var i = start; i < start + seedCount; i++) seed += values[i];
        seed /= period;
        var alpha = 2.0 / (period + 1);
        var prev = seed;
        for (var i = 0; i < start; i++) result[i] = double.NaN;
        for (var i = start; i < values.Count; i++)
        {
            if (i < start + period - 1) result[i] = double.NaN;
            else if (i == start + period - 1) result[i] = prev;
            else
            {
                prev = alpha * values[i] + (1 - alpha) * prev;
                result[i] = prev;
            }
        }
        return result;
    }

    public static bool[] CrossAbove(IReadOnlyList<double> fast, IReadOnlyList<double> slow)
    {
        var result = new bool[fast.Count];
        for (var i = 1; i < fast.Count; i++)
        {
            if (!double.IsNaN(fast[i]) && !double.IsNaN(slow[i]) &&
                !double.IsNaN(fast[i - 1]) && !double.IsNaN(slow[i - 1]) &&
                fast[i] > slow[i] && fast[i - 1] <= slow[i - 1])
                result[i] = true;
        }
        return result;
    }

    public static bool[] CrossBelow(IReadOnlyList<double> fast, IReadOnlyList<double> slow)
    {
        var result = new bool[fast.Count];
        for (var i = 1; i < fast.Count; i++)
        {
            if (!double.IsNaN(fast[i]) && !double.IsNaN(slow[i]) &&
                !double.IsNaN(fast[i - 1]) && !double.IsNaN(slow[i - 1]) &&
                fast[i] < slow[i] && fast[i - 1] >= slow[i - 1])
                result[i] = true;
        }
        return result;
    }

    // ---------- indicators ----------

    private static IndicatorComputation Ma(IReadOnlyList<AnalysisKline> data, double[] closes, Dictionary<string, double> p)
    {
        var period = I(p, "period", 20);
        var ma = Sma(closes, period);
        var signals = new List<AnalysisSignal>();
        if (period >= 5)
        {
            var shortMa = Sma(closes, 5);
            var up = CrossAbove(shortMa, ma);
            var down = CrossBelow(shortMa, ma);
            for (var i = 0; i < data.Count; i++)
            {
                if (up[i]) signals.Add(new() { Date = data[i].Date, Type = "buy", Description = $"MA5上穿MA{period}（金叉）" });
                if (down[i]) signals.Add(new() { Date = data[i].Date, Type = "sell", Description = $"MA5下穿MA{period}（死叉）" });
            }
        }
        return Build("MA", p, [$"MA_{period}"], [ma], signals);
    }

    private static IndicatorComputation EmaIndicator(IReadOnlyList<AnalysisKline> data, double[] closes, Dictionary<string, double> p)
    {
        var period = I(p, "period", 20);
        var values = Ema(closes, period);
        var signals = new List<AnalysisSignal>();
        if (period >= 5)
        {
            var shortEma = Ema(closes, 5);
            var up = CrossAbove(shortEma, values);
            var down = CrossBelow(shortEma, values);
            for (var i = 0; i < data.Count; i++)
            {
                if (up[i]) signals.Add(new() { Date = data[i].Date, Type = "buy", Description = $"EMA5上穿EMA{period}（金叉）" });
                if (down[i]) signals.Add(new() { Date = data[i].Date, Type = "sell", Description = $"EMA5下穿EMA{period}（死叉）" });
            }
        }
        return Build("EMA", p, [$"EMA_{period}"], [values], signals);
    }

    private static IndicatorComputation Macd(IReadOnlyList<AnalysisKline> data, double[] closes, Dictionary<string, double> p)
    {
        var fast = Ema(closes, I(p, "fast", 12));
        var slow = Ema(closes, I(p, "slow", 26));
        var dif = new double[closes.Length];
        for (var i = 0; i < closes.Length; i++)
            dif[i] = double.IsNaN(fast[i]) || double.IsNaN(slow[i]) ? double.NaN : fast[i] - slow[i];

        var dea = Ema(dif, I(p, "signal", 9));
        var bar = new double[closes.Length];
        for (var i = 0; i < closes.Length; i++)
            bar[i] = double.IsNaN(dif[i]) || double.IsNaN(dea[i]) ? double.NaN : 2 * (dif[i] - dea[i]);

        var signals = new List<AnalysisSignal>();
        var up = CrossAbove(dif, dea);
        var down = CrossBelow(dif, dea);
        for (var i = 0; i < data.Count; i++)
        {
            if (up[i]) signals.Add(new() { Date = data[i].Date, Type = "buy", Description = "DIF上穿DEA（MACD金叉）" });
            if (down[i]) signals.Add(new() { Date = data[i].Date, Type = "sell", Description = "DIF下穿DEA（MACD死叉）" });
        }
        return Build("MACD", p, ["DIF", "DEA", "BAR"], [dif, dea, bar], signals);
    }

    private static IndicatorComputation Boll(IReadOnlyList<AnalysisKline> data, double[] closes, Dictionary<string, double> p)
    {
        var period = I(p, "period", 20);
        var stddev = p.TryGetValue("stddev", out var sd) ? sd : 2;
        var mid = Sma(closes, period);
        var upper = new double[closes.Length];
        var lower = new double[closes.Length];
        for (var i = 0; i < closes.Length; i++)
        {
            if (i < period - 1 || double.IsNaN(mid[i]))
            {
                upper[i] = double.NaN;
                lower[i] = double.NaN;
                continue;
            }
            double variance = 0;
            for (var j = i - period + 1; j <= i; j++)
            {
                var d = closes[j] - mid[i];
                variance += d * d;
            }
            variance /= period;
            var std = Math.Sqrt(variance);
            upper[i] = mid[i] + stddev * std;
            lower[i] = mid[i] - stddev * std;
        }

        var signals = new List<AnalysisSignal>();
        for (var i = 0; i < data.Count; i++)
        {
            if (!double.IsNaN(upper[i]) && closes[i] > upper[i])
                signals.Add(new() { Date = data[i].Date, Type = "sell", Description = "价格突破布林上轨（超买）" });
            if (!double.IsNaN(lower[i]) && closes[i] < lower[i])
                signals.Add(new() { Date = data[i].Date, Type = "buy", Description = "价格跌破布林下轨（超卖）" });
        }
        return Build("BOLL", p, ["MID", "UPPER", "LOWER"], [mid, upper, lower], signals);
    }

    private static IndicatorComputation Rsi(IReadOnlyList<AnalysisKline> data, double[] closes, Dictionary<string, double> p)
    {
        var period = I(p, "period", 14);
        var gains = new List<double>();
        var losses = new List<double>();
        for (var i = 1; i < closes.Length; i++)
        {
            var delta = closes[i] - closes[i - 1];
            gains.Add(delta > 0 ? delta : 0);
            losses.Add(delta < 0 ? -delta : 0);
        }

        double sumGain = 0, sumLoss = 0;
        var take = Math.Min(period, gains.Count);
        for (var i = 0; i < take; i++) { sumGain += gains[i]; sumLoss += losses[i]; }
        var avgGain = sumGain / period;
        var avgLoss = sumLoss / period;

        var values = new double[closes.Length];
        for (var i = 0; i < closes.Length; i++)
        {
            if (i < period)
            {
                values[i] = double.NaN;
                continue;
            }
            if (i > period)
            {
                avgGain = (avgGain * (period - 1) + gains[i - 1]) / period;
                avgLoss = (avgLoss * (period - 1) + losses[i - 1]) / period;
            }
            var rs = avgLoss == 0 ? 100 : avgGain / avgLoss;
            values[i] = 100 - 100 / (1 + rs);
        }

        var signals = new List<AnalysisSignal>();
        for (var i = 0; i < data.Count; i++)
        {
            if (double.IsNaN(values[i])) continue;
            if (values[i] > 70) signals.Add(new() { Date = data[i].Date, Type = "sell", Description = "RSI>70（超买）" });
            if (values[i] < 30) signals.Add(new() { Date = data[i].Date, Type = "buy", Description = "RSI<30（超卖）" });
        }
        return Build("RSI", p, [$"RSI_{period}"], [values], signals);
    }

    private static IndicatorComputation Kdj(IReadOnlyList<AnalysisKline> data, Dictionary<string, double> p)
    {
        var n = I(p, "n", 9);
        var kValues = new double[data.Count];
        var dValues = new double[data.Count];
        var jValues = new double[data.Count];
        double k = 50, d = 50;
        for (var i = 0; i < data.Count; i++)
        {
            if (i < n - 1)
            {
                kValues[i] = dValues[i] = jValues[i] = double.NaN;
                continue;
            }
            double highest = double.MinValue, lowest = double.MaxValue;
            for (var j = i - n + 1; j <= i; j++)
            {
                if (data[j].High > highest) highest = data[j].High;
                if (data[j].Low < lowest) lowest = data[j].Low;
            }
            var rsv = lowest == highest ? 50 : (data[i].Close - lowest) / (highest - lowest) * 100;
            k = 2.0 / 3 * k + 1.0 / 3 * rsv;
            d = 2.0 / 3 * d + 1.0 / 3 * k;
            kValues[i] = k;
            dValues[i] = d;
            jValues[i] = 3 * k - 2 * d;
        }

        var signals = new List<AnalysisSignal>();
        var up = CrossAbove(kValues, dValues);
        var down = CrossBelow(kValues, dValues);
        for (var i = 0; i < data.Count; i++)
        {
            if (up[i]) signals.Add(new() { Date = data[i].Date, Type = "buy", Description = "K上穿D（KDJ金叉）" });
            if (down[i]) signals.Add(new() { Date = data[i].Date, Type = "sell", Description = "K下穿D（KDJ死叉）" });
            if (!double.IsNaN(kValues[i]) && kValues[i] < 20 && dValues[i] < 20)
                signals.Add(new() { Date = data[i].Date, Type = "buy", Description = "K/D<20（超卖）" });
            if (!double.IsNaN(kValues[i]) && kValues[i] > 80 && dValues[i] > 80)
                signals.Add(new() { Date = data[i].Date, Type = "sell", Description = "K/D>80（超买）" });
        }
        return Build("KDJ", p, ["K", "D", "J"], [kValues, dValues, jValues], signals);
    }

    private static IndicatorComputation Wr(IReadOnlyList<AnalysisKline> data, Dictionary<string, double> p)
    {
        var period = I(p, "period", 14);
        var values = new double[data.Count];
        for (var i = 0; i < data.Count; i++)
        {
            if (i < period - 1)
            {
                values[i] = double.NaN;
                continue;
            }
            double hh = double.MinValue, ll = double.MaxValue;
            for (var j = i - period + 1; j <= i; j++)
            {
                if (data[j].High > hh) hh = data[j].High;
                if (data[j].Low < ll) ll = data[j].Low;
            }
            values[i] = hh == ll ? 50 : (hh - data[i].Close) / (hh - ll) * 100;
        }

        var signals = new List<AnalysisSignal>();
        for (var i = 0; i < data.Count; i++)
        {
            if (double.IsNaN(values[i])) continue;
            if (values[i] < 20) signals.Add(new() { Date = data[i].Date, Type = "sell", Description = "WR<20（超买）" });
            if (values[i] > 80) signals.Add(new() { Date = data[i].Date, Type = "buy", Description = "WR>80（超卖）" });
        }
        return Build("WR", p, [$"WR_{period}"], [values], signals);
    }

    private static IndicatorComputation Vol(IReadOnlyList<AnalysisKline> data, Dictionary<string, double> p)
    {
        var period = I(p, "period", 5);
        var volumes = data.Select(d => d.Volume).ToArray();
        var volMa = Sma(volumes, period);
        var signals = new List<AnalysisSignal>();
        for (var i = 0; i < data.Count; i++)
            if (!double.IsNaN(volMa[i]) && volumes[i] > 2 * volMa[i])
                signals.Add(new() { Date = data[i].Date, Type = "buy", Description = "成交量放大2倍以上（放量）" });
        return Build("VOL", p, [$"VOL_MA_{period}"], [volMa], signals);
    }

    private static IndicatorComputation Obv(IReadOnlyList<AnalysisKline> data, Dictionary<string, double> p)
    {
        var obv = new double[data.Count];
        double value = 0;
        for (var i = 0; i < data.Count; i++)
        {
            if (i == 0) value = data[i].Volume;
            else if (data[i].Close > data[i - 1].Close) value += data[i].Volume;
            else if (data[i].Close < data[i - 1].Close) value -= data[i].Volume;
            obv[i] = value;
        }
        var maPeriod = I(p, "maPeriod", 20);
        var obvMa = Sma(obv, maPeriod);
        var signals = new List<AnalysisSignal>();
        var up = CrossAbove(obv, obvMa);
        var down = CrossBelow(obv, obvMa);
        for (var i = 0; i < data.Count; i++)
        {
            if (up[i]) signals.Add(new() { Date = data[i].Date, Type = "buy", Description = "OBV上穿均线（资金流入）" });
            if (down[i]) signals.Add(new() { Date = data[i].Date, Type = "sell", Description = "OBV下穿均线（资金流出）" });
        }
        return Build("OBV", p, ["OBV", $"OBV_MA_{maPeriod}"], [obv, obvMa], signals);
    }

    private static IndicatorComputation VolumeRatio(IReadOnlyList<AnalysisKline> data, Dictionary<string, double> p)
    {
        var period = I(p, "period", 5);
        var volumes = data.Select(d => d.Volume).ToArray();
        var values = new double[data.Count];
        for (var i = 0; i < data.Count; i++)
        {
            if (i < period)
            {
                values[i] = double.NaN;
                continue;
            }
            double sum = 0;
            for (var j = i - period; j < i; j++) sum += volumes[j];
            var avg = sum / period;
            values[i] = avg > 0 ? volumes[i] / avg : 1;
        }

        var signals = new List<AnalysisSignal>();
        for (var i = 0; i < data.Count; i++)
        {
            if (double.IsNaN(values[i])) continue;
            if (values[i] > 3) signals.Add(new() { Date = data[i].Date, Type = "buy", Description = "量比>3（极度放量）" });
            else if (values[i] > 1.5) signals.Add(new() { Date = data[i].Date, Type = "buy", Description = "量比>1.5（放量）" });
        }
        return Build("VolumeRatio", p, ["VR"], [values], signals);
    }

    private static IndicatorComputation Atr(IReadOnlyList<AnalysisKline> data, Dictionary<string, double> p)
    {
        var period = I(p, "period", 14);
        var tr = new double[data.Count];
        for (var i = 0; i < data.Count; i++)
        {
            tr[i] = i == 0
                ? data[i].High - data[i].Low
                : Math.Max(data[i].High - data[i].Low,
                    Math.Max(Math.Abs(data[i].High - data[i - 1].Close), Math.Abs(data[i].Low - data[i - 1].Close)));
        }
        var atr = Sma(tr, period);
        if (period < atr.Length && !double.IsNaN(atr[period - 1]))
        {
            var prev = atr[period - 1];
            for (var i = period; i < atr.Length; i++)
            {
                prev = (prev * (period - 1) + tr[i]) / period;
                atr[i] = prev;
            }
        }

        var signals = new List<AnalysisSignal>();
        for (var i = 5; i < data.Count; i++)
            if (!double.IsNaN(atr[i]) && !double.IsNaN(atr[i - 5]) && atr[i] > atr[i - 5] * 1.3)
                signals.Add(new() { Date = data[i].Date, Type = "buy", Description = "ATR上升（波动加剧）" });
        return Build("ATR", p, [$"ATR_{period}"], [atr], signals);
    }
}