using FundAnalysis.Services;
using Xunit;

namespace FundAnalysis.Tests;

public class FredCsvTests
{
    [Fact]
    public void ParseFredCsv_SkipsHeaderMissingAndInvalidRows()
    {
        const string csv =
            "observation_date,SOFR\r\n" +
            "2026-11-17,3.92\r\n" +
            "2026-11-18,3.90\r\n" +
            "2026-11-19,\r\n" +
            "2026-11-20,.\r\n" +
            "not-a-csv-line\r\n";

        var rows = CollectorService.ParseFredCsv(csv);

        Assert.Equal(2, rows.Count);
        Assert.Equal(new DateTime(2026, 11, 17), rows[0].Date);
        Assert.Equal(3.92, rows[0].Value);
        Assert.Equal(new DateTime(2026, 11, 18), rows[1].Date);
        Assert.Equal(3.90, rows[1].Value);
    }

    [Fact]
    public void ParseFredCsv_EmptyInput()
        => Assert.Empty(CollectorService.ParseFredCsv(""));
}