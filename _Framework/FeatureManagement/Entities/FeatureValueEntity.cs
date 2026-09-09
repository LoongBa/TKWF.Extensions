using System;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.FeatureManagement;

/// <summary>
/// Feature 值实体（表 FeatureValue）——分层值存储（Global/Tenant/Role/User）。
/// <para>唯一约束 UX_FeatureValue_Name_Provider（Name+ProviderName+ProviderKey）——注意 SQLite/PostgreSQL
/// 可空唯一索引 NULL 互不相同：Global 层（ProviderKey=null）唯一性由 <see cref="FeatureManager.SetValueAsync"/>
/// 应用层保证（预检 + 事务内二次校验，对齐 FileManagement C2 教训）。</para>
/// <para>删除语义：物理删除（不声明 IsDeleted，hasSoftDelete:false）。</para>
/// </summary>
[Table("FeatureValue")]
[FreeSql.DataAnnotations.Index("UX_FeatureValue_Name_Provider", nameof(Name) + "," + nameof(ProviderName) + "," + nameof(ProviderKey), IsUnique = true)]
[DomainGenerateCode(DefaultPageSize = 50, SubDomain = "FeatureManagement", SubDomainRoutePrefix = "/FeatureManagement")]
public partial class FeatureValueEntity
{
    /// <summary>主键（自增）。</summary>
    [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
    public long Id { get; set; }

    /// <summary>Feature 名（对应 FeatureDefinition.Name）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 2)]
    [System.ComponentModel.DataAnnotations.MaxLength(128)]
    public string Name { get; set; } = "";

    /// <summary>值（字符串表示）。
    /// <para>v0.3.0：<c>[MaxLength(512)]</c> → <c>[MaxLength(-1)]</c>（FreeSql 无界字符串约定——
    /// SQLite=text/Pg=text/SqlServer=nvarchar(max)），容纳 JSON 复杂值；新装库 SyncTables 幂等建无界列；
    /// 存量库 VARCHAR(512) 加宽依赖 SyncTables 实现（若未自动 ALTER 需 DBA 手动变更，见使用指南）。
    /// 注：单纯移除 MaxLength 属性 FreeSql 默认映射 NVARCHAR(255)（仍受限）——须显式 <c>MaxLength(-1)</c>。</para></summary>
    [FreeSql.DataAnnotations.Column(Position = 3)]
    [System.ComponentModel.DataAnnotations.MaxLength(-1)]
    public string? Value { get; set; }

    /// <summary>Provider 层（Global/Tenant/Role/User）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 4)]
    [System.ComponentModel.DataAnnotations.MaxLength(32)]
    public string ProviderName { get; set; } = FeatureProviders.Global;

    /// <summary>ProviderKey（Global=null；Tenant=TenantId；Role=角色名；User=UserId）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 5)]
    [System.ComponentModel.DataAnnotations.MaxLength(128)]
    public string? ProviderKey { get; set; }

    /// <summary>描述（管理界面）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 6)]
    [System.ComponentModel.DataAnnotations.MaxLength(512)]
    public string? Description { get; set; }

    /// <summary>是否对客户端可见（管理界面过滤）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 7)]
    public bool IsVisibleToClients { get; set; } = false;

    /// <summary>创建时间（UTC）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 8, CanUpdate = false)]
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;

    /// <summary>更新时间（UTC）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 9)]
    public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
}
