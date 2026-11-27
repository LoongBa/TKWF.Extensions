-- =====================================================================
-- AuditLogging v0.9.0 生产 DDL（加列；全部可空，存量行为 NULL）
-- 适用：生产库关闭 UseAutoSyncStructure（开发环境自动加列，无需执行）
-- 执行前请确认目标列不存在；三方言择一执行。
-- =====================================================================

-- ---------- PostgreSQL ----------
ALTER TABLE "TKWF_AuditLog"      ADD COLUMN IF NOT EXISTS "TenantId"      BIGINT NULL;
ALTER TABLE "TKWF_SecurityLog"   ADD COLUMN IF NOT EXISTS "TenantId"      BIGINT NULL;
ALTER TABLE "TKWF_BusinessAudit" ADD COLUMN IF NOT EXISTS "AttributesJson" TEXT   NULL;

-- ---------- SQL Server（ADD COLUMN 无 IF NOT EXISTS，执行前确认列不存在） ----------
-- ALTER TABLE [TKWF_AuditLog]      ADD [TenantId]      BIGINT        NULL;
-- ALTER TABLE [TKWF_SecurityLog]   ADD [TenantId]      BIGINT        NULL;
-- ALTER TABLE [TKWF_BusinessAudit] ADD [AttributesJson] NVARCHAR(MAX) NULL;

-- ---------- SQLite ----------
-- ALTER TABLE TKWF_AuditLog      ADD COLUMN TenantId      BIGINT NULL;
-- ALTER TABLE TKWF_SecurityLog   ADD COLUMN TenantId      BIGINT NULL;
-- ALTER TABLE TKWF_BusinessAudit ADD COLUMN AttributesJson TEXT   NULL;

-- =====================================================================
-- 校验（执行后）
-- =====================================================================
-- PostgreSQL:
--   SELECT column_name FROM information_schema.columns
--    WHERE table_name IN ('TKWF_AuditLog','TKWF_SecurityLog','TKWF_BusinessAudit')
--      AND column_name IN ('TenantId','AttributesJson');
-- SQL Server:
--   SELECT OBJECT_NAME(object_id) AS tbl, name FROM sys.columns
--    WHERE name IN ('TenantId','AttributesJson');
-- SQLite:
--   PRAGMA table_info(TKWF_AuditLog);
