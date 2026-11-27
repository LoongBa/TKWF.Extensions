using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 默认审计访问策略（fail-closed）。<c>QueryOwn</c> 放行（宿主已认证）；
    /// 其余敏感操作仅当 <see cref="AuditAccessPolicyOptions.AllowWithoutPermissions"/> 为 true 时放行，否则拒绝。
    /// <para>消费方启用 Permissions 时，可 <c>TryAdd</c> 替换为按 <see cref="AuditPermissionNames"/> 判定的实现。</para>
    /// </summary>
    internal sealed class DefaultAuditAccessPolicy : IAuditAccessPolicy
    {
        private readonly bool _allowWithoutPermissions;

        public DefaultAuditAccessPolicy(IOptions<AuditAccessPolicyOptions> options)
        {
            _allowWithoutPermissions = options?.Value.AllowWithoutPermissions ?? false;
        }

        public Task<AuditAccessDecision> EvaluateAsync(AuditAccessRequest request, CancellationToken ct = default)
        {
            if (request is null)
            {
                return Task.FromResult(AuditAccessDecision.Deny("request 为空"));
            }

            if (request.Operation == AuditOperation.QueryOwn)
            {
                return Task.FromResult(request.IsAuthenticated
                    ? AuditAccessDecision.Allow()
                    : AuditAccessDecision.Deny("未认证（QueryOwn 需已认证，fail-closed）"));
            }

            return Task.FromResult(_allowWithoutPermissions
                ? AuditAccessDecision.Allow()
                : AuditAccessDecision.Deny("审计权限未启用且未显式允许（fail-closed）"));
        }
    }
}
