using System;
using System.Collections.Generic;

namespace TKWF.Ext.AuthSurface;

/// <summary>
/// 批次统计分桶（T-2 统计口径契约——DataService 原子分桶载体）：
/// expired = Status==0 且 expire_at_utc≤now（时间派生，禁按 Status==2 落库计数）；pending = Status==0 且 (null∥&gt;now)；
/// redeemed = Status==1；revoked = Status==3；total 由头列 GeneratedCount 提供（非分桶）。
/// </summary>
/// <param name="RedeemedCount">已兑计数（Status==1）。</param>
/// <param name="ExpiredCount">过期计数（Status==0 且过期——时间派生）。</param>
/// <param name="RevokedCount">已吊销计数（Status==3）。</param>
/// <param name="PendingCount">未兑未过期计数（Status==0 且未过期）。</param>
public sealed record CodeBatchStatsDto(
    int RedeemedCount,
    int ExpiredCount,
    int RevokedCount,
    int PendingCount);

/// <summary>
/// 兑换码批次 DTO（v0.3.0 批次管理端）——批次头基础 + 统计指标（T-2 统计口径契约：
/// expired = Status==0 且 expire_at_utc≤now（时间派生，禁按 Status==2 落库计数）；pending = Status==0 且 (null∥&gt;now)；
/// redeemed = Status==1；revoked = Status==3；total = GeneratedCount（DRAFT 为 0）；不变式 total = redeemed+pending+expired+revoked；
/// 批次码行生成时继承头 expire_at_utc——口径一律按码行逐码判定）。
/// </summary>
/// <param name="Id">批次 Id。</param>
/// <param name="BatchNo">展示批次号（B{yyyyMMdd}{4 随机}）。</param>
/// <param name="Name">批次/券名称。</param>
/// <param name="ProductName">产品名（码行生成时投影）。</param>
/// <param name="TargetAppId">目标应用（软引用 AuthApp.AppId；"*" 全产品语义透传）。</param>
/// <param name="ValidityDays">兑换后授权时长模板（自然日，null=无 grant 语义——扩展不解释，消费方编排读）。</param>
/// <param name="ExpireAtUtc">批次券有效期（绝对 UTC；null=不过期）。</param>
/// <param name="Channel">发行渠道（≤100 软词汇）。</param>
/// <param name="Quantity">计划数量。</param>
/// <param name="GeneratedCount">实际生成数（统计 total 口径）。</param>
/// <param name="Status">状态（0 DRAFT/1 GENERATING/2 GENERATED/3 REVOKED）。</param>
/// <param name="Test">测试批次标记（拒兑策略归消费方兑换编排）。</param>
/// <param name="ExportedAtUtc">生成即导出时间（= GENERATED 落定时刻）。</param>
/// <param name="CreatedBy">创建人（透传不做身份校验）。</param>
/// <param name="CreateTime">创建时间（UTC）。</param>
/// <param name="RedeemedCount">已兑统计（Status==1 码行计数）。</param>
/// <param name="PendingCount">未兑未过期统计（Status==0 且未过期码行计数）。</param>
/// <param name="ExpiredCount">过期统计（Status==0 且 expire_at_utc≤now 码行计数——时间派生）。</param>
/// <param name="RevokedCount">已吊销统计（Status==3 码行计数）。</param>
/// <param name="RedeemRate">兑换率（RedeemedCount/GeneratedCount，GeneratedCount=0 为 0——A-6 口径）。</param>
public sealed record CodeBatchDto(
    long Id,
    string BatchNo,
    string Name,
    string ProductName,
    string TargetAppId,
    int? ValidityDays,
    DateTime? ExpireAtUtc,
    string? Channel,
    int Quantity,
    int GeneratedCount,
    int Status,
    bool Test,
    DateTime? ExportedAtUtc,
    string? CreatedBy,
    DateTime CreateTime,
    int RedeemedCount,
    int PendingCount,
    int ExpiredCount,
    int RevokedCount,
    double RedeemRate);

/// <summary>
/// 创建批次请求（v0.3.0 T-0）——通用校验：Name/ProductName/TargetAppId 非空、Quantity∈[1,MaxBatchQuantity(默认5000)]、
/// ExpireAtUtc 为空或晚于 now、Channel ≤100、ValidityDays&gt;0（业务政策白名单归消费方）。
/// </summary>
/// <param name="Name">批次/券名称（≤100，非空）。</param>
/// <param name="ProductName">产品名（≤100，非空）。</param>
/// <param name="TargetAppId">目标应用（≤100，非空）。</param>
/// <param name="ValidityDays">兑换后授权时长模板（自然日，null=无 grant 语义；须&gt;0）。</param>
/// <param name="ExpireAtUtc">批次券有效期（绝对 UTC，null=不过期；非 null 须晚于 now）。</param>
/// <param name="Channel">发行渠道（≤100，可选）。</param>
/// <param name="Quantity">计划数量（1..Options.MaxBatchQuantity 默认 5000）。</param>
/// <param name="Test">测试批次标记（拒兑策略归消费方兑换编排）。</param>
/// <param name="CreatedBy">创建人（≤64，透传不做身份校验）。</param>
public sealed record CreateCodeBatchRequest(
    string Name,
    string ProductName,
    string TargetAppId,
    int? ValidityDays = null,
    DateTime? ExpireAtUtc = null,
    string? Channel = null,
    int Quantity = 1,
    bool Test = false,
    string? CreatedBy = null);

/// <summary>
/// 批次列表过滤（T-2）——状态/渠道/创建时间范围/名称模糊/Test 标记。
/// </summary>
/// <param name="Status">状态过滤（null=全部：0 DRAFT/1 GENERATING/2 GENERATED/3 REVOKED）。</param>
/// <param name="Channel">渠道精确过滤（null=全部）。</param>
/// <param name="StartTime">创建时间下界（UTC，含）。</param>
/// <param name="EndTime">创建时间上界（UTC，含）。</param>
/// <param name="NameKeyword">名称模糊（Contains）。</param>
/// <param name="Test">Test 过滤（null=全部）。</param>
public sealed record CodeBatchFilter(
    int? Status = null,
    string? Channel = null,
    DateTime? StartTime = null,
    DateTime? EndTime = null,
    string? NameKeyword = null,
    bool? Test = null);

/// <summary>
/// 批次详情 DTO（T-2）——批次头基础 + 统计 + 派生标志。
/// <para><paramref name="PartialRevoked"/>：<c>Status==GENERATED 且 0&lt;RevokedCount&lt;PendingCount+ExpiredCount</c>（部分吊销——
/// PARTIAL_REVOKED 不落库，统计聚合直接派生）。</para>
/// </summary>
/// <param name="Batch">批次头基础（含统计——复用 <see cref="CodeBatchDto"/>，其统计字段即详情统计）。</param>
/// <param name="PartialRevoked">部分吊销派生标志（全吊销=批次 Status 已升 REVOKED，本标志 false）。</param>
public sealed record CodeBatchDetailDto(
    CodeBatchDto Batch,
    bool PartialRevoked);

/// <summary>批次列表分页结果（T-2）——Items + 总数。</summary>
/// <param name="Items">当前页批次（统计已聚合）。</param>
/// <param name="Total">过滤后批次总数。</param>
public sealed record CodeBatchPageDto(
    IReadOnlyList<CodeBatchDto> Items,
    int Total);

/// <summary>券明细分页结果（T-2）——掩码行列表（明文不出现在列表——掩码/状态/归属/时间）。</summary>
/// <param name="Items">当前页码行（倒序 RedeemedAtUtc/Id；越界=空页）。</param>
/// <param name="Total">该批（状态过滤后）码行总数。</param>
public sealed record CodePageDto(
    IReadOnlyList<CodeMaskedRowDto> Items,
    int Total);

/// <summary>券掩码行（T-2 明细分页行）——<c>CodeMasked</c> 已脱敏（库中仅存脱敏值 + SHA256 哈希，明文不落库）。</summary>
/// <param name="Id">码行主键（单码吊销 <c>RevokeCodeAsync</c> 直达——B1 §4.7 明细页操作；服务端行键，非明文派生物）。</param>
/// <param name="CodeMasked">脱敏码（如 "EDU-AB12-****-EF56"）。</param>
/// <param name="Status">状态（0 Available/1 Redeemed/2 Expired/3 Revoked）。</param>
/// <param name="RedeemedByUId">兑换人平台内部 id（未兑换为 null）。</param>
/// <param name="RedeemedAtUtc">兑换时间（UTC，未兑换为 null）。</param>
public sealed record CodeMaskedRowDto(
    long Id,
    string CodeMasked,
    int Status,
    string? RedeemedByUId,
    DateTime? RedeemedAtUtc);

/// <summary>
/// 按明文哈希读码 lookup DTO（v0.3.0 T-5）——只读预检非兑换承诺（目标匹配/吊销/test 拒兑判定归消费方兑换编排，CAS 仍唯一裁决）。
/// <paramref name="BatchTest"/>/<paramref name="Channel"/> 头行两跳取（BatchId null → 均 null）；调用不计兑换频控。
/// </summary>
/// <param name="Id">码行 Id。</param>
/// <param name="TargetAppId">兑换目标应用 id。</param>
/// <param name="Status">码状态（0 Available/1 Redeemed/2 Expired/3 Revoked）。</param>
/// <param name="ExpireAtUtc">码有效期（UTC，null=不过期）。</param>
/// <param name="BatchId">批次软引用（null=手工造码）。</param>
/// <param name="BatchTest">批次测试标记（两跳查头；BatchId null → null）。</param>
/// <param name="Channel">批次发行渠道（两跳查头；BatchId null → null）。</param>
public sealed record CodeBatchLookupDto(
    long Id,
    string TargetAppId,
    int Status,
    DateTime? ExpireAtUtc,
    long? BatchId,
    bool? BatchTest,
    string? Channel);