namespace FundAnalysis.Analysis;

/// <summary>回测引擎，逐行对齐 stock-analyzer/server/src/backtesting。</summary>
public static class BacktestEngine
{
    private const int Warmup = 60;

    private sealed class OpenPosition
    {
        public double Shares { get; set; }
        public double EntryPrice { get; set; }
        public string EntryDate { get; set; } = "";
        public int EntryIndex { get; set; }
        public double AllocatedCapital { get; set; }
    }

    public static BacktestResultDto Run(BacktestRequest config, IReadOnlyList<AnalysisKline> kline)
    {
        var result = new BacktestResultDto { Config = config };
        if (kline.Count < Warmup + 1)
        {
            result.Metrics = new AnalysisMetrics();
            result.EquityCurve.Add(new AnalysisEquityPoint
            {
                Date = kline.Count > 0 ? kline[0].Date : "",
                Value = config.InitialCapital,
            });
            return result;
        }

        var trades = result.Trades;
        var equity = result.EquityCurve;
        var cash = config.InitialCapital;
        OpenPosition? position = null;

        for (var i = 0; i < Warmup; i++)
            equity.Add(new AnalysisEquityPoint { Date = kline[i].Date, Value = config.InitialCapital });

        for (var i = Warmup; i < kline.Count - 1; i++)
        {
            var past = kline.Take(i + 1).ToList();
            var next = kline[i + 1];
            var nextOpen = next.Open;
            var nextDate = next.Date;

            if (position is not null)
            {
                var (exit, reason) = ShouldExit(past, position.EntryIndex, i, position.EntryPrice, config.ExitRules);
                if (exit)
                {
                    var exitPrice = nextOpen * (1 - config.Slippage);
                    var gross = (exitPrice - position.EntryPrice) * position.Shares;
                    var commission = (position.EntryPrice + exitPrice) * position.Shares * config.Commission;
                    var pnl = gross - commission;
                    var pnlPct = (exitPrice - position.EntryPrice) / position.EntryPrice * 100;
                    cash += position.AllocatedCapital + pnl;
                    trades.Add(new AnalysisTrade
                    {
                        Code = config.Code,
                        EntryDate = position.EntryDate,
                        EntryPrice = position.EntryPrice,
                        ExitDate = nextDate,
                        ExitPrice = exitPrice,
                        Shares = position.Shares,
                        Pnl = Round2(pnl),
                        PnlPct = Round2(pnlPct),
                        ExitReason = reason,
                    });
                    position = null;
                }
            }

            if (position is null && RuleEvaluator.Evaluate(past, config.EntryRules))
            {
                var entryPrice = nextOpen * (1 + config.Slippage);
                double? atr = null;
                var atrResult = IndicatorEngine.Compute("ATR", new Dictionary<string, double> { ["period"] = 14 }, past);
                if (atrResult is { Values.Count: > 0 })
                {
                    var v = atrResult.Values[0][i];
                    if (!double.IsNaN(v)) atr = v;
                }

                var (shares, allocated) = CalculatePosition(
                    config.PositionMethod, cash, atr, entryPrice, config.PositionSizing);
                if (shares > 0 && allocated <= cash)
                {
                    cash -= allocated;
                    position = new OpenPosition
                    {
                        Shares = shares,
                        EntryPrice = entryPrice,
                        EntryDate = nextDate,
                        EntryIndex = i + 1,
                        AllocatedCapital = allocated,
                    };
                }
            }

            var positionValue = position is not null
                ? position.Shares * next.Close * (1 - config.Slippage)
                : 0;
            equity.Add(new AnalysisEquityPoint { Date = nextDate, Value = cash + positionValue });
        }

        if (position is not null)
        {
            var last = kline[^1];
            var exitPrice = last.Close * (1 - config.Slippage);
            var gross = (exitPrice - position.EntryPrice) * position.Shares;
            var commission = (position.EntryPrice + exitPrice) * position.Shares * config.Commission;
            var pnl = gross - commission;
            var pnlPct = (exitPrice - position.EntryPrice) / position.EntryPrice * 100;
            cash += position.AllocatedCapital + pnl;
            trades.Add(new AnalysisTrade
            {
                Code = config.Code,
                EntryDate = position.EntryDate,
                EntryPrice = position.EntryPrice,
                ExitDate = last.Date,
                ExitPrice = exitPrice,
                Shares = position.Shares,
                Pnl = Round2(pnl),
                PnlPct = Round2(pnlPct),
                ExitReason = "回测结束",
            });
        }

        result.Metrics = CalculatePerformance(trades, equity, config.InitialCapital);
        return result;
    }

    /// <summary>监控扫描复用回测的退出判定。</summary>
    public static (bool Exit, string Reason) EvaluateExit(
        IReadOnlyList<AnalysisKline> data, int entryIndex, int currentIndex, double entryPrice, ExitRulesDto rules)
        => ShouldExit(data, entryIndex, currentIndex, entryPrice, rules);

    private static (bool Exit, string Reason) ShouldExit(
        IReadOnlyList<AnalysisKline> data, int entryIndex, int currentIndex, double entryPrice, ExitRulesDto rules)
    {
        if (currentIndex <= entryIndex || currentIndex < 0 || currentIndex >= data.Count) return (false, "");
        var currentPrice = data[currentIndex].Close;
        var changePct = (currentPrice - entryPrice) / entryPrice * 100;

        if (rules.StopLossPct is > 0 && changePct <= -rules.StopLossPct.Value)
            return (true, $"止损 {changePct:F1}%");

        if (rules.StopLossAtrMultiplier is > 0)
        {
            var atr = IndicatorEngine.Compute("ATR", new Dictionary<string, double> { ["period"] = 14 }, data);
            if (atr is { Values.Count: > 0 })
            {
                var val = atr.Values[0][currentIndex];
                if (!double.IsNaN(val))
                {
                    var stop = entryPrice - rules.StopLossAtrMultiplier.Value * val;
                    if (currentPrice <= stop) return (true, $"ATR止损 {changePct:F1}%");
                }
            }
        }

        if (rules.TakeProfitPct is > 0 && changePct >= rules.TakeProfitPct.Value)
            return (true, $"止盈 {changePct:F1}%");

        if (rules.ExitIndicator is not null && RuleEvaluator.Evaluate(data, rules.ExitIndicator))
            return (true, "指标退出信号");

        return (false, "");
    }

    private static (double Shares, double Allocated) CalculatePosition(
        string method, double capital, double? atr, double price, double fixedPct)
    {
        double allocated;
        if (string.Equals(method, "volatility_weighted", StringComparison.OrdinalIgnoreCase)
            && atr is > 0 && price > 0)
        {
            var volRatio = atr.Value / price;
            var weight = Math.Min(1 / Math.Max(volRatio, 0.01), 1);
            allocated = capital * fixedPct * weight;
        }
        else
        {
            allocated = capital * fixedPct;
        }
        var shares = Math.Floor(allocated / price / 100) * 100;
        return (shares, allocated);
    }

    public static AnalysisMetrics CalculatePerformance(
        IReadOnlyList<AnalysisTrade> trades, IReadOnlyList<AnalysisEquityPoint> equity, double initialCapital)
    {
        var finalEquity = equity.Count > 0 ? equity[^1].Value : initialCapital;
        var totalReturn = (finalEquity - initialCapital) / initialCapital * 100;

        var totalYears = 1.0;
        if (equity.Count >= 2)
        {
            var start = AnalysisDataService.ParseKlineDate(equity[0].Date);
            var end = AnalysisDataService.ParseKlineDate(equity[^1].Date);
            totalYears = Math.Max((end - start).TotalDays / 365.25, 0.25);
        }

        var decimalReturn = (finalEquity - initialCapital) / initialCapital;
        var annualized = totalYears > 0
            ? (Math.Pow(1 + decimalReturn, 1 / totalYears) - 1) * 100
            : totalReturn;

        double maxDrawdown = 0;
        var peak = initialCapital;
        int? ddStart = null;
        for (var i = 0; i < equity.Count; i++)
        {
            var value = equity[i].Value;
            if (value > peak)
            {
                peak = value;
                ddStart = null;
            }
            else
            {
                var dd = (peak - value) / peak * 100;
                if (dd > maxDrawdown) maxDrawdown = dd;
                ddStart ??= i;
            }
        }
        var ddDuration = ddStart is not null ? equity.Count - ddStart.Value : 0;

        var daily = new List<double>();
        for (var i = 1; i < equity.Count; i++)
            daily.Add((equity[i].Value - equity[i - 1].Value) / equity[i - 1].Value);

        var avg = daily.Count > 0 ? daily.Average() : 0;
        var std = daily.Count > 1
            ? Math.Sqrt(daily.Sum(r => Math.Pow(r - avg, 2)) / (daily.Count - 1))
            : 0;
        const double rfDaily = 0.03 / 252;
        var sharpe = std > 0 ? (avg - rfDaily) / std * Math.Sqrt(252) : 0;

        var downside = daily.Where(r => r < 0).ToList();
        var avgDown = downside.Count > 0 ? downside.Average() : 0;
        var stdDown = downside.Count > 1
            ? Math.Sqrt(downside.Sum(r => Math.Pow(r - avgDown, 2)) / (downside.Count - 1))
            : 0;
        var sortino = stdDown > 0 ? (avg - rfDaily) / stdDown * Math.Sqrt(252) : 0;

        var wins = trades.Where(t => t.Pnl > 0).ToList();
        var losses = trades.Where(t => t.Pnl < 0).ToList();
        var winRate = trades.Count > 0 ? (double)wins.Count / trades.Count * 100 : 0;
        var gains = wins.Sum(t => t.Pnl);
        var lossAmount = Math.Abs(losses.Sum(t => t.Pnl));
        double? profitFactor = lossAmount > 0 ? gains / lossAmount : gains > 0 ? null : 0;
        var avgWin = wins.Count > 0 ? gains / wins.Count : 0;
        var avgLoss = losses.Count > 0 ? lossAmount / losses.Count : 0;
        var calmar = maxDrawdown > 0 ? annualized / maxDrawdown : 0;

        return new AnalysisMetrics
        {
            TotalReturn = Round2(totalReturn),
            AnnualizedReturn = Round2(annualized),
            MaxDrawdown = Round2(maxDrawdown),
            MaxDrawdownDuration = ddDuration,
            SharpeRatio = Round2(sharpe),
            SortinoRatio = Round2(sortino),
            WinRate = Round2(winRate),
            ProfitFactor = profitFactor is null ? null : Round2(profitFactor.Value),
            AvgWin = Round2(avgWin),
            AvgLoss = Round2(avgLoss),
            CalmarRatio = Round2(calmar),
            TotalTrades = trades.Count,
        };
    }

    public static double Round2(double v) => double.IsFinite(v) ? Math.Round(v * 100) / 100 : v;
}