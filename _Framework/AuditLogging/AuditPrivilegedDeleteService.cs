using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 特权删除实现（C4）——访问策略放行 → 先写业务审计留痕（脱敏快照）→ 再物理删。
    /// <para>留痕写失败按 <c>AuditFailurePolicyOptions.FailOpen</c> 决定（严格模式 consumer 设 false → 删除中止）。</para>
    /// </summary>
    [DiContractIgnore]
    internal sealed class AuditPrivilegedDeleteService : DomainServiceBase, IAuditPrivilegedDeleteService
    {
        private AuditLogEntityDataService? _invocation;
        private AuditLogEntityDataService Invocation => _invocation ??= User.Use<AuditLogEntityDataService>();

        private BusinessAuditEntityDataService? _business;
        private BusinessAuditEntityDataService Business => _business ??= User.Use<BusinessAuditEntityDataService>();

        private IAuditAccessPolicy? _policy;
        private IAuditAccessPolicy Policy => _policy ??= User.Use<IAuditAccessPolicy>();

        private IBusinessAuditWriter? _writer;
        private IBusinessAuditWriter Writer => _writer ??= User.GetService<IBusinessAuditWriter>();

        private IReadOnlyList<IAuditFacetReader>? _readers;
        private IReadOnlyList<IAuditFacetReader> Readers =>
            _readers ??= (User.GetService<IEnumerable<IAuditFacetReader>>() ?? Enumerable.Empty<IAuditFacetReader>()).ToList();

        public AuditPrivilegedDeleteService(IDomainUser user) : base(user) { }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(AuditFacet facet, long id, string reason, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("特权删除必须提供原因。", nameof(reason));

            if (facet == AuditFacet.Security)
                throw new AuditAccessDeniedException("安全日志 append-only，不支持特权删除。");

            var decision = await Policy.EvaluateAsync(
                new AuditAccessRequest(AuditOperation.Delete, User.UserId, User.TenantId?.ToString(), User.IsAuthenticated), ct);
            if (!decision.Allowed)
                throw new AuditAccessDeniedException(decision.Reason);

            var snapshot = await TrySnapshotAsync(facet, id, ct);
            await Writer.SaveStrictAsync(new BusinessAuditEntry(
                Actor,
                new AuditTarget("audit", $"{facet}:{id}"),
                "Audit.PrivilegedDelete",
                DateTimeOffset.UtcNow,
                Summary: reason,
                Attributes: snapshot), ct);

            return facet switch
            {
                AuditFacet.Invocation => await Invocation.DeleteAsync(id, ct),
                AuditFacet.Business => await Business.DeleteAsync(id, ct),
                _ => throw new AuditFacetUnavailableException(facet),
            };
        }

        private AuditActor Actor => User.UserId is not null
            ? AuditActor.User(User.UserId, User.UserName)
            : AuditActor.System();

        private async Task<IReadOnlyDictionary<string, object?>?> TrySnapshotAsync(
            AuditFacet facet, long id, CancellationToken ct)
        {
            var reader = Readers.FirstOrDefault(r => r.Facet == facet);
            if (reader is null) return null;

            var record = await reader.GetDetailAsync(id, ct);
            if (record is null) return null;

            return new Dictionary<string, object?>
            {
                ["facet"] = facet.ToString(),
                ["recordId"] = id,
                ["actorType"] = record.Actor?.Type.ToString(),
                ["actorId"] = record.Actor?.Id,
                ["targetType"] = record.Target?.TargetType,
                ["targetId"] = record.Target?.TargetId,
                ["actionType"] = record.ActionType,
                ["summary"] = record.Summary,
                ["executionTime"] = record.ExecutionTime,
            };
        }
    }
}
