/* ============================================================================
   TKWF.Ext.Permissions — PermissionGrant 建表迁移脚本 (V0.7.0 W2)
   ----------------------------------------------------------------------------
   用途：生产环境建表（扩展实体不在消费方 SyncTables 自动建表范围，需 DBA/CI 执行）。
   对齐：列名/类型与 PermissionGrantEntity（_Framework/Permissions/PermissionGrantEntity.cs）完全一致。
   幂等：IF OBJECT_ID 守卫——重复执行安全（不会重复建表/索引）。
   注意：IsGranted 由扩展 upsert 语义维护（存在则更新，不存在则插入，按三列业务键）。
   ============================================================================ */

IF OBJECT_ID(N'dbo.PermissionGrant', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PermissionGrant] (
        [Id]             BIGINT IDENTITY(1,1) NOT NULL,
        [PermissionName] NVARCHAR(200)        NOT NULL,
        [ProviderName]   NVARCHAR(64)         NOT NULL,
        [ProviderKey]    NVARCHAR(128)        NOT NULL,
        [IsGranted]      BIT                  NOT NULL CONSTRAINT [DF_PermissionGrant_IsGranted] DEFAULT (0),
        CONSTRAINT [PK_PermissionGrant] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [UK_PermissionGrant_Provider] UNIQUE NONCLUSTERED
        (
            [PermissionName] ASC,
            [ProviderName]   ASC,
            [ProviderKey]    ASC
        )
    );
END;
GO

/* 按权限名查询索引（GetByPermissionNameAsync / 权限检查高频路径） */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PermissionGrant_PermissionName' AND object_id = OBJECT_ID(N'dbo.PermissionGrant'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_PermissionGrant_PermissionName]
        ON [dbo].[PermissionGrant] ([PermissionName] ASC);
END;
GO

/* 按 provider 查询索引（GetByProviderAsync / 用户-角色权限批量解析路径） */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PermissionGrant_Provider' AND object_id = OBJECT_ID(N'dbo.PermissionGrant'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_PermissionGrant_Provider]
        ON [dbo].[PermissionGrant] ([ProviderName] ASC, [ProviderKey] ASC);
END;
GO
