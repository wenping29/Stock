namespace FundAnalysis.Models;

// ---------- 认证相关 ----------

public record RegisterRequest(string UserName, string Password, string? Email);

public record LoginRequest(string UserName, string Password);

public record RefreshRequest(string RefreshToken);

public record ChangePasswordRequest(string OldPassword, string NewPassword);

public record UserInfoDto(string Id, string UserName, string? Email, IList<string> Roles);

public record TokenResponse(string AccessToken, string RefreshToken, int ExpiresInMinutes, UserInfoDto User);

// ---------- 行情相关 ----------

public record SearchItemDto(string Symbol, string Name, string Type, string MarketName);

public record KLinePointDto(string Time, double Open, double High, double Low, double Close, double Volume, double ChangePct);

public record KLineResponse(string Symbol, string Name, string Type, string Period, List<KLinePointDto> Points);

public record PerfWindowDto(string Key, string Label, double? ReturnPct);

public record PerformanceResponse(
    string Symbol, string Name, string Type,
    double LatestPrice, string LatestDate,
    List<PerfWindowDto> Windows,
    double MaxDrawdownPct);

public record MoneyFlowItemDto(string Date, double MainNet, double LargeNet, double MidNet, double SmallNet);

public record MoneyFlowResponse(string Symbol, List<MoneyFlowItemDto> Items, double CumulativeMainNet);

public record WatchlistAddRequest(string Symbol);

public record WatchlistItemDto(
    string Symbol, string Name, string Type,
    double? LatestPrice, double? ChangePct, double? MonthReturnPct, double? MainNetIn5d);

// ---------- 基金重仓股估算 ----------

public record FundHoldingItemDto(
    string Symbol, string Name, int SecMarket,
    double WeightPct, double? Price, double? ChangePct);

/// <summary>
/// 基金当日涨跌估算：按最新季报前十大重仓股的实时涨跌幅、以占净值比例加权。
/// 公募基金只按季披露前十大重仓，CoveredWeightPct 为参与计算的持仓占净值比例
/// （估算已按该覆盖权重归一，未披露部分不计入）。
/// QuoteSource 为本次实际使用的行情源：Eastmoney / Tencent（备用）/ None（均不可用）。
/// </summary>
public record FundEstimateDto(
    string Symbol, string Name,
    double? EstimatedChangePct,
    double CoveredWeightPct,
    DateTime? ReportDate,
    List<FundHoldingItemDto> Holdings,
    string QuoteSource);

// ---------- 管理后台相关 ----------

public record AdminUserDto(string Id, string UserName, string? Email, string Role, bool Enabled, DateTime CreatedAt);

public record UpdateRoleRequest(string Role);

public record UpdateEnabledRequest(bool Enabled);

// ---------- 央行流动性 ----------

public record MarketIndexDto(string Code, string Name, string Market, double Price, double ChangePct, List<double> Trend);

/// <summary>两市成交量（沪=上证指数成交额，深=深证综指成交额）。单位：亿元。</summary>
public record MarketVolumePointDto(string Date, double Sh, double Sz, double Total);

public record MarketVolumeDto(
    double TodayAmount,
    double YesterdayAmount,
    double? ChangePct,
    List<MarketVolumePointDto> Points);

/// <summary>今日两市成交额分时累计点。Time 为 HH:mm，Amount 为累计成交额（亿元）。</summary>
public record MarketVolumeCurvePointDto(string Time, double Amount);

/// <summary>今日两市成交额分时曲线（沪 000001 + 深 399106 分钟成交额累加）。</summary>
public record MarketVolumeTodayDto(string Date, List<MarketVolumeCurvePointDto> Points);

/// <summary>行业板块主力资金流（金额单位：亿元）。</summary>
public record SectorFlowItemDto(string Code, string Name, double MainNet, double Amount, double ChangePct);

public record SectorFlowDto(string? Date, List<SectorFlowItemDto> Items);

/// <summary>人民币汇率（离岸 USDCNH）。数值上升 = 人民币贬值。Parity 为央行中间价（实时值）。</summary>
public record FxPointDto(string Date, double Rate);

public record FxRateDto(
    double Latest,
    string LatestDate,
    double DayChangePct,
    double? MonthChangePct,
    List<FxPointDto> Points,
    double ParityLatest,
    double ParityChangePct);

/// <summary>十年期美债收益率（%）。DayChangeBp：较上日变动（基点）。</summary>
public record Us10yDto(
    double Latest,
    string LatestDate,
    double DayChangeBp,
    double? MonthChangeBp,
    List<FxPointDto> Points);

/// <summary>人民币中长期国债收益率（%）：5 年 / 10 年 / 30 年期。</summary>
public record CnBondPointDto(string Date, double Y5, double Y10, double Y30);

public record CnBondDto(
    double Latest10y,
    string LatestDate,
    double DayChangeBp,
    double? MonthChangeBp,
    List<CnBondPointDto> Points);

/// <summary>指数页面清单项。Amount 为当日成交额（亿元）；MainInflow 为主力净流入（元，仅行业指数有值）。</summary>
public record IndexItemDto(
    string Code, string Name, string Group, double Price, double ChangePct, double Amount,
    List<double> Trend, double? MainInflow = null);

public record IndicesResponse(List<IndexItemDto> Items);

/// <summary>指数日内分时点。Time 为 HH:mm；Avg 为当日累计均价；Volume 单位：手。</summary>
public record IndexTrendPointDto(string Time, double Price, double Volume, double Avg);

/// <summary>
/// 指数日内分时走势（东财实时 trends2）。PrevClose 取 IndexDaily 上一交易日收盘价，
/// 供前端绘制昨收基准线与价格轴断轴（昨收不在当日波动区间时压缩中间空档）。
/// </summary>
public record IndexTrendResponse(string Code, string Name, string Date, double PrevClose, List<IndexTrendPointDto> Points);

/// <summary>指数关联的场内 ETF；行情源不可用时 Price/ChangePct 为 null，Amount（成交额，元）为 0。</summary>
public record IndexEtfItemDto(string Symbol, string Name, double? Price, double? ChangePct, double Amount);

/// <summary>ETF 实时行情明细（Volume 单位手，Amount 单位元，换手率/量比/振幅单位 %）。</summary>
public record EtfQuoteDto(
    string Symbol, string Name, string? Industry,
    double? Price, double? PrevClose, double? Open, double? High, double? Low,
    double? Change, double? ChangePct, double? Volume, double? Amount,
    double? TurnoverRate, double? VolumeRatio, double? Amplitude);

// ---------- 自定义指数 ----------

/// <summary>自定义指数成分股。</summary>
public record CustomIndexComponentDto(string Symbol, string Name, double Weight);

/// <summary>自定义指数清单项：最新点位/涨跌幅 + 近 30 日点位迷你走势。</summary>
public record CustomIndexItemDto(
    long Id, string Name, double BaseValue, DateTime CreatedAt,
    List<CustomIndexComponentDto> Components,
    double Price, double ChangePct, List<double> Trend);

/// <summary>保存自定义指数（新增 / 编辑共用）。Weight 为相对权重，服务端按合计归一化。</summary>
public record CustomIndexSaveRequest(
    string Name, double? BaseValue, List<CustomIndexComponentInput> Components);

public record CustomIndexComponentInput(string Symbol, string? Name, double Weight);

/// <summary>预设指数（按官方成分股快速配置自定义指数）。</summary>
public record CustomIndexPresetDto(string Key, string Name, int Count);

/// <summary>预设指数成分股清单；Weight 为官方权重，缺失时按等权填充。</summary>
public record CustomIndexPresetDetailDto(string Key, string Name, List<CustomIndexComponentDto> Components);

/// <summary>
/// 手动采集作业（前端"刷新"触发）。State 取 Hangfire 作业状态：
/// Enqueued / Processing / Succeeded / Failed / Scheduled 等。
/// </summary>
public record CollectJobDto(string JobId, string Category, string State, DateTime? EnqueuedAt);

// ---------- 选股 ----------

public record ScreenerItemDto(
    string Symbol, string Name, string? Industry,
    double ClosePrice, double? PeTtm, double? PbMrq, double? MarketCap,
    DateTime? ReportDate, double? Roe, double? NetMargin, double? GrossMargin,
    double? RevenueYoY, double? NetProfitYoY);

public record ScreenerResponse(int Total, int Page, int PageSize, List<ScreenerItemDto> Items);

// ---------- 个股浏览 ----------

/// <summary>个股列表项。IndexWeightPct 为上证指数权重占比（%），非沪市股票为 null。</summary>
public record StockListItemDto(
    string Symbol, string Name, string? Industry, string Board,
    double ClosePrice, double? ChangePct, double? PeTtm, double? PbMrq,
    double? MarketCap, double? Roe, double? RevenueYoY, double? NetProfitYoY,
    double? IndexWeightPct);

public record StockListResponse(int Total, int Page, int PageSize, List<StockListItemDto> Items);

/// <summary>个股筛选条件（区间筛选，均可选；比率为 %，市值单位亿元）。</summary>
public record StockFilterQuery(
    double? WeightMin = null, double? WeightMax = null,
    double? ChangeMin = null, double? ChangeMax = null,
    double? PeMin = null, double? PeMax = null,
    double? PbMin = null, double? PbMax = null,
    double? RoeMin = null, double? RoeMax = null,
    double? RevenueMin = null, double? RevenueMax = null,
    double? ProfitMin = null, double? ProfitMax = null,
    double? CapMin = null, double? CapMax = null);

/// <summary>个股单期财务摘要（金额单位：亿元，比率为 %）。</summary>
public record FinancialReportDto(
    string Symbol, string Name, string ReportDate, string ReportType,
    double? Eps, double? Bps, double? Roe,
    double? Revenue, double? RevenueYoY, double? NetProfit, double? NetProfitYoY,
    double? GrossMargin, double? NetMargin, double? DebtRatio, double? OcfPerShare);

public record BoardStatDto(string Key, string Name, int Count);

/// <summary>沪深港通（北上/南下）资金。单位：亿元。</summary>
public record HsgtPointDto(string Date, double NorthAmount, double SouthNetInflow, double SouthAmount);

public record HsgtFlowDto(
    /// <summary>北向今日成交额（净流入自 2024-08 起交易所停止披露）。</summary>
    double NorthTodayAmount,
    double NorthYesterdayAmount,
    double? NorthAmountChangePct,
    /// <summary>南向今日净流入（负值为净流出）。</summary>
    double SouthTodayNetInflow,
    double SouthYesterdayNetInflow,
    List<HsgtPointDto> Points);

public record LiquidityCardDto(string Label, string Value, string? Sub, double? TrendPct);

public record ShiborPointDto(string Date, double On, double W1, double W2, double M1);

public record ShiborResponse(List<ShiborPointDto> Points);

public record MoneySupplyPointDto(string Month, double M2, double M1, double M0, double M2YoY, double M1YoY, double M0YoY);

public record MoneySupplyResponse(List<MoneySupplyPointDto> Points);

public record ReserveRatioItemDto(string Date, double Large, double Small, double? Change);

public record ReserveRatioResponse(List<ReserveRatioItemDto> Items);

public record LprItemDto(string Date, double Lpr1Y, double Lpr5Y);

public record LprResponse(List<LprItemDto> Items);

/// <summary>单笔逆回购操作：开始=操作日，结束=到期日，Amount=投放量（亿元）。</summary>
public record OmoItemDto(
    string Date, string MaturityDate, string Term, int TermDays,
    double Amount, double? Rate, string Announcement);

public record OmoResponse(List<OmoItemDto> Items);

public record LiquidityOverviewDto(
    string LatestShiborDate,
    double ShiborOn, double ShiborOnChangeBp,
    double Shibor1W, double Shibor1WChangeBp,
    string LatestSupplyMonth,
    double M2YoY, double M1YoY, double M0YoY,
    double Lpr1Y, double Lpr5Y, string LatestLprDate,
    double ReserveLarge, double ReserveSmall, string LatestReserveDate);

// ---------- 市场情绪 ----------

/// <summary>单个交易日的市场情绪快照。金额单位：亿元，涨跌幅单位：%。</summary>
public record SentimentSnapshotDto(
    string Date,
    int UpCount, int DownCount, int FlatCount,
    int Up5Count, int Down5Count,
    int LimitUpCount, int LimitDownCount, int BrokenBoardCount, int MaxBoardStreak,
    double TotalAmount, double AvgChangePct, double MedianChangePct,
    double SentimentScore, string Level, string Advice,
    string? BasisDate,
    /// <summary>与上一交易日相比的情绪分变化。</summary>
    double? ScoreChange,
    /// <summary>与上一交易日相比的成交额变化（%）。</summary>
    double? AmountChangePct);

public record SentimentOverviewDto(SentimentSnapshotDto? Latest);

/// <summary>情绪历史趋势点。</summary>
public record SentimentHistoryPointDto(
    string Date, double SentimentScore,
    int UpCount, int DownCount, int FlatCount,
    int LimitUpCount, int LimitDownCount, int BrokenBoardCount, int MaxBoardStreak,
    double TotalAmount, double AvgChangePct);

public record SentimentHistoryDto(List<SentimentHistoryPointDto> Points);

/// <summary>行业冷暖：平均涨跌幅、上涨占比与涨停/跌停家数。</summary>
public record SentimentIndustryDto(
    string Industry, int Count, double AvgChangePct, double UpRatio,
    int LimitUpCount, int LimitDownCount);

/// <summary>全市场涨跌幅分布分桶。</summary>
public record SentimentBucketDto(string Label, int Count);

public record SentimentStructureDto(
    string? BasisDate,
    int TotalStocks,
    List<SentimentIndustryDto> TopIndustries,
    List<SentimentIndustryDto> BottomIndustries,
    List<SentimentBucketDto> Distribution);
