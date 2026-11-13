-- =====================================================================
-- FundAnalysis 灾难重建 · 01b / 补建 3 个分级角色（修复 42501 用）
-- 用途：已经执行过 01（库与 fundmanager 已建好），但执行 03 时报
--       SQL Error [42501]: permission denied to create role 时，用本文件补角色。
-- 原因：CREATE ROLE 需要 CREATEROLE 属性，而 fundmanager 按最小权限设为
--       NOCREATEROLE；实例级建角色与库内授权必须由不同身份执行。
-- 执行位置：任意库（连 postgres 维护库即可），实例级操作，执行一次即可
--   psql -v ON_ERROR_STOP=1 -h <HOST> -p <PORT> -U postgres -d postgres -f 01b_create_roles.sql
-- 执行身份：超级用户，或具备 CREATEROLE 属性的运维账号
-- ⚠️ 执行前把 3 处 REPLACE_ME_* 换成强密码。
-- ⚠️ 若托管数据库不提供 CREATEROLE 账号（部分云 PostgreSQL 只给受限管理员）：
--    在云控制台 / 宝塔「PostgreSQL 用户管理」里手工创建下面 3 个同名 LOGIN 角色，
--    设置密码后跳过本文件，直接执行 03（03 只做授权，不需要 CREATEROLE）。
-- 幂等性：DO 块按 pg_roles 判断，可重复执行；重复执行会重设这 3 个账号密码。
-- 完成后：回到 fund_db 执行 03_create_users.sql，再执行 04_verify.sql。
-- =====================================================================

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

-- 结果确认：应看到 4 个 fund* 角色，全部 rolcanlogin = true、rolcreaterole = false
SELECT rolname, rolcanlogin, rolcreatedb, rolcreaterole
FROM pg_roles
WHERE rolname LIKE 'fund%'
ORDER BY 1;
