using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuditLogging;

namespace TKWF.Ext.SecurityLog
{
    /// <summary>
    /// 安全日志存储实现——经 <see cref="SecurityLogEntityDataService"/>（SG1/xCodeGen 生成的 DataService）
    /// 委托持久化，遵循数据访问红线（2026-09-07 用户裁定）：扩展不直接注入 IFreeSql / IEntityDAC，只依赖 DataService。
    /// <para><b>只增不改（Oracle C2）</b>：本实现仅调用 <c>EntityCreateAsync</c>（追加写），
    /// 无任何 Update/Delete 路径——安全日志不可篡改语义。</para>
    /// <para>异常静默处理：落库失败时记录 Warning 日志，不抛出异常（审计不阻断认证流程）。</para>
    /// <para>V4.10.53（领域自治根治，ADR90）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文
    /// （IDomainUser 永不注册 DI，旧 TryAddScoped 构造注入 IDomainUser 生产必失败）；注册形态改
    /// <c>AddConstructibleService&lt;ISecurityLogStore, SecurityLogStore&gt;</c>（接口可构造守卫工厂）。</para>
    /// </summary>
    [DiContractIgnore]
    internal sealed class SecurityLogStore : DomainServiceBase, ISecurityLogStore
    {
        private readonly ILogger<SecurityLogStore> _logger;
        private readonly IServiceProvider? _serviceProvider;

        private SecurityLogEntityDataService? _dataService;
        private SecurityLogEntityDataService DataService => _dataService ??= User.Use<SecurityLogEntityDataService>();

        public SecurityLogStore(IDomainUser user, ILogger<SecurityLogStore> logger, IServiceProvider? serviceProvider = null) : base(user)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = serviceProvider;
        }

        /// <inheritdoc />
        public async Task SaveAsync(SecurityLogEntry entry, CancellationToken ct = default)
        {
            if (entry == null) return;

            try
            {
                var entity = MapToEntity(entry);
                await DataService.EntityCreateAsync(entity, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "安全日志写入失败: {EventType} 用户 {UserName}（不阻断认证流程）",
                    entry.EventType, entry.UserName);
                RaiseFailure(entry, ex);
            }
        }

        /// <summary>落库失败同步信号——可选依赖（未启用 AuditLogging 时 hook 为 null，静默跳过）；回调异常隔离。</summary>
        private void RaiseFailure(SecurityLogEntry entry, Exception ex)
        {
            var hook = _serviceProvider?.GetService<IAuditFailureHook>();
            if (hook is null) return;

            try
            {
                hook.OnPersistFailed(new AuditPersistFailure(
                    AuditFacet.Security,
                    DateTimeOffset.UtcNow,
                    ServiceName: null,
                    MethodName: null,
                    entry.CorrelationId,
                    ex.GetType().Name));
            }
            catch (Exception hookEx)
            {
                _logger.LogWarning(hookEx, "审计失败回调异常（已隔离，不影响安全日志写入/认证流程）");
            }
        }

        private static SecurityLogEntity MapToEntity(SecurityLogEntry entry)
        {
            return new SecurityLogEntity
            {
                EventType = entry.EventType,
                EventCategory = entry.EventCategory,
                UserName = entry.UserName,
                UserId = entry.UserId,
                IpAddress = entry.IpAddress,
                UserAgent = entry.UserAgent,
                Result = entry.Result,
                Detail = entry.Detail,
                CorrelationId = entry.CorrelationId,
                CreateTime = DateTime.UtcNow
            };
        }
    }
}
