using System.Net;

namespace FundAnalysis.Services.Providers;

/// <summary>
/// 东财行情 HttpClient 的统一注册。
/// 出口被东财风控（ResponseEnded / Empty reply）时，可通过代理绕行：
///   配置项 Eastmoney:ProxyUrl，或环境变量 Eastmoney__ProxyUrl / EASTMONEY_PROXY。
/// 为空则直连（沿用系统默认代理），支持 http://user:pass@host:port 形式。
/// </summary>
public static class EastmoneyHttpClientExtensions
{
    public static IHttpClientBuilder AddEastmoneyHttpClient(
        this IServiceCollection services, IConfiguration config, int maxConnectionsPerServer = 8)
    {
        var proxyUrl = ResolveProxy(config);

        return services.AddHttpClient("eastmoney", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                var handler = new SocketsHttpHandler
                {
                    AutomaticDecompression = DecompressionMethods.All,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                    MaxConnectionsPerServer = maxConnectionsPerServer,
                };
                if (!string.IsNullOrWhiteSpace(proxyUrl))
                    handler.Proxy = new WebProxy(proxyUrl);
                return handler;
            });
    }

    private static string? ResolveProxy(IConfiguration config)
        => (config["Eastmoney:ProxyUrl"] ?? Environment.GetEnvironmentVariable("EASTMONEY_PROXY"))?.Trim();
}
