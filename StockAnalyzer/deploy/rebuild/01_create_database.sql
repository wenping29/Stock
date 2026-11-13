-- =====================================================================
-- FundAnalysis 灾难重建 · 01 / 建库 + 应用主账号（PostgreSQL）
-- 目标实例：生产连接串中的 fund_db @ 43.142.147.244:5454
-- 执行位置：连到维护库 postgres（不要在 fund_db 里执行）
--   psql -v ON_ERROR_STOP=1 -h <HOST> -p <PORT> -U postgres -d postgres -f 01_create_database.sql
--   pgAdmin / 宝塔 PostgreSQL 管理页：选中 postgres 库，粘贴执行
-- 执行身份：超级用户，或具备 CREATEDB + CREATEROLE 的运维账号
--
-- 执行顺序：01 -> 02 -> 03 -> 04
--   本脚本一次性创建 4 个实例级角色（含 3 个分级账号），02/03 不再建角色。
--   若已执行过旧版 01（只有 fundmanager），不要重跑本文件的 CREATE DATABASE，
--   改用 01b_create_roles.sql 补建那 3 个角色即可。
-- 幂等性：角色与授权可重复执行；CREATE DATABASE 若同名库已存在会报错，
--         此时把第 2 步整段注释掉即可。本脚本绝不 DROP 任何库、表或角色。
--         注意：重复执行会重设 4 个角色的密码，务必与连接串保持同步。
--
-- ⚠️ 执行前把 4 处 REPLACE_ME_* 换成强密码；
--    Web 与 Collector 的 appsettings.json / 环境变量 ConnectionStrings__Default
--    必须使用同一个库名、账号、密码，否则系统仍然起不来。
--    建议用环境变量注入密码，不要再把明文口令提交进仓库。
-- ⚠️ CREATE ROLE / CREATE DATABASE 是实例级操作，执行账号必须同时具备
--    CREATEROLE + CREATEDB（或超级用户）；库内的 03 不需要这两个属性。
-- =====================================================================

-- 1) 应用主账号：Web(EF Migrate + Hangfire) 与 Collector 共用
--    必须是库 OWNER —— Hangfire.PostgreSql 启动时会创建 hangfire schema 及其表，
--    普通受限账号会在这一步行权限不足并导致启动失败。
DO $rebuild$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'fundmanager') THEN
        CREATE ROLE fundmanager LOGIN;
    END IF;
END
$rebuild$;

ALTER ROLE fundmanager
    WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION
    PASSWORD 'REPLACE_ME_APP_PASSWORD';

-- 1b) 分级运维账号（实例级角色）：查询 / 新增 / 修改
--     与 fundmanager 一样必须由具备 CREATEROLE 的账号创建；它们的库内权限在 03 授予。
--     已执行过旧版 01 的不要重跑本文件，改用 01b_create_roles.sql 补建这 3 个角色。
DO $rebuild$
DECLARE r text;
BEGIN
    FOREACH r IN ARRAY ARRAY['fund_reader', 'fund_inserter', 'fund_updater'] LOOP
        IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = r) THEN
            EXECUTE format('CREATE ROLE %I LOGIN', r);
        END IF;
    END LOOP;
END
$rebuild$;

ALTER ROLE fund_reader
    WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION
    PASSWORD 'REPLACE_ME_READ_PASSWORD';
ALTER ROLE fund_inserter
    WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION
    PASSWORD 'REPLACE_ME_INSERT_PASSWORD';
ALTER ROLE fund_updater
    WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION
    PASSWORD 'REPLACE_ME_UPDATE_PASSWORD';

-- 2) 重建数据库（CREATE DATABASE 不能放在事务或 DO 块内）
--    若库已存在：确认其中没有需要保留的数据后，注释掉下面整段再执行。
CREATE DATABASE fund_db
    WITH OWNER = fundmanager
    ENCODING = 'UTF8'
    CONNECTION LIMIT = -1;

GRANT ALL PRIVILEGES ON DATABASE fund_db TO fundmanager;

-- 3) 可选加固：默认 PUBLIC 拥有 CONNECT 与 TEMPORARY 权限
--    取消注释可禁止未授权账号连接；但会影响依赖匿名连接的其他系统，
--    且 03 已为三个分级账号显式 GRANT CONNECT，故默认不启用。
-- REVOKE CONNECT, TEMPORARY ON DATABASE fund_db FROM PUBLIC;

-- 4) 结果确认
SELECT datname, pg_get_userbyid(datdba) AS owner,
       pg_encoding_to_char(encoding) AS encoding
FROM pg_database
WHERE datname = 'fund_db';
