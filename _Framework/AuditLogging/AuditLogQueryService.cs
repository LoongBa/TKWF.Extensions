using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 审计日志查询服务实现（internal sealed）——经 <see cref="AuditLogEntityDataService"/>（SG1 DataService）委托查询。
    /// <para>异常静默处理：查询失败时记录 Warning 日志并返回空结果（不抛出异常，不阻塞消费方）。</para>
    /// <para>数据访问红线整改（2026-09-07）：不直接注入 IFreeSql，动态 Where 用 Expression API 拼 predicate。</para>
    /// <para>V4.10.53（领域自治根治，ADR90，正确路线）：继承 <see cref="DomainServiceBase"/>（非泛型主实现）——
    /// 经基类 <c>User</c> 获取用户上下文（IDomainUser 永不注册 DI——D01；旧 TryAddScoped 构造注入 IDomainUser
    /// 生产解析必失败）。<c>[DiContractIgnore]</c> 豁免 DI001（AddConstructibleService 运行时手写注册非 SG DI 契约目标）。
    /// DataService 仍经 <c>User.Use&lt;AuditLogEntityDataService&gt;()</c> NoAop 懒加载（DI004 零豁免）。
    /// 注册形态改 <c>AddConstructibleService&lt;IAuditLogQueryService, AuditLogQueryService&gt;</c>（接口可构造守卫工厂）。</para>
    /// </summary>
    [DiContractIgnore]
    internal sealed class AuditLogQueryService : DomainServiceBase, IAuditLogQueryService
    {
        private const int DefaultTake = 50;
        private const int MaxTake = 200;

        private readonly ILogger<AuditLogQueryService> _logger;

        private AuditLogEntityDataService? _dataService;
        private AuditLogEntityDataService DataService => _dataService ??= User.Use<AuditLogEntityDataService>();

        public AuditLogQueryService(IDomainUser user, ILogger<AuditLogQueryService> logger)
            : base(user)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<AuditLogPagedResult> GetListAsync(AuditLogQueryInput query, CancellationToken ct = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            try
            {
                // V0.4.0（Oracle P2-2 单一真相源）：委托 DataService.SearchLogsAsync——谓词构建/分页/裁剪映射集中于此，
                // 管理 API 端点与服务层共享同一查询路径（不再本地 BuildPredicate）。
                return await DataService.SearchLogsAsync(query, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "审计日志查询失败: GetListAsync");
                return new AuditLogPagedResult(0, Array.Empty<AuditLogListItemDto>());
            }
        }

        /// <inheritdoc />
        public async Task<long> CountAsync(AuditLogQueryInput query, CancellationToken ct = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            try
            {
                // V0.4.0（Oracle P2-2 单一真相源）：谓词构建委托 DataService.BuildQueryPredicate（与 SearchLogsAsync 共享）。
                var predicate = AuditLogEntityDataService.BuildQueryPredicate(query);
                return await DataService.CountAsync(predicate, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "审计日志统计失败: CountAsync");
                return 0;
            }
        }
    }
}