-- =====================================================================
-- FundAnalysis 灾难重建 · 04 / 验收与账号连通性核对
-- 执行位置：fund_db 库内
--   psql -v ON_ERROR_STOP=1 -h <HOST> -p <PORT> -U fundmanager -d fund_db -f 04_verify.sql
-- 期望结果：26 张表、32 个索引、5 条迁移历史、4 个 fund* 角色
-- 前置：4 个角色由 01（或 01b 补建）创建；缺角色时先补角色再跑 03。
-- =====================================================================

-- 1) 连接与实例信息
SELECT current_database() AS db, current_user AS login_user, version() AS pg_version;

-- 2) 表数量与缺失表检测（第二段应返回 0 行）
SELECT count(*) AS table_count
FROM information_schema.tables
WHERE table_schema = 'public' AND table_type = 'BASE TABLE';

WITH expected(t) AS (
    VALUES ('AlertRules'), ('AspNetRoleClaims'), ('AspNetRoles'), ('AspNetUserClaims'),
           ('AspNetUserLogins'), ('AspNetUserRoles'), ('AspNetUserTokens'), ('AspNetUsers'),
           ('FundHoldingSyncs'), ('FundHoldings'), ('FundNavs'), ('IndexDailies'),
           ('IndexIntradays'), ('LprRates'), ('MarketIndicators'), ('MoneyFlowDailies'),
           ('MoneySupplies'), ('OmoOperations'), ('ReserveRatios'), ('ShiborRates'),
           ('StockBasics'), ('StockDailies'), ('StockFundamentals'), ('StockMinutes'),
           ('WatchlistItems'), ('__EFMigrationsHistory')
)
SELECT e.t AS missing_table
FROM expected e
LEFT JOIN information_schema.tables i
       ON i.table_schema = 'public' AND i.table_name = e.t
WHERE i.table_name IS NULL
ORDER BY 1;

-- 3) 索引数量（应为 32）与迁移历史（应为 5 行，顺序与 02 头部一致）
SELECT count(*) AS index_count FROM pg_indexes WHERE schemaname = 'public';
SELECT "MigrationId", "ProductVersion" FROM "__EFMigrationsHistory" ORDER BY 1;

-- 4) 角色清单：fundmanager / fund_reader / fund_inserter / fund_updater
SELECT rolname, rolcanlogin, rolsuper, rolcreatedb, rolcreaterole
FROM pg_roles WHERE rolname LIKE 'fund%' ORDER BY 1;

-- 5) 权限矩阵抽样（以 "StockDailies" 为例）
--    期望：fundmanager 全 true；reader 仅 select；inserter select+insert；updater select+update
SELECT r.rolname,
       has_table_privilege(r.rolname, 'public."StockDailies"', 'SELECT') AS can_select,
       has_table_privilege(r.rolname, 'public."StockDailies"', 'INSERT') AS can_insert,
       has_table_privilege(r.rolname, 'public."StockDailies"', 'UPDATE') AS can_update,
       has_table_privilege(r.rolname, 'public."StockDailies"', 'DELETE') AS can_delete
FROM pg_roles r
WHERE r.rolname LIKE 'fund%'
ORDER BY 1;

-- 6) 数据量概览（估算值，避免对大表做全表 COUNT；重建后应全部为 0）
SELECT relname AS table_name, n_live_tup AS approx_rows
FROM pg_stat_user_tables
WHERE schemaname = 'public'
ORDER BY relname;

-- 7) 应用级登录账号（系统用户，不是数据库角色）
--    由 DbSeeder.SeedAsync() 在后端首次启动时自动创建：Admin/VIP/User 三个角色 +
--    admin 用户（密码取 Seed:AdminPassword / 环境变量 Seed__AdminPassword）。
--    Identity 的 PasswordHash 是 PBKDF2 格式，不能用 SQL 手写 INSERT 伪造，
--    所以本套脚本不生成应用账号，只提供验收查询：
SELECT u."UserName", u."Email", u."EmailConfirmed", u."CreatedAt"
FROM "AspNetUsers" u ORDER BY u."UserName";
SELECT r."Name" AS role_name FROM "AspNetRoles" r ORDER BY 1;

-- =====================================================================
-- 8) 各账号连通性 / 权限实测（按需复制执行，事务回滚不写入数据）
--
-- psql -h <HOST> -p <PORT> -U fund_reader   -d fund_db -c 'SELECT count(*) FROM "StockDailies";'
-- psql -h <HOST> -p <PORT> -U fund_inserter -d fund_db <<'SQL'
--   BEGIN;
--   INSERT INTO "LprRates" ("Date", "Lpr1Y", "Lpr5Y") VALUES ('2000-01-01', 0, 0);
--   ROLLBACK;
-- SQL
-- psql -h <HOST> -p <PORT> -U fund_updater  -d fund_db <<'SQL'
--   BEGIN;
--   UPDATE "LprRates" SET "Lpr1Y" = "Lpr1Y" WHERE false;
--   ROLLBACK;
-- SQL
-- 反向验证（应报 permission denied）：
--   fund_reader 执行 INSERT / UPDATE；fund_inserter 执行 UPDATE；fund_updater 执行 INSERT。
--
-- 9) 重建后的必要动作
--    a. 后端 appsettings.json / 环境变量：确认 ConnectionStrings__Default 指向
--       fund_db + fundmanager + 01 中设置的密码；Web 与 Collector 必须完全一致。
--    b. 启动 Web：MigrateAsync() 看到 5 条历史会跳过建表；
--       DbSeeder 创建角色与 admin；首次启动自动触发 InitialSync / SyncAll /
--       SyncIndices / SyncMarketIndicators / SyncFundList 等采集任务补行情数据。
--    c. 启动 Collector（若有独立部署），确认 Collector__ScheduleEnabled 二选一，避免重复采集。
--    d. 无法自动恢复的数据：注册用户、自选、价格提醒规则、历史业务配置。
--       若还有旧备份（pg_dump / 物理备份 / WAL）优先 pg_restore 后再补采，
--       行情类数据重采即可，业务类数据只能靠备份。
--    e. 登录后立即修改 admin 默认口令；确认 pg_hba.conf 与防火墙/安全组放行 <PORT>。
-- =====================================================================
