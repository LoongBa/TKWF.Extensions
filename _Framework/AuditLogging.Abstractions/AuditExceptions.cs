using System;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 审计面不可用（定向查询引用了未注册 Reader 的面）。<b>P7/P14</b>：显式请求不存在的能力必须硬失败，禁静默空结果。
    /// <para>边界映射建议：REST 404 或 501（ErrorCode <c>AUDIT_FACET_UNAVAILABLE</c>）。</para>
    /// </summary>
    public sealed class AuditFacetUnavailableException : Exception
    {
        public AuditFacet Facet { get; }

        public string ErrorCode { get; } = "AUDIT_FACET_UNAVAILABLE";

        public AuditFacetUnavailableException(AuditFacet facet)
            : base($"审计面 {facet} 不可用：对应扩展未启用或未注册 Reader。")
        {
            Facet = facet;
        }
    }

    /// <summary>
    /// 审计访问被拒绝。<b>P14</b>：拒绝语义类型化（不再返回空结果 200）。
    /// <para>边界映射建议：REST 403 ProblemDetails（ErrorCode <c>AUDIT_ACCESS_DENIED</c>）。</para>
    /// </summary>
    public sealed class AuditAccessDeniedException : Exception
    {
        public string ErrorCode { get; } = "AUDIT_ACCESS_DENIED";

        public AuditAccessDeniedException(string? reason = null)
            : base(reason ?? "审计访问被拒绝。")
        {
        }
    }
}
