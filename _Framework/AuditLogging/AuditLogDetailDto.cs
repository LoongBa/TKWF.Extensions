using System;

namespace TKWF.Ext.AuditLogging;

/// <summary>
/// 审计日志详情 DTO（V0.4.0 管理 API）——含 <see cref="ArgumentsJson"/>（脱敏参数 JSON），仅详情端点暴露。
/// <para>与 <see cref="AuditLogListItemDto"/> 的关系（Oracle P2-5 厘清）：列表 DTO 裁剪（D5 安全决策——
/// 不含 ArgumentsJson，防参数结构泄露）；详情 DTO 显式暴露（供审计查看，须消费方控制器级 <c>[RequirePermission]</c> 门控）。</para>
/// <para>独立手写 record（非 xCodeGen 生成）——与 <c>AuditLogEntityDto</c>（全字段含 ArgumentsJson）区分：详情端点返回此 DTO，标准端点已被 ExcludeMethods 排除。</para>
/// </summary>
public sealed record AuditLogDetailDto(
    long Id,
    string? UserName,
    string? UserId,
    string ServiceName,
    string MethodName,
    string? ArgumentsJson,
    DateTime ExecutionTime,
    int DurationMs,
    bool Success,
    string? Exception,
    string? CorrelationId,
    DateTimeOffset CreateTime);
