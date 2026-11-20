using FundAnalysis.Services;

namespace FundAnalysis.Tests;

/// <summary>
/// 多期财务摘要解析测试：东财 F10 主要财务指标响应 → StockFinancialReport（元转亿元、空值保留 null）。
/// </summary>
public class FinancialReportTests
{
    [Fact]
    public void 解析多期财报_字段映射与单位换算正确()
    {
        var rows = FinancialReportService.ParseReports(SampleResponse, "600519");

        // 响应内两条同报告期记录，去重后保留最新在前
        Assert.Equal(2, rows.Count);
        Assert.Equal(new DateTime(2025, 9, 30), rows[0].ReportDate);
        Assert.Equal("三季报", rows[0].ReportType);

        var latest = rows[0];
        Assert.Equal("600519", latest.Symbol);
        Assert.Equal("贵州茅台", latest.Name);
        Assert.Equal(51.53, latest.Eps);
        Assert.Equal(205.0, latest.Bps);
        Assert.Equal(24.5, latest.Roe);
        Assert.Equal(1309.0, latest.Revenue);       // 1309 亿
        Assert.Equal(646.0, latest.NetProfit);      // 646 亿
        Assert.Equal(6.32, latest.RevenueYoY);
        Assert.Equal(6.2, latest.NetProfitYoY);
        Assert.Equal(91.3, latest.GrossMargin);
        Assert.Equal(49.4, latest.NetMargin);
        Assert.Equal(15.2, latest.DebtRatio);
        Assert.Equal(40.1, latest.OcfPerShare);

        // 原始数值字段完整留存（含未映射字段）
        Assert.Contains("\"TOTALOPERATEREVE\"", latest.DataJson);
        Assert.Contains("\"TOTAL_ASSETS_PK\"", latest.DataJson);
    }

    [Fact]
    public void 解析多期财报_空字段保留null()
    {
        var rows = FinancialReportService.ParseReports(SampleResponse, "600519");

        var mid = rows[1];
        Assert.Equal(new DateTime(2025, 6, 30), mid.ReportDate);
        Assert.Null(mid.Eps);
        Assert.Null(mid.Revenue);
        Assert.Null(mid.NetProfit);
    }

    [Theory]
    [InlineData("600519", "600519.SH")]
    [InlineData("000001", "000001.SZ")]
    [InlineData("300750", "300750.SZ")]
    [InlineData("688981", "688981.SH")]
    [InlineData("430047", "430047.BJ")]
    [InlineData("832000", "832000.BJ")]
    [InlineData("920001", "920001.BJ")]
    public void SecuCode_按市场补后缀(string symbol, string expected)
        => Assert.Equal(expected, FinancialReportService.ToSecuCode(symbol));

    private const string SampleResponse = """
    {
      "result": {
        "pages": 1,
        "data": [
          {
            "SECUCODE": "600519.SH",
            "SECURITY_CODE": "600519",
            "SECURITY_NAME_ABBR": "贵州茅台",
            "REPORT_DATE": "2025-09-30 00:00:00",
            "REPORT_TYPE": "三季报",
            "EPSJB": 51.53,
            "BPS": 205.0,
            "ROEJQ": 24.5,
            "TOTALOPERATEREVE": 130900000000,
            "TOTALOPERATEREVETZ": 6.32,
            "PARENTNETPROFIT": 64600000000,
            "PARENTNETPROFITTZ": 6.2,
            "XSMLL": 91.3,
            "XSJLL": 49.4,
            "ZCFZL": 15.2,
            "MGJYXJJE": 40.1,
            "TOTAL_ASSETS_PK": 300000000000
          },
          {
            "SECUCODE": "600519.SH",
            "SECURITY_CODE": "600519",
            "SECURITY_NAME_ABBR": "贵州茅台",
            "REPORT_DATE": "2025-09-30 00:00:00",
            "REPORT_TYPE": "三季报",
            "EPSJB": 51.53,
            "TOTALOPERATEREVE": 130900000000
          },
          {
            "SECUCODE": "600519.SH",
            "SECURITY_CODE": "600519",
            "SECURITY_NAME_ABBR": "贵州茅台",
            "REPORT_DATE": "2025-06-30 00:00:00",
            "REPORT_TYPE": "中报",
            "EPSJB": null,
            "TOTALOPERATEREVE": null,
            "PARENTNETPROFIT": null
          }
        ],
        "count": 3
      },
      "success": true,
      "message": "ok",
      "code": 0
    }
    """;
}