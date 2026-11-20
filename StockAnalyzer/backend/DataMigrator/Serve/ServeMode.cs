using System.Net;
using DataMigrator.Monitor;
using DataMigrator.Realtime;
using FundAnalysis.Analysis;
using FundAnalysis.Data;
using FundAnalysis.Services.Providers;
using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DataMigrator.Serve;

/// <summary>
/// --serve：实时行情服务（全市场抓取 + 内存缓存 + HTTP），取代 stock-analyzer/patch 的常驻服务。
/// 默认监听 3012（可用 --port 覆盖），接口与 patch 保持同构：
///   GET  /health
///   GET  /api/quote/all
///   GET  /api/quote/{code}
///   POST /api/quote/refresh
/// 追加 --monitor 时启用数据库中的监控任务 cron（默认关闭，避免与 Web 端 Hangfire 重复推送）。
/// </summary>
public static class ServeMode
{
    public const string DefaultConnection = "Host=43.142.147.244;Port=5454;Database=fund_db;Username=fundmanager;Password=admin123456";

    public static async Task<int> RunAsync(string[] args, string pgConnection)
    {
        var port = GetOption(args, "--port") ?? Environment.GetEnvironmentVariable("REALTIME_PORT") ?? "3012";
        var withMonitor = args.Contains("--monitor");
        var connection = GetOption(args, "--conn") ?? pgConnection;

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        var config = builder.Configuration;
        if (string.IsNullOrWhiteSpace(config.GetConnectionString("Default")))
            config["ConnectionStrings:Default"] = connection;
        if (string.IsNullOrWhiteSpace(config["Database:Provider"]))
            config["Database:Provider"] = "postgresql";

        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
        // 常驻服务：屏蔽每次抓取的 HttpClient 逐请求日志，失败仍有告警
        builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

        var services = builder.Services;
        services.AddCors(o => o.AddPolicy("all", p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        services.AddHttpClient("eastmoney", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
                client.DefaultRequestHeaders.Referrer = new Uri("https://quote.eastmoney.com/");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = 32,
            });

        services.AddSingleton<EastmoneyRealtimeSource>();
        services.AddSingleton<RealtimeQuoteCache>();
        services.AddHostedService<RealtimeRefreshWorker>();

        if (withMonitor)
        {
            services.AddDbContext<AppDbContext>(opt => opt.UseDatabase(config));
            services.AddHttpClient("webhook", client => client.Timeout = TimeSpan.FromSeconds(10));
            services.AddScoped<EastmoneyProvider>();
            services.AddScoped<IQuoteProvider>(sp => sp.GetRequiredService<EastmoneyProvider>());
            services.AddScoped<AnalysisDataService>();
            services.AddSingleton<IRecurringJobManager, NoopRecurringJobManager>();
            services.AddSingleton<IBackgroundJobClient, NoopBackgroundJobClient>();
            services.AddScoped<MonitoringService>();
            services.AddSingleton<MonitorCronWorker>();
            services.AddHostedService(sp => sp.GetRequiredService<MonitorCronWorker>());
        }

        var app = builder.Build();
        app.UseCors("all");

        app.MapGet("/health", async (RealtimeQuoteCache cache, HttpContext ctx) =>
        {
            object monitor = Array.Empty<object>();
            if (withMonitor)
                monitor = await ctx.RequestServices.GetRequiredService<MonitorCronWorker>().GetJobsAsync(ctx.RequestAborted);
            return Results.Json(new { success = true, realtime = cache.Status, monitor });
        });

        app.MapGet("/api/quote/all", (RealtimeQuoteCache cache) =>
            Results.Json(new { success = true, data = cache.GetAll(), meta = cache.Status }));

        app.MapGet("/api/quote/{code}", (string code, RealtimeQuoteCache cache) =>
        {
            var quote = cache.Get(code);
            return quote is null
                ? Results.Json(new { success = false, error = $"no quote for {code}" }, statusCode: 404)
                : Results.Json(new { success = true, data = quote });
        });

        app.MapPost("/api/quote/refresh", async (RealtimeQuoteCache cache) =>
        {
            try
            {
                var count = await cache.RefreshAsync();
                return Results.Json(new { success = true, data = new { count }, meta = cache.Status });
            }
            catch (Exception ex)
            {
                return Results.Json(new { success = false, error = ex.Message }, statusCode: 500);
            }
        });

        app.Logger.LogInformation(
            "serve 模式启动：实时行情每 {Seconds}s 刷新，监控 cron {Monitor}",
            config.GetValue("Realtime:RefreshSeconds", 60),
            withMonitor ? "已启用" : "未启用（--monitor 开启）");
        await app.RunAsync();
        return 0;
    }

    private static string? GetOption(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
