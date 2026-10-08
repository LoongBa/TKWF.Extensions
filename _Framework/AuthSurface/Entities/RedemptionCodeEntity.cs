using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuthSurface;

/// <summary>
/// 兑换码实体（表 <c>RedemptionCode</c>）——口令兑换体系数据属主（授权面）。
/// <para>单实体承载生成/兑换/历史（v0.1.0 无批次——一码一兑语义下"兑换记录 = 码被兑换态"；
/// v0.2.0 批次管理端加 <c>BatchId</c> 列即可扩展）。</para>
/// <para>🔒 安全：code 明文<b>不落库</b>——<see cref="CodeHash"/>（SHA256 十六进制 64，唯一，防库泄露即任意兑换）；
/// 展示经 <see cref="CodeMasked"/>（脱敏值）。行级 FK = <c>AuthAccount.UId</c>（前置裁定 P4——<see cref="RedeemedByUId"/>）。</para>
/// </summary>
[Table("TKWF_RedemptionCode")]
[FreeSql.DataAnnotations.Index("TKWFIX_RedemptionCode_CodeHash", nameof(CodeHash), IsUnique = true)]
[DomainGenerateCode(DefaultPageSize = 50)]
public partial class RedemptionCodeEntity
{
    [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
    public long Id { get; set; }

    /// <summary>SHA256(code 明文) 十六进制 64——唯一（一码一兑防重）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 2, StringLength = 64)]
    public string CodeHash { get; set; } = "";

    /// <summary>展示脱敏值（如 "EDU-AB12-****-EF56"）——对应 <c>RedemptionRecordDto.CodeMasked</c> 已脱敏透传。</summary>
    [FreeSql.DataAnnotations.Column(Position = 3, StringLength = 32)]
    public string CodeMasked { get; set; } = "";

    /// <summary>商品/载体名。</summary>
    [FreeSql.DataAnnotations.Column(Position = 4, StringLength = 100)]
    public string ProductName { get; set; } = "";

    /// <summary>兑换目标应用 id——软引用 <c>AuthApp.AppId</c> 语义（无 FK 约束，运营灵活性；删除前检查引用）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 5, StringLength = 100)]
    public string TargetAppId { get; set; } = "";

    /// <summary>
    /// 状态——<remarks>0=Available / 1=Redeemed / 2=Expired（惰性翻转——查询时发现过期顺手标记）；
    /// 对齐 AuthGrantEntity.Status(int) 先例；v0.2.0 考虑 RedemptionCodeStatus 枚举 + Revoked(管理作废)。</remarks>
    /// </summary>
    [FreeSql.DataAnnotations.Column(Position = 6)]
    public int Status { get; set; }

    /// <summary>有效期（UTC）——null=永不过期。</summary>
    [FreeSql.DataAnnotations.Column(Position = 7, IsNullable = true)]
    public DateTime? ExpireAtUtc { get; set; }

    /// <summary>兑换人平台内部 id（行级 FK=<c>AuthAccount.UId</c>，P4 白名单）——未兑换为 null。</summary>
    [FreeSql.DataAnnotations.Column(Position = 8, IsNullable = true, StringLength = 32)]
    public string? RedeemedByUId { get; set; }

    /// <summary>兑换时间（UTC）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 9, IsNullable = true)]
    public DateTime? RedeemedAtUtc { get; set; }

    [FreeSql.DataAnnotations.Column(Position = 10, CanUpdate = false)]
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;

    [FreeSql.DataAnnotations.Column(Position = 11)]
    public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
}
