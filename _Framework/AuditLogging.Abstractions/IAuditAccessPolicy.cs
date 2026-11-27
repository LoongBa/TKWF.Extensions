using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>审计操作类型（权限/数据域判定）。</summary>
    public enum AuditOperation
    {
        /// <summary>查询本域（仅本人/本租户）。</summary>
        QueryOwn = 0,

        /// <summary>查询全量（跨域）。</summary>
        QueryAll = 1,

        /// <summary>读取详情（含敏感字段）。</summary>
        Detail = 2,

        /// <summary>导出。</summary>
        Export = 3,

        /// <summary>特权删除（不可变模式的例外）。</summary>
        Delete = 4,

        /// <summary>保留期清理。</summary>
        Purge = 5,
    }

    /// <summary>访问请求（<paramref name="IsAuthenticated"/> 供 QueryOwn 认证判定）。</summary>
    public sealed record AuditAccessRequest(
        AuditOperation Operation, string? UserId = null, string? TenantId = null, bool IsAuthenticated = false);

    /// <summary>访问决策。</summary>
    public sealed record AuditAccessDecision(bool Allowed, string? Reason = null)
    {
        public static AuditAccessDecision Allow() => new(true);
        public static AuditAccessDecision Deny(string? reason = null) => new(false, reason);
    }

    /// <summary>
    /// 审计访问策略（<b>fail-closed 默认</b>）。
    /// <para>默认实现（主包）：<c>QueryOwn</c> 已认证放行；<c>QueryAll/Detail/Export/Delete/Purge</c>
    /// 需 <see cref="AuditPermissionNames"/> 授权，否则仅当 <c>AuditAccessPolicyOptions.AllowWithoutPermissions=true</c> 放行（默认 false → 拒绝）。
    /// 策略独立于 Permissions 扩展生效（Permissions 未启用时仍 fail-closed）。</para>
    /// </summary>
    public interface IAuditAccessPolicy
    {
        Task<AuditAccessDecision> EvaluateAsync(AuditAccessRequest request, CancellationToken ct = default);
    }
}
