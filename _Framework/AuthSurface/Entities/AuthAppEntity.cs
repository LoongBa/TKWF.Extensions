using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuthSurface;

/// <summary>
/// 应用目录实体（表 <c>AuthApp</c>）——"我的应用"聚合的 AppName 来源。
/// <para>背景：AuthCenter <c>AuthGrantEntity</c> 底座<b>无应用元数据</b>（仅 AppId 裸 id——explore 实证）；
/// 独立目录表 = OAuth2 client 注册表语义（长期演进清晰），不动 AuthCenter（跨扩展零改）。</para>
/// <para>⚠️ 软引用语义：<see cref="AppId"/> 与 <c>AuthGrant.AppId</c> / <c>RedemptionCode.TargetAppId</c> 为字符串约定
/// （无 FK 约束）——删除前检查引用；建议 <c>SetEnabledAsync(false)</c> 软禁用而非物理删除（防 vm_UserApps 丢 AppName）。</para>
/// </summary>
[Table("AuthApp")]
[FreeSql.DataAnnotations.Index("UX_AuthApp_AppId", nameof(AppId), IsUnique = true)]
[DomainGenerateCode(DefaultPageSize = 50)]
public partial class AuthAppEntity
{
    [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
    public long Id { get; set; }

    /// <summary>应用/产品 id——唯一；对齐 <c>AuthGrant.AppId</c> / <c>RedemptionCode.TargetAppId</c> 语义。</summary>
    [FreeSql.DataAnnotations.Column(Position = 2, StringLength = 100)]
    public string AppId { get; set; } = "";

    /// <summary>应用名（"我的应用"列表展示字段）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 3, StringLength = 100)]
    public string AppName { get; set; } = "";

    /// <summary>图标 URL（string 非二进制——对齐 AuthAccount.Avatar 先例）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 4, IsNullable = true, StringLength = 512)]
    public string? Icon { get; set; }

    /// <summary>启用标记——false = 软禁用（应用不出现在"我的应用"聚合结果语义由门面保证）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 5)]
    public bool IsEnabled { get; set; } = true;

    [FreeSql.DataAnnotations.Column(Position = 6, CanUpdate = false)]
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;

    [FreeSql.DataAnnotations.Column(Position = 7)]
    public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
}
