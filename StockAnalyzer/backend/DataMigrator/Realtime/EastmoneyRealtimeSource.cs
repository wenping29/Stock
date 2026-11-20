using System.Globalization;
using System.Text.Json;

namespace DataMigrator.Realtime;

/// <summary>
/// 东方财富 clist 全市场行情抓取（对齐 patch fetcher.fetchRealtimeQuotes）：
/// 每页 5000 条，主域名失败时切到 push2delay。
/// </summary>
public sealed class EastmoneyRealtimeSource(IHttpClientFactory httpFactory, ILogger<EastmoneyRealtimeSource> logger)
{
    private const string Fs = "m:0+t:6,m:0+t:80,m:1+t:2,m:1+t:23,m:0+t:81+s:2048";
    private const string Fields = "f12,f14,f2,f3,f5,f6,f15,f16";
    private const string Ut = "bd1d9ddb04089700cf9c27f6f7426281";
    private const int PageSize = 5000;
    private const int MaxPages = 80;

    private static readonly string[] BaseUrls =
    [
        "https://push2.eastmoney.com",
        "https://push2delay.eastmoney.com",
    ];

    public async Task<List<RealtimeQuote>> FetchAllAsync(CancellationToken ct = default)
    {
        var client = httpFactory.CreateClient("eastmoney");
        var quotes = new List<RealtimeQuote>();
        var total = 0;
        var primaryDown = false;
        var consecutiveEmpty = 0;

        for (var page = 1; page <= MaxPages; page++)
        {
            JsonElement? items = null;
            foreach (var baseUrl in BaseUrls)
            {
                if (baseUrl == BaseUrls[0] && primaryDown) continue;
                try
                {
                    using var resp = await client.GetAsync(BuildUrl(baseUrl, page), ct);
                    resp.EnsureSuccessStatusCode();
                    using var doc = await JsonDocument.ParseAsync(
                        await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                        continue;

                    if (data.TryGetProperty("total", out var totalEl) && totalEl.TryGetInt32(out var t))
                        total = t;
                    if (data.TryGetProperty("diff", out var diff))
                    {
                        items = diff.Clone();
                        break;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning("实时行情第 {Page} 页请求失败（{Host}）：{Message}", page, baseUrl, ex.Message);
                    if (baseUrl == BaseUrls[0]) primaryDown = true;
                }
            }

            if (items is null)
            {
                if (++consecutiveEmpty >= 3)
                {
                    logger.LogError("实时行情连续 {Count} 页为空，中止本次抓取", consecutiveEmpty);
                    break;
                }
                continue;
            }
            consecutiveEmpty = 0;

            foreach (var el in EnumerateDiff(items.Value))
                quotes.Add(Parse(el));

            if (total > 0 && quotes.Count >= total) break;
        }

        return quotes;
    }

    private static IEnumerable<JsonElement> EnumerateDiff(JsonElement diff)
        => diff.ValueKind switch
        {
            JsonValueKind.Array => diff.EnumerateArray(),
            JsonValueKind.Object => diff.EnumerateObject().Select(p => p.Value),
            _ => [],
        };

    private static RealtimeQuote Parse(JsonElement el)
    {
        var code = Str(el, "f12");
        return new RealtimeQuote(
            code,
            Str(el, "f14"),
            MarketOf(code),
            Num(el, "f2"),
            Num(el, "f3"),
            Num(el, "f5"),
            Num(el, "f6"),
            Num(el, "f15"),
            Num(el, "f16"));
    }

    private static string BuildUrl(string baseUrl, int page)
    {
        var query = string.Join('&',
            $"pn={page}",
            $"pz={PageSize}",
            "po=1",
            "np=1",
            "fltt=2",
            "invt=2",
            "fid=f2",
            $"fs={Uri.EscapeDataString(Fs)}",
            $"fields={Fields}",
            $"ut={Ut}");
        return $"{baseUrl}/api/qt/clist/get?{query}";
    }

    private static string Str(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) ? v.ToString() : "";

    private static double Num(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String => double.TryParse(v.GetString(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var d) ? d : 0,
            _ => 0,
        };
    }

    private static string MarketOf(string code)
    {
        if (code.StartsWith('6') || code.StartsWith("900")) return "SH";
        if (code.StartsWith('0') || code.StartsWith('3') || code.StartsWith("200")) return "SZ";
        if (code.StartsWith("920") || code.StartsWith('8')) return "BJ";
        if (code.StartsWith('4')) return "NEEQ";
        return "其他";
    }
}
