// SQLite → PostgreSQL 数据搬迁工具
// 用法（在 backend/DataMigrator 下）:
//   dotnet run -- --source "Data Source=../FundAnalysis/fundanalysis.db" \
//                --target "Host=...;Port=5454;Database=fund_analysis;Username=postgres;Password=xxx"
//   dotnet run -- --diag   （参数绑定诊断）
using System.Data;
using System.Text;
using DataMigrator.Serve;
using Microsoft.Data.Sqlite;
using Npgsql;
using NpgsqlTypes;

// 与主应用一致的 PG 时间行为（本地时间直写）
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

// --serve：实时行情常驻服务（取代 stock-analyzer/patch）；追加 --monitor 时启用监控 cron
if (args.Contains("--serve"))
    return await ServeMode.RunAsync(args, ServeMode.DefaultConnection);

// --syncseq：迁移后重置全部 IDENTITY 序列到当前最大 Id（否则应用插入新行会主键冲突）
if (args.Contains("--syncseq"))
{
    var seqConn = new NpgsqlConnection("Host=43.142.147.244;Port=5454;Database=fund_db;Username=fundmanager;Password=admin123456");
    await seqConn.OpenAsync();
    await using var findCmd = new NpgsqlCommand(
        @"SELECT c.table_name, c.column_name
          FROM information_schema.columns c
          JOIN information_schema.tables t ON t.table_name = c.table_name AND t.table_schema='public'
          WHERE c.table_schema='public' AND c.is_identity = 'YES'", seqConn);
    await using var fr = await findCmd.ExecuteReaderAsync();
    var idCols = new List<(string Table, string Col)>();
    while (await fr.ReadAsync()) idCols.Add((fr.GetString(0), fr.GetString(1)));
    fr.Close();

    foreach (var (t, c) in idCols)
    {
        try
        {
            var sql = $"SELECT setval(pg_get_serial_sequence('\"{t}\"', '{c}'), COALESCE((SELECT MAX(\"{c}\") FROM \"{t}\"), 0) + 1, false)";
            await using var fix = new NpgsqlCommand(sql, seqConn);
            var v = await fix.ExecuteScalarAsync();
            Console.WriteLine($"{t,-22} 序列已推进到 {v}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{t,-22} 序列推进失败: {ex.Message}");
        }
    }
    return 0;
}

// --fixfund：存量基金数据统一加 ".F" 后缀（净值/自选，与基金清单同步一致）
if (args.Contains("--fixfund"))
{
    var fconn2 = new NpgsqlConnection("Host=43.142.147.244;Port=5454;Database=fund_db;Username=fundmanager;Password=admin123456");
    await fconn2.OpenAsync();
    await using var u1 = new NpgsqlCommand(
        @"UPDATE ""FundNavs"" SET ""FundCode"" = ""FundCode"" || '.F' WHERE ""FundCode"" NOT LIKE '%.F'", fconn2);
    Console.WriteLine($"FundNavs 更新 {await u1.ExecuteNonQueryAsync()} 行");
    await using var u2 = new NpgsqlCommand(
        @"UPDATE ""WatchlistItems"" SET ""Symbol"" = ""Symbol"" || '.F' WHERE ""Symbol"" NOT LIKE '%.F'
          AND ""Symbol"" IN (SELECT ""Symbol"" FROM ""StockBasics"" WHERE ""Type"" = 'Fund')", fconn2);
    Console.WriteLine($"WatchlistItems 更新 {await u2.ExecuteNonQueryAsync()} 行");
    return 0;
}

// --codes：检查 IndexDailies 指数代码分布
if (args.Contains("--codes"))
{
    var cconn = new NpgsqlConnection("Host=43.142.147.244;Port=5454;Database=fund_db;Username=fundmanager;Password=admin123456");
    await cconn.OpenAsync();
    var ccmd = new NpgsqlCommand("SELECT \"Code\", COUNT(*) FROM \"IndexDailies\" GROUP BY \"Code\"", cconn);
    await using (var cr = await ccmd.ExecuteReaderAsync())
    {
        while (await cr.ReadAsync())
            Console.WriteLine($"{cr.GetString(0)}  {cr.GetInt64(1)} 根");
    }
    await using (var c2 = new NpgsqlCommand("SELECT COUNT(*) FROM \"IndexDailies\" WHERE \"Code\"='399106'", cconn))
        Console.WriteLine($"399106 精确查询: {await c2.ExecuteScalarAsync()} 行");
    // 模拟应用的成交量查询
    await using (var c3 = new NpgsqlCommand(
        "SELECT COUNT(*), COUNT(DISTINCT \"Date\") FROM \"IndexDailies\" WHERE \"Code\" IN ('000001', '399106')", cconn))
    await using (var r3 = await c3.ExecuteReaderAsync())
    {
        if (await r3.ReadAsync())
            Console.WriteLine($"应用查询(000001+399106): {r3.GetInt64(0)} 行 / {r3.GetInt64(1)} 个不同日期");
    }
    // 看日期边界
    await using (var c4 = new NpgsqlCommand(
        "SELECT \"Code\", \"Date\"::text FROM \"IndexDailies\" ORDER BY \"Date\" DESC LIMIT 3", cconn))
    await using (var r4 = await c4.ExecuteReaderAsync())
    {
        while (await r4.ReadAsync())
            Console.WriteLine($"最新: {r4.GetString(0)} @ {r4.GetString(1)}");
    }
    return 0;
}

// --seed399：向 fund_db 插入 399106 深证综指 30 根测试日线（成交量联调用）
if (args.Contains("--seed399"))
{
    var sconn = new NpgsqlConnection("Host=43.142.147.244;Port=5454;Database=fund_db;Username=fundmanager;Password=admin123456");
    await sconn.OpenAsync();
    await using var tx = await sconn.BeginTransactionAsync();
    var added = 0;
    for (var i = 0; i < 30; i++)
    {
        var date = DateTime.Parse("2026-09-10").AddBusinessDaysLike(i);
        await using var c = new NpgsqlCommand(
            @"INSERT INTO ""IndexDailies"" (""Code"",""Market"",""Date"",""Open"",""High"",""Low"",""Close"",""Volume"",""Amount"",""ChangePct"")
              VALUES ('399106',0,@d,2200,2260,2180,2230,@v,@a,0.5) ON CONFLICT DO NOTHING", sconn, tx);
        c.Parameters.AddWithValue("d", date);
        c.Parameters.AddWithValue("v", 400000000.0 + i * 10000000);
        c.Parameters.AddWithValue("a", 800000000000.0 + i * 20000000000);
        added += await c.ExecuteNonQueryAsync();
    }
    await using (var idx = new NpgsqlCommand(
        "SELECT indexname, indexdef FROM pg_indexes WHERE tablename='IndexDailies'", sconn))
    await using (var ir = await idx.ExecuteReaderAsync())
    {
        while (await ir.ReadAsync())
            Console.WriteLine($"索引: {ir.GetString(1)}");
    }

    await using (var test = new NpgsqlCommand(
        @"INSERT INTO ""IndexDailies"" (""Code"",""Market"",""Date"",""Open"",""High"",""Low"",""Close"",""Volume"",""Amount"",""ChangePct"")
          VALUES ('399106',0,'2026-10-23',2200,2260,2180,2230,400000000,800000000000,0.5)", sconn, tx))
    {
        var n = await test.ExecuteNonQueryAsync();
        Console.WriteLine($"直接插入(无ON CONFLICT): {n} 行");
    }
    await tx.RollbackAsync(); // 测试数据回滚，不污染
    Console.WriteLine("已回滚");
    return 0;
}

// --fixhistory：把本地全部迁移 ID 补写进 __EFMigrationsHistory（表结构已通过 SQL 手动达到最新状态时用）
if (args.Contains("--fixhistory"))
{
    var migDir = Path.Combine(Environment.CurrentDirectory, "../FundAnalysis.Core/Migrations");
    var ids = Directory.GetFiles(migDir, "*_Init.cs")
        .Concat(Directory.GetFiles(migDir, "*.cs").Where(f => !f.EndsWith("Init.cs") && !f.EndsWith("Designer.cs") && !f.EndsWith("Snapshot.cs")))
        .Select(Path.GetFileNameWithoutExtension)
        .Where(n => n != "AppDbContextModelSnapshot")
        .Distinct()
        .ToList();
    var fconn = new NpgsqlConnection("Host=43.142.147.244;Port=5454;Database=fund_db;Username=fundmanager;Password=admin123456");
    await fconn.OpenAsync();
    await using var hc = new NpgsqlCommand(
        @"CREATE TABLE IF NOT EXISTS ""__EFMigrationsHistory"" (""MigrationId"" text NOT NULL CONSTRAINT ""PK___EFMigrationsHistory"" PRIMARY KEY, ""ProductVersion"" text NOT NULL)", fconn);
    await hc.ExecuteNonQueryAsync();
    foreach (var id in ids)
    {
        await using var ic = new NpgsqlCommand(
            @"INSERT INTO ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"") VALUES (@id, '10.0.13') ON CONFLICT DO NOTHING", fconn);
        ic.Parameters.AddWithValue("id", id);
        await ic.ExecuteNonQueryAsync();
        Console.WriteLine($"历史已标记: {id}");
    }
    return 0;
}

// --addindustry：给 StockBasics 补 Industry 列（旧库升级）
if (args.Contains("--addindustry"))
{
    var aconn = new NpgsqlConnection("Host=43.142.147.244;Port=5454;Database=fund_db;Username=fundmanager;Password=admin123456");
    await aconn.OpenAsync();
    await using var ac = new NpgsqlCommand(
        @"ALTER TABLE ""StockBasics"" ADD COLUMN IF NOT EXISTS ""Industry"" text", aconn);
    await ac.ExecuteNonQueryAsync();
    Console.WriteLine("StockBasics.Industry 列已就绪");
    return 0;
}

if (args.Contains("--list"))
{
    var lconn = new NpgsqlConnection("Host=43.142.147.244;Port=5454;Database=fund_db;Username=fundmanager;Password=admin123456");
    await lconn.OpenAsync();
    var lcmd = new NpgsqlCommand("SELECT table_name FROM information_schema.tables WHERE table_schema='public' ORDER BY table_name", lconn);
    await using var lr = await lcmd.ExecuteReaderAsync();
    var names = new List<string>();
    while (await lr.ReadAsync()) names.Add(lr.GetString(0));
    Console.WriteLine($"PG 库共 {names.Count} 张表:\n{string.Join(", ", names)}");
    return 0;
}

// --history：只补写迁移历史标记（表已由 SQL 建好时用）
if (args.Contains("--history"))
{
    var hconn = new NpgsqlConnection("Host=43.142.147.244;Port=5454;Database=fund_db;Username=fundmanager;Password=admin123456");
    await hconn.OpenAsync();
    await using var hc = new NpgsqlCommand(
        @"CREATE TABLE IF NOT EXISTS ""__EFMigrationsHistory"" (""MigrationId"" text NOT NULL CONSTRAINT ""PK___EFMigrationsHistory"" PRIMARY KEY, ""ProductVersion"" text NOT NULL);
          INSERT INTO ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"") VALUES ('20261024041644_Init', '10.0.13') ON CONFLICT DO NOTHING;", hconn);
    await hc.ExecuteNonQueryAsync();
    Console.WriteLine("迁移历史已写入");
    return 0;
}

// --init：执行建表 SQL（deploy/init-schema-postgresql.sql）在空库中创建全部表结构
if (args.Contains("--init"))
{
    var schemaPath = Path.Combine(Environment.CurrentDirectory, "../../FundAnalysis/deploy/init-schema-postgresql.sql");
    if (!File.Exists(schemaPath)) schemaPath = Path.Combine(Environment.CurrentDirectory, "init-schema-postgresql.sql");
    var iconn = new NpgsqlConnection("Host=43.142.147.244;Port=5454;Database=fund_db;Username=fundmanager;Password=admin123456");
    await iconn.OpenAsync();
    var script = await File.ReadAllTextAsync(schemaPath);
    var statements = script.Split(";\n", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    var done = 0;
    foreach (var stmt in statements)
    {
        if (string.IsNullOrWhiteSpace(stmt)) continue;
        await using var c = new NpgsqlCommand(stmt, iconn);
        await c.ExecuteNonQueryAsync();
        done++;
    }
    Console.WriteLine($"建表 SQL 执行完成，共 {done} 条语句");

    // 标记迁移已应用（避免应用启动时重复建表冲突）
    var migrationId = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.csproj").Length >= 0
        ? "20261024041644_Init" // 与 deploy/init-schema-postgresql.sql 对应的迁移版本
        : "";
    await using var hc = new NpgsqlCommand(
        @"CREATE TABLE IF NOT EXISTS ""__EFMigrationsHistory"" (""MigrationId"" text NOT NULL CONSTRAINT ""PK___EFMigrationsHistory"" PRIMARY KEY, ""ProductVersion"" text NOT NULL);
          INSERT INTO ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"") VALUES (@mid, '10.0.13') ON CONFLICT DO NOTHING;", iconn);
    hc.Parameters.AddWithValue("mid", migrationId);
    await hc.ExecuteNonQueryAsync();
    Console.WriteLine($"已写入迁移历史: {migrationId}");
    return 0;
}

if (args.Contains("--diag"))
{
    var dconn = new NpgsqlConnection("Host=43.142.147.244;Port=5454;Database=fund_db;Username=fundmanager;Password=admin123456");
    await dconn.OpenAsync();
    var c1 = new NpgsqlCommand("SELECT $1::text AS v", dconn);
    c1.Parameters.Add(new NpgsqlParameter { ParameterName = "1", Value = "hello" });
    try { Console.WriteLine("位置参数($1): " + await c1.ExecuteScalarAsync()); }
    catch (Exception ex) { Console.WriteLine("位置参数失败: " + ex.Message); }
    var c2 = new NpgsqlCommand("SELECT @p::text AS v", dconn);
    c2.Parameters.AddWithValue("p", "world");
    try { Console.WriteLine("命名参数(@p): " + await c2.ExecuteScalarAsync()); }
    catch (Exception ex) { Console.WriteLine("命名参数失败: " + ex.Message); }
    return 0;
}

// --import-node：把 stock-analyzer/server 的 SQLite 数据导入 fund_db（量化分析模块）
//   dotnet run -- --import-node [--node-db "D:\Project\Stock\stock-analyzer\server\data\stock_data.db"]
if (args.Contains("--import-node"))
{
    string? ValueOf(string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == name && i + 1 < args.Length) return args[i + 1];
            if (args[i].StartsWith(name + "=", StringComparison.Ordinal)) return args[i][(name.Length + 1)..];
        }
        return null;
    }

    var nodeDb = ValueOf("--node-db") ?? @"D:\Project\Stock\stock-analyzer\server\data\stock_data.db";
    var importTarget = ValueOf("--target") ?? "Host=43.142.147.244;Port=5454;Database=fund_db;Username=fundmanager;Password=admin123456";
    if (!File.Exists(nodeDb)) throw new FileNotFoundException("找不到 Node 数据库文件", nodeDb);
    Console.WriteLine($"源: {nodeDb}");
    Console.WriteLine($"目标: {importTarget}");
    var rc = await NodeImporter.RunAsync(nodeDb, importTarget, onlyStocks: args.Contains("--only-stocks"));
    return rc;
}

var argsMap = new Dictionary<string, string>();
for (var i = 0; i < args.Length - 1; i += 2)
    argsMap[args[i].TrimStart('-')] = args[i + 1];

var source = argsMap.GetValueOrDefault("source", "Data Source=../FundAnalysis/fundanalysis.db");
var target = argsMap.GetValueOrDefault("target")
    ?? throw new ArgumentException("缺少 --target 连接串");

// 迁移顺序：先主/引用表后子表（表名 = EF 建的实际复数表名）
var tables = new[]
{
    "AspNetRoles", "AspNetUsers", "AspNetUserRoles", "AspNetUserClaims",
    "StockBasics", "AspNetUserLogins", "AspNetUserTokens",
    "StockDailies", "StockMinutes", "FundNavs", "MoneyFlowDailies",
    "WatchlistItems", "ShiborRates", "MoneySupplies", "ReserveRatios",
    "LprRates", "IndexDailies", "StockFundamentals", "AlertRules",
};

await using var src = new SqliteConnection(source);
await using var dst = new NpgsqlConnection(target);
await src.OpenAsync();
await dst.OpenAsync();

var totalRows = 0L;
foreach (var table in tables)
{
    var rows = await CopyTableAsync(src, dst, table);
    totalRows += rows;
    Console.WriteLine($"{table,-22} {rows,7} 行");
}

Console.WriteLine($"搬迁完成，共 {totalRows} 行");

static async Task<long> CopyTableAsync(SqliteConnection src, NpgsqlConnection dst, string table)
{
    // 目标列定义（名称+类型，按序）
    var cols = new List<(string Name, string DataType)>();
    await using (var cmd = new NpgsqlCommand(
        @"SELECT column_name, data_type FROM information_schema.columns
          WHERE table_schema='public' AND table_name=@t ORDER BY ordinal_position", dst))
    {
        cmd.Parameters.AddWithValue("t", table);
        await using var colReader = await cmd.ExecuteReaderAsync();
        while (await colReader.ReadAsync())
            cols.Add((colReader.GetString(0), colReader.GetString(1)));
    }
    if (cols.Count == 0)
    {
        Console.WriteLine($"{table,-22} 目标表不存在，跳过");
        return 0;
    }

    // 源数据
    await using var select = new SqliteCommand($"SELECT * FROM \"{table}\"", src);
    await using var reader = await select.ExecuteReaderAsync();
    if (!reader.HasRows) return 0;

    var colNames = cols.Select(c => $"\"{c.Name}\"").ToList();
    var placeholders = cols.Select((c, i) => $"@c{i}").ToList();
    var insertSql = new StringBuilder($"INSERT INTO \"{table}\" ({string.Join(",", colNames)}) VALUES ({string.Join(",", placeholders)})");

    await using var tx = await dst.BeginTransactionAsync();
    long count = 0;
    try
    {
        await using (var del = new NpgsqlCommand($"DELETE FROM \"{table}\"", dst, tx))
            await del.ExecuteNonQueryAsync();

        await using var insert = new NpgsqlCommand(insertSql.ToString(), dst, tx);
        var parameters = cols.Select((c, i) =>
        {
            var p = new NpgsqlParameter { ParameterName = $"c{i}", NpgsqlDbType = MapDbType(c.DataType) };
            insert.Parameters.Add(p);
            return p;
        }).ToArray();

        while (await reader.ReadAsync())
        {
            for (var i = 0; i < cols.Count; i++)
            {
                var (name, dataType) = cols[i];
                var raw = reader[name] is DBNull ? null : reader[name];
                parameters[i].Value = ConvertFor(raw, dataType);
            }
            await insert.ExecuteNonQueryAsync();
            count++;
        }

        await tx.CommitAsync();
    }
    catch
    {
        await tx.RollbackAsync();
        throw;
    }
    return count;
}

// SQLite 动态类型 → PG 目标列类型
static object? ConvertFor(object? value, string pgType) => value switch
{
    null or DBNull => DBNull.Value,
    long n => pgType switch
    {
        "integer" => (int)n,
        "smallint" => (short)n,
        "bigint" => n,
        "double precision" or "real" => (double)n,
        "boolean" => n != 0,
        "timestamp with time zone" or "timestamp without time zone" or "date" => DateTime.FromBinary(n),
        "text" or "character varying" => n.ToString(),
        _ => n,
    },
    double d => pgType switch
    {
        "integer" => (int)d,
        "bigint" => (long)d,
        "double precision" or "real" => d,
        "text" => d.ToString(),
        _ => d,
    },
    string str => pgType.StartsWith("timestamp") || pgType == "date"
        ? DateTime.Parse(str)
        : str,
    _ => value,
};

// PG 列类型 → NpgsqlDbType（显式指定，null 值也可绑定）
static NpgsqlTypes.NpgsqlDbType MapDbType(string pgType) => pgType switch
{
    "bigint" => NpgsqlTypes.NpgsqlDbType.Bigint,
    "integer" => NpgsqlTypes.NpgsqlDbType.Integer,
    "smallint" => NpgsqlTypes.NpgsqlDbType.Smallint,
    "double precision" or "real" => NpgsqlTypes.NpgsqlDbType.Double,
    "numeric" => NpgsqlTypes.NpgsqlDbType.Numeric,
    "boolean" => NpgsqlTypes.NpgsqlDbType.Boolean,
    "timestamp with time zone" => NpgsqlTypes.NpgsqlDbType.TimestampTz,
    "timestamp without time zone" => NpgsqlTypes.NpgsqlDbType.Timestamp,
    "date" => NpgsqlTypes.NpgsqlDbType.Date,
    "jsonb" => NpgsqlTypes.NpgsqlDbType.Jsonb,
    _ => NpgsqlTypes.NpgsqlDbType.Text,
};

return 0;

internal static class BusinessDayExt
{
    public static DateTime AddBusinessDaysLike(this DateTime start, int days)
    {
        var d = start;
        var left = days;
        while (left > 0) { d = d.AddDays(1); if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) left--; }
        return d;
    }
}

/// <summary>stock-analyzer/server（Node/sql.js）→ 现有 fund_db 表结构的定向导入。</summary>
internal static class NodeImporter
{
    private static readonly string[] RequiredTables =
    [
        "StockBasics", "StockDailies", "IndexDailies", "ShiborRates", "MarketIndicators",
        "MarketSeriesPoints", "BacktestRecords", "BacktestTradeRecords", "MonitoringJobs", "MonitoringRuns",
    ];

    public static async Task<int> RunAsync(string nodeDbPath, string target, bool onlyStocks = false)
    {
        var started = DateTime.Now;
        await using var src = new SqliteConnection($"Data Source={nodeDbPath};Mode=ReadOnly");
        await using var dst = new NpgsqlConnection(target);
        await src.OpenAsync();
        await dst.OpenAsync();

        var missing = new List<string>();
        foreach (var table in RequiredTables)
            if (!await TableExistsAsync(dst, table)) missing.Add(table);
        if (missing.Count > 0)
        {
            Console.WriteLine($"目标库缺少表：{string.Join(", ", missing)}");
            Console.WriteLine("请先启动 FundAnalysis Web 端（会自动执行 EF 迁移），或执行 dotnet ef database update。");
            return 2;
        }

        Console.WriteLine("开始导入（K 线等重复主键保留目标库现有数据；股票列表以 Node 名称/行业为准）...");
        await ImportStockListAsync(src, dst);
        if (!onlyStocks)
        {
            await ImportKlinesAsync(src, dst);
            await ImportIndexPeriodsAsync(src, dst);
            await ImportUsRatesAsync(src, dst);
            await ImportCnRatesAsync(src, dst);
            await ImportMarketSeriesAsync(src, dst, "crude_oil", "crude_oil");
            await ImportMarketSeriesAsync(src, dst, "cn10y", "cn10y");
            await ImportMarketSeriesAsync(src, dst, "fx_cny_usd", "fx_cny_usd");
            await ImportMarketSeriesAsync(src, dst, "gold_price", "gold_price");
            await ImportBacktestsAsync(src, dst);
            await ImportMonitorAsync(src, dst);
        }

        Console.WriteLine($"导入完成，用时 {(DateTime.Now - started).TotalSeconds:F1}s");
        return 0;
    }

    private static async Task<bool> TableExistsAsync(NpgsqlConnection conn, string table)
    {
        await using var cmd = new NpgsqlCommand(
            @"SELECT 1 FROM information_schema.tables WHERE table_schema='public' AND table_name=@t", conn);
        cmd.Parameters.AddWithValue("t", table);
        return await cmd.ExecuteScalarAsync() is not null;
    }

    private static async Task<bool> SqliteTableExistsAsync(SqliteConnection conn, string table)
    {
        await using var cmd = new SqliteCommand(
            "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@t", conn);
        cmd.Parameters.AddWithValue("@t", table);
        return await cmd.ExecuteScalarAsync() is not null;
    }

    // ---------- 各数据域 ----------

    private static async Task ImportStockListAsync(SqliteConnection src, NpgsqlConnection dst)
    {
        var rows = new List<object?[]>();
        await using var cmd = new SqliteCommand(
            "SELECT code, name, market, industry FROM stock_list ORDER BY code", src);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var code = r.GetString(0).Trim();
            if (code.Length != 6) continue;
            var name = r.IsDBNull(1) ? code : r.GetString(1).Trim();
            var market = r.IsDBNull(2) ? "" : r.GetString(2).Trim().ToUpperInvariant();
            var industry = r.IsDBNull(3) ? null : r.GetString(3);
            rows.Add([code, name, market == "SH" ? 1 : 0, "Stock", industry, DateTime.Now]);
        }
        var n = await BatchInsertAsync(dst, "StockBasics",
            ["Symbol", "Name", "SecMarket", "Type", "Industry", "UpdatedAt"],
            [NpgsqlDbType.Text, NpgsqlDbType.Text, NpgsqlDbType.Integer, NpgsqlDbType.Text, NpgsqlDbType.Text, NpgsqlDbType.Timestamp],
            @"ON CONFLICT (""Symbol"") DO UPDATE SET
                ""Name"" = EXCLUDED.""Name"",
                ""Industry"" = COALESCE(NULLIF(""StockBasics"".""Industry"", ''), EXCLUDED.""Industry""),
                ""UpdatedAt"" = EXCLUDED.""UpdatedAt""", rows);
        Console.WriteLine($"{"StockBasics",-22}{n,10} 行写入");
    }

    private static async Task ImportKlinesAsync(SqliteConnection src, NpgsqlConnection dst)
    {
        var batch = new List<object?[]>(500);
        long read = 0, added = 0;
        string? currentCode = null;
        double prevClose = 0;

        await using var cmd = new SqliteCommand(
            "SELECT code, date, open, high, low, close, volume, amount FROM kline_data WHERE period='daily' ORDER BY code, date", src);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var code = r.GetString(0).Trim();
            if (code.Length != 6) continue;
            var close = Num(r, 5);
            if (code != currentCode)
            {
                currentCode = code;
                prevClose = 0;
            }
            var change = prevClose > 0 ? Math.Round((close / prevClose - 1) * 100, 4) : 0;
            var volume = Num(r, 6);
            var amount = Num(r, 7);
            // Node 主源新浪的 volume 为股，东财为手；按成交额/收盘价判断后统一为手
            if (volume > 0 && amount > 0 && close > 0 && amount / (volume * close) < 10)
                volume /= 100;

            batch.Add([
                code, ParseDate(r.GetString(1)), Num(r, 2), Num(r, 3), Num(r, 4), close, volume, amount, change,
            ]);
            prevClose = close;
            read++;

            if (batch.Count >= 500)
            {
                added += await FlushKlinesAsync(dst, batch);
                batch.Clear();
            }
        }
        if (batch.Count > 0) added += await FlushKlinesAsync(dst, batch);
        Console.WriteLine($"{"StockDailies",-22}{added,10} 行新增（源 {read} 行）");
    }

    private static Task<int> FlushKlinesAsync(NpgsqlConnection dst, List<object?[]> rows)
        => BatchInsertAsync(dst, "StockDailies",
            ["Symbol", "Date", "Open", "High", "Low", "Close", "Volume", "Amount", "ChangePct"],
            [NpgsqlDbType.Text, NpgsqlDbType.Timestamp, NpgsqlDbType.Double, NpgsqlDbType.Double, NpgsqlDbType.Double,
             NpgsqlDbType.Double, NpgsqlDbType.Double, NpgsqlDbType.Double, NpgsqlDbType.Double],
            @"ON CONFLICT (""Symbol"",""Date"") DO NOTHING", rows);

    private static async Task ImportIndexPeriodsAsync(SqliteConnection src, NpgsqlConnection dst)
    {
        var batch = new List<object?[]>(500);
        double prevClose = 0;
        string? currentCode = null;

        foreach (var period in new[] { "daily", "weekly", "monthly", "quarterly", "yearly" })
        {
            var table = $"index_{period}";
            if (!await SqliteTableExistsAsync(src, table)) continue;

            await using var cmd = new SqliteCommand(
                $"SELECT code, date, open, high, low, close, volume, amount FROM {table} ORDER BY code, date", src);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var rawCode = r.GetString(0).Trim();
                if (rawCode.Length < 7) continue;
                var prefix = rawCode[..2].ToLowerInvariant();
                var code = rawCode[2..];
                if (code.Length != 6) continue;
                if (code != currentCode) { currentCode = code; prevClose = 0; }

                var close = Num(r, 5);
                var change = prevClose > 0 ? Math.Round((close / prevClose - 1) * 100, 4) : 0;
                batch.Add([
                    code, prefix == "sh" ? 1 : 0, ParseDate(r.GetString(1)),
                    Num(r, 2), Num(r, 3), Num(r, 4), close, Num(r, 6), Num(r, 7), change,
                ]);
                prevClose = close;
                if (batch.Count >= 500) { await FlushIndicesAsync(dst, batch); batch.Clear(); }
            }
            if (batch.Count > 0) { await FlushIndicesAsync(dst, batch); batch.Clear(); }
        }
        Console.WriteLine("IndexDailies          导入完成");
    }

    private static Task<int> FlushIndicesAsync(NpgsqlConnection dst, List<object?[]> rows)
        => BatchInsertAsync(dst, "IndexDailies",
            ["Code", "Market", "Date", "Open", "High", "Low", "Close", "Volume", "Amount", "ChangePct"],
            [NpgsqlDbType.Text, NpgsqlDbType.Integer, NpgsqlDbType.Timestamp, NpgsqlDbType.Double, NpgsqlDbType.Double,
             NpgsqlDbType.Double, NpgsqlDbType.Double, NpgsqlDbType.Double, NpgsqlDbType.Double, NpgsqlDbType.Double],
            @"ON CONFLICT (""Code"",""Date"") DO NOTHING", rows);

    private static async Task ImportUsRatesAsync(SqliteConnection src, NpgsqlConnection dst)
    {
        var rows = new List<object?[]>();
        await using var cmd = new SqliteCommand(
            "SELECT maturity, date, rate FROM us_short_rates ORDER BY maturity, date", src);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var maturity = r.GetString(0).Trim().ToUpperInvariant();
            if (maturity is not ("3M" or "6M" or "1Y" or "10Y")) continue;
            rows.Add(["US" + maturity, ParseDate(r.GetString(1)), Num(r, 2), null]);
        }
        var n = await BatchInsertAsync(dst, "MarketIndicators",
            ["Symbol", "Date", "Value", "ChangePct"],
            [NpgsqlDbType.Text, NpgsqlDbType.Timestamp, NpgsqlDbType.Double, NpgsqlDbType.Double],
            @"ON CONFLICT (""Symbol"",""Date"") DO NOTHING", rows);
        Console.WriteLine($"{"MarketIndicators",-22}{n,10} 行新增（美债）");
    }

    private static async Task ImportCnRatesAsync(SqliteConnection src, NpgsqlConnection dst)
    {
        if (!await SqliteTableExistsAsync(src, "cn_rates")) return;

        var shibor = new List<object?[]>();
        var cn10y = new List<object?[]>();
        await using var cmd = new SqliteCommand("SELECT type, date, rate FROM cn_rates ORDER BY type, date", src);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var type = r.GetString(0).Trim().ToUpperInvariant();
            var date = ParseDate(r.GetString(1));
            var rate = Num(r, 2);
            if (type == "CN_10Y") cn10y.Add(["CN10Y", date, rate, null]);
            else if (ShiborPeriod(type) is { } period) shibor.Add([period, date, rate, 0d]);
        }

        var n1 = await BatchInsertAsync(dst, "ShiborRates",
            ["Period", "Date", "Rate", "ChangeBp"],
            [NpgsqlDbType.Text, NpgsqlDbType.Timestamp, NpgsqlDbType.Double, NpgsqlDbType.Double],
            @"ON CONFLICT (""Period"",""Date"") DO NOTHING", shibor);
        var n2 = await BatchInsertAsync(dst, "MarketIndicators",
            ["Symbol", "Date", "Value", "ChangePct"],
            [NpgsqlDbType.Text, NpgsqlDbType.Timestamp, NpgsqlDbType.Double, NpgsqlDbType.Double],
            @"ON CONFLICT (""Symbol"",""Date"") DO NOTHING", cn10y);
        Console.WriteLine($"{"ShiborRates",-22}{n1,10} 行新增；CN10Y {n2} 行");
    }

    private static async Task ImportMarketSeriesAsync(
        SqliteConnection src, NpgsqlConnection dst, string key, string tablePrefix)
    {
        foreach (var period in new[] { "daily", "weekly", "monthly", "quarterly", "yearly" })
        {
            var table = $"{tablePrefix}_{period}";
            if (!await SqliteTableExistsAsync(src, table)) continue;

            var rows = new List<object?[]>();
            await using var cmd = new SqliteCommand(
                $"SELECT date, open, high, low, close FROM {table} ORDER BY date", src);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                rows.Add([key, period, ParseDate(r.GetString(0)), Num(r, 1), Num(r, 2), Num(r, 3), Num(r, 4)]);
            }
            if (rows.Count == 0) continue;

            var n = await BatchInsertAsync(dst, "MarketSeriesPoints",
                ["Key", "Period", "Date", "Open", "High", "Low", "Close"],
                [NpgsqlDbType.Text, NpgsqlDbType.Text, NpgsqlDbType.Timestamp, NpgsqlDbType.Double,
                 NpgsqlDbType.Double, NpgsqlDbType.Double, NpgsqlDbType.Double],
                @"ON CONFLICT (""Key"",""Period"",""Date"") DO NOTHING", rows);
            Console.WriteLine($"{"MarketSeriesPoints",-22}{n,10} 行新增（{key}/{period}）");
        }
    }

    private static async Task ImportBacktestsAsync(SqliteConnection src, NpgsqlConnection dst)
    {
        if (!await SqliteTableExistsAsync(src, "backtest_results")) return;

        var rows = new List<object?[]>();
        await using (var cmd = new SqliteCommand(
            "SELECT id, name, config, metrics, equity_curve, created_at FROM backtest_results", src))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                rows.Add([
                    r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1),
                    r.IsDBNull(2) ? "{}" : r.GetString(2), r.IsDBNull(3) ? "{}" : r.GetString(3),
                    r.IsDBNull(4) ? "[]" : r.GetString(4), ParseDate(r.IsDBNull(5) ? "" : r.GetString(5)),
                ]);
            }
        }
        var n = await BatchInsertAsync(dst, "BacktestRecords",
            ["Id", "Name", "ConfigJson", "MetricsJson", "EquityCurveJson", "CreatedAt"],
            [NpgsqlDbType.Text, NpgsqlDbType.Text, NpgsqlDbType.Text, NpgsqlDbType.Text, NpgsqlDbType.Text, NpgsqlDbType.Timestamp],
            @"ON CONFLICT (""Id"") DO NOTHING", rows);
        Console.WriteLine($"{"BacktestRecords",-22}{n,10} 行新增");

        if (!await SqliteTableExistsAsync(src, "trades")) return;
        var tradeRows = new List<object?[]>();
        await using (var cmd = new SqliteCommand(
            "SELECT backtest_id, code, entry_date, entry_price, exit_date, exit_price, shares, pnl, pnl_pct, exit_reason FROM trades", src))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                if (r.IsDBNull(0)) continue;
                tradeRows.Add([
                    r.GetString(0), r.IsDBNull(1) ? "" : r.GetString(1),
                    ParseDate(r.IsDBNull(2) ? "" : r.GetString(2)), r.IsDBNull(3) ? 0d : Num(r, 3),
                    ParseDate(r.IsDBNull(4) ? "" : r.GetString(4)), r.IsDBNull(5) ? 0d : Num(r, 5),
                    r.IsDBNull(6) ? 0d : Num(r, 6), r.IsDBNull(7) ? 0d : Num(r, 7),
                    r.IsDBNull(8) ? 0d : Num(r, 8), r.IsDBNull(9) ? "" : r.GetString(9),
                ]);
            }
        }
        // 无复合唯一键：先清掉待导入回测的交易，防止重复运行产生重复行
        foreach (var backtestId in tradeRows.Select(x => (string)x[0]!).Distinct())
        {
            await using var del = new NpgsqlCommand(
                @"DELETE FROM ""BacktestTradeRecords"" WHERE ""BacktestId""=@id", dst);
            del.Parameters.AddWithValue("id", backtestId);
            await del.ExecuteNonQueryAsync();
        }
        var n2 = await BatchInsertAsync(dst, "BacktestTradeRecords",
            ["BacktestId", "Code", "EntryDate", "EntryPrice", "ExitDate", "ExitPrice", "Shares", "Pnl", "PnlPct", "ExitReason"],
            [NpgsqlDbType.Text, NpgsqlDbType.Text, NpgsqlDbType.Timestamp, NpgsqlDbType.Double,
             NpgsqlDbType.Timestamp, NpgsqlDbType.Double, NpgsqlDbType.Double, NpgsqlDbType.Double,
             NpgsqlDbType.Double, NpgsqlDbType.Text],
            "", tradeRows);
        Console.WriteLine($"{"BacktestTradeRecords",-22}{n2,10} 行新增");
    }

    private static async Task ImportMonitorAsync(SqliteConnection src, NpgsqlConnection dst)
    {
        if (await SqliteTableExistsAsync(src, "monitor_jobs"))
        {
            var rows = new List<object?[]>();
            await using var cmd = new SqliteCommand(
                "SELECT id, name, config, enabled, created_at FROM monitor_jobs", src);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var config = r.IsDBNull(2) ? "{}" : r.GetString(2);
                rows.Add([
                    r.GetString(0), r.IsDBNull(1) ? "" : r.GetString(1), config,
                    CronOf(config), !r.IsDBNull(3) && r.GetInt64(3) != 0,
                    ParseDate(r.IsDBNull(4) ? "" : r.GetString(4)),
                ]);
            }
            var n = await BatchInsertAsync(dst, "MonitoringJobs",
                ["Id", "Name", "ConfigJson", "Cron", "Enabled", "CreatedAt"],
                [NpgsqlDbType.Text, NpgsqlDbType.Text, NpgsqlDbType.Text, NpgsqlDbType.Text,
                 NpgsqlDbType.Boolean, NpgsqlDbType.Timestamp],
                @"ON CONFLICT (""Id"") DO NOTHING", rows);
            Console.WriteLine($"{"MonitoringJobs",-22}{n,10} 行新增");
        }

        if (!await SqliteTableExistsAsync(src, "monitoring_results")) return;
        var runs = new List<object?[]>();
        await using (var cmd = new SqliteCommand(
            "SELECT job_id, run_at, entry_signals, exit_signals FROM monitoring_results", src))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                if (r.IsDBNull(0)) continue;
                runs.Add([
                    r.GetString(0), ParseDate(r.IsDBNull(1) ? "" : r.GetString(1)),
                    r.IsDBNull(2) ? "[]" : r.GetString(2), r.IsDBNull(3) ? "[]" : r.GetString(3),
                ]);
            }
        }
        var n2 = await BatchInsertAsync(dst, "MonitoringRuns",
            ["JobId", "RunAt", "EntrySignalsJson", "ExitSignalsJson"],
            [NpgsqlDbType.Text, NpgsqlDbType.Timestamp, NpgsqlDbType.Text, NpgsqlDbType.Text],
            "", runs);
        Console.WriteLine($"{"MonitoringRuns",-22}{n2,10} 行新增");
    }

    // ---------- 通用 ----------

    private static async Task<int> BatchInsertAsync(
        NpgsqlConnection conn, string table, string[] columns, NpgsqlDbType[] types,
        string conflictClause, List<object?[]> rows)
    {
        if (rows.Count == 0) return 0;
        var total = 0;
        for (var offset = 0; offset < rows.Count; offset += 500)
        {
            var chunk = rows.Skip(offset).Take(500).ToList();
            var sql = new StringBuilder($"INSERT INTO \"{table}\" (")
                .Append(string.Join(",", columns.Select(c => $"\"{c}\"")))
                .Append(") VALUES ");

            await using var cmd = new NpgsqlCommand { Connection = conn };
            for (var i = 0; i < chunk.Count; i++)
            {
                if (i > 0) sql.Append(',');
                sql.Append('(');
                for (var c = 0; c < columns.Length; c++)
                {
                    if (c > 0) sql.Append(',');
                    var name = $"p{i}_{c}";
                    sql.Append('@').Append(name);
                    cmd.Parameters.Add(new NpgsqlParameter(name, types[c])
                    {
                        Value = chunk[i][c] ?? DBNull.Value,
                    });
                }
                sql.Append(')');
            }
            if (!string.IsNullOrEmpty(conflictClause)) sql.Append(' ').Append(conflictClause);
            cmd.CommandText = sql.ToString();
            total += await cmd.ExecuteNonQueryAsync();
        }
        return total;
    }

    private static double Num(SqliteDataReader r, int index)
        => r.IsDBNull(index) ? 0d : Convert.ToDouble(r.GetValue(index));

    private static DateTime ParseDate(string value)
    {
        value = value.Trim();
        if (value.Length >= 8 && DateTime.TryParseExact(value[..8], "yyyyMMdd",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var compact))
            return compact;
        return DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d) ? d : DateTime.MinValue;
    }

    private static string? ShiborPeriod(string type) => type switch
    {
        "SHIBOR_ON" => "隔夜(O/N)",
        "SHIBOR_1W" => "1周(1W)",
        "SHIBOR_2W" => "2周(2W)",
        "SHIBOR_1M" => "1月(1M)",
        "SHIBOR_3M" => "3月(3M)",
        "SHIBOR_6M" => "6月(6M)",
        "SHIBOR_9M" => "9月(9M)",
        "SHIBOR_1Y" => "1年(1Y)",
        _ => null,
    };

    private static string CronOf(string configJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(configJson);
            return doc.RootElement.TryGetProperty("cron", out var cron) ? cron.GetString() ?? "" : "";
        }
        catch
        {
            return "";
        }
    }
}
