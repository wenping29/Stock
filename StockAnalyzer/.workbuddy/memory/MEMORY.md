# 项目长期笔记 · StockAnalyzer / FundAnalysis

## 数据库
- 生产数据库是 PostgreSQL：fund_db @ 43.142.147.244:5454，账号 fundmanager（见 backend/FundAnalysis/appsettings.json 与 FundAnalysis.Collector/appsettings.json）。Web 与独立采集器必须共用同一库。
- 表结构唯一权威来源是 EF Core 迁移（backend/FundAnalysis.Core/Migrations），全量 DDL 用 `dotnet ef migrations script --idempotent` 生成；手写 SQL 需同时写入 __EFMigrationsHistory 的 5 条迁移记录，否则启动 MigrateAsync() 会重复建表失败。
- deploy/createdb.sql 与 backend 下两份 init-schema-postgresql.sql 为早期版本，已与当前模型不一致，禁止用于生产恢复；恢复请用 deploy/rebuild/ 下的 5 个脚本（01 建库+4 角色 / 01b 补建角色 / 02 DDL / 03 授权 / 04 验收）。
- 权限边界：CREATE ROLE / CREATE DATABASE 是实例级操作，执行账号须有 CREATEROLE+CREATEDB（或超级用户）；库内 GRANT 由库 OWNER 执行即可。fundmanager 按最小权限设为 NOCREATEROLE，不要在 03 里建角色。
- Web 启动链路：MigrateAsync() → DbSeeder（Admin/VIP/User 角色 + admin 用户）→ Hangfire 注册 → 空库时自动触发行情/流动性/指数/宏观/基本面/基金清单采集。应用级用户密码由 Identity PBKDF2 生成，不能用 SQL 伪造。
- Hangfire.PostgreSql 会自建 hangfire schema，应用连接账号必须是库 OWNER 或有 CREATE 权限。
