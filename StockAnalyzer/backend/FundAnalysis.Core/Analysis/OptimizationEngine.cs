using System.Text.Json;

namespace FundAnalysis.Analysis;

public static class OptimizationEngine
{
    private const int MaxCombinations = 10000;

    public static List<OptimizationResultDto> GridSearch(OptimizationRequest request, IReadOnlyList<AnalysisKline> kline)
    {
        var combinations = GenerateCombinations(request.ParamRanges);
        var results = new List<OptimizationResultDto>();
        foreach (var combo in combinations)
        {
            var config = BuildConfig(request, combo, recursive: true);
            var result = BacktestEngine.Run(config, kline);
            results.Add(new OptimizationResultDto
            {
                Params = combo,
                Metrics = result.Metrics,
                TotalTrades = result.Trades.Count,
            });
        }

        return results
            .OrderByDescending(r => MetricValue(r.Metrics, request.Metric))
            .Take(request.TopN <= 0 ? 10 : request.TopN)
            .ToList();
    }

    public static List<WalkForwardResultDto> WalkForward(WalkForwardRequest request, IReadOnlyList<AnalysisKline> kline)
    {
        var results = new List<WalkForwardResultDto>();
        var insampleDays = request.InsampleDays;
        var outsampleDays = request.OutsampleDays;
        if (insampleDays <= 0 || outsampleDays <= 0) return results;

        var insampleStart = 0;
        while (insampleStart + insampleDays + outsampleDays <= kline.Count)
        {
            var insampleEnd = insampleStart + insampleDays;
            var outsampleStart = insampleEnd;
            var outsampleEnd = Math.Min(outsampleStart + outsampleDays, kline.Count);

            var insampleData = kline.Skip(insampleStart).Take(insampleDays).ToList();
            // 与 Node 实现一致：out-sample 窗口包含 in-sample 段
            var outsampleData = kline.Skip(insampleStart).Take(outsampleEnd - insampleStart).ToList();

            var optReq = new OptimizationRequest
            {
                Code = request.Code,
                Name = request.Name,
                StartDate = insampleData[0].Date,
                EndDate = insampleData[^1].Date,
                Period = request.Period,
                BaseConfig = request.BaseConfig,
                ParamRanges = request.ParamRanges,
                Metric = request.Metric,
                TopN = request.TopN,
            };
            var insampleResults = GridSearch(optReq, insampleData);
            if (insampleResults.Count == 0)
            {
                insampleStart += outsampleDays;
                continue;
            }

            var best = insampleResults[0];
            var fullConfig = BuildConfig(request, best.Params, recursive: false);
            fullConfig.Code = request.Code;
            fullConfig.Name = request.Name;
            fullConfig.StartDate = outsampleData[0].Date;
            fullConfig.EndDate = outsampleData[^1].Date;
            fullConfig.Period = request.Period;

            var outsample = BacktestEngine.Run(fullConfig, outsampleData);
            var insampleSharpe = best.Metrics.SharpeRatio;
            var outsampleSharpe = outsample.Metrics.SharpeRatio;
            var overfit = insampleSharpe > 0 && outsampleSharpe < insampleSharpe * 0.5;

            results.Add(new WalkForwardResultDto
            {
                Window = new WalkForwardWindowDto
                {
                    InsampleStart = insampleData[0].Date,
                    InsampleEnd = insampleData[^1].Date,
                    OutsampleStart = outsampleData[insampleEnd].Date,
                    OutsampleEnd = outsampleData[^1].Date,
                },
                InsampleBest = best,
                OutsampleMetrics = outsample.Metrics,
                OutsampleTrades = outsample.Trades.Count,
                Overfit = overfit,
            });

            insampleStart += outsampleDays;
        }

        return results;
    }

    private static BacktestRequest BuildConfig(OptimizationRequest request, Dictionary<string, double> parameters, bool recursive)
    {
        var baseConfig = DeepClone(request.BaseConfig);
        var config = new BacktestRequest
        {
            Code = request.Code,
            Name = request.Name,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Period = request.Period,
            InitialCapital = baseConfig.InitialCapital,
            EntryRules = baseConfig.EntryRules,
            ExitRules = baseConfig.ExitRules,
            PositionMethod = baseConfig.PositionMethod,
            PositionSizing = baseConfig.PositionSizing,
            Commission = baseConfig.Commission,
            Slippage = baseConfig.Slippage,
        };
        ApplyToRules(config.EntryRules, parameters, recursive);
        return config;
    }

    private static BacktestBaseDto DeepClone(BacktestBaseDto value)
        => JsonSerializer.Deserialize<BacktestBaseDto>(
               JsonSerializer.Serialize(value, AnalysisJson.Options), AnalysisJson.Options) ?? new BacktestBaseDto();

    private static void ApplyToRules(RuleNode? node, Dictionary<string, double> parameters, bool recursive)
    {
        if (node is null) return;
        if (node.IsGroup)
        {
            if (recursive)
                foreach (var child in node.Conditions ?? []) ApplyToRules(child, parameters, true);
            return;
        }
        if (node.Params.Count == 0) return;
        var firstKey = node.Params.Keys.First();
        var key = $"{node.Indicator}.{firstKey}";
        if (parameters.TryGetValue(key, out var value)) node.Params[firstKey] = value;
    }

    private static List<Dictionary<string, double>> GenerateCombinations(List<ParamRangeDto> ranges)
    {
        if (ranges.Count == 0) return [];
        var combos = new List<Dictionary<string, double>> { new() };
        foreach (var range in ranges)
        {
            if (range.Step <= 0) throw new AnalysisRequestException("参数步长必须大于 0");
            var next = new List<Dictionary<string, double>>();
            for (var v = range.Min; v <= range.Max + 1e-9; v += range.Step)
            {
                var rounded = Math.Round(v * 10000) / 10000;
                foreach (var combo in combos)
                {
                    var copy = new Dictionary<string, double>(combo)
                    {
                        [$"{range.Indicator}.{range.Param}"] = rounded,
                    };
                    next.Add(copy);
                }
            }
            combos = next;
            if (combos.Count > MaxCombinations)
                throw new AnalysisRequestException($"参数组合数 {combos.Count} 超过上限 {MaxCombinations}，请缩小参数范围");
        }
        return combos;
    }

    private static double MetricValue(AnalysisMetrics m, string metric) => metric switch
    {
        "totalReturn" => m.TotalReturn,
        "annualizedReturn" => m.AnnualizedReturn,
        "maxDrawdown" => m.MaxDrawdown,
        "maxDrawdownDuration" => m.MaxDrawdownDuration,
        "sharpeRatio" => m.SharpeRatio,
        "sortinoRatio" => m.SortinoRatio,
        "winRate" => m.WinRate,
        "profitFactor" => m.ProfitFactor ?? (m.AvgWin > 0 ? double.MaxValue : 0),
        "avgWin" => m.AvgWin,
        "avgLoss" => m.AvgLoss,
        "calmarRatio" => m.CalmarRatio,
        "totalTrades" => m.TotalTrades,
        _ => m.TotalReturn,
    };
}

public sealed class AnalysisRequestException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}