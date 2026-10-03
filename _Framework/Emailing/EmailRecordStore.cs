using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Emailing
{
    /// <summary>
    /// 邮件记录存储实现——经 <see cref="EmailRecordEntityDataService"/>（SG1/xCodeGen 生成的 DataService）委托持久化，
    /// 遵循数据访问红线（2026-09-07 用户裁定）：扩展不直接注入 IFreeSql / IEntityDAC，只依赖 DataService。
    /// <para>异常静默处理：操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。</para>
    /// <para>V4.10.53（领域自治根治，ADR90，正确路线）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c>
    /// 获取用户上下文（<b>IDomainUser 永不注册 DI</b>——D01；旧 TryAddScoped 构造注入 IDomainUser 生产解析必失败）；
    /// <c>[DiContractIgnore]</c> 豁免 DI001；DataService 仍经 <c>User.Use&lt;EmailRecordEntityDataService&gt;()</c>
    /// NoAop 懒加载（DI004 零豁免）。注册形态改 <c>AddConstructibleService&lt;IEmailRecordStore, EmailRecordStore&gt;</c>
    /// （接口可构造守卫工厂——消费方 <c>User.Use&lt;IEmailRecordStore&gt;()</c> 解析）。</para>
    /// </summary>
    [DiContractIgnore]
    internal sealed class EmailRecordStore : DomainServiceBase, IEmailRecordStore
    {
        private EmailRecordEntityDataService? _dataService;
        private readonly ILogger<EmailRecordStore> _logger;

        private EmailRecordEntityDataService DataService => _dataService ??= User.Use<EmailRecordEntityDataService>();

        public EmailRecordStore(IDomainUser user, ILogger<EmailRecordStore> logger)
            : base(user)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<EmailRecordEntity?> GetAsync(long id, CancellationToken ct = default)
        {
            try
            {
                return await DataService.GetEntityByIdAsync(id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "邮件记录读取失败: Id={Id}", id);
                return null;
            }
        }

        public async Task<IReadOnlyList<EmailRecordEntity>> GetListAsync(string? status = null, CancellationToken ct = default)
        {
            try
            {
                return await DataService.GetListByStatusAsync(status, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "邮件记录列表读取失败: Status={Status}", status);
                return Array.Empty<EmailRecordEntity>();
            }
        }

        public async Task SaveAsync(EmailRecordEntity entity, CancellationToken ct = default)
        {
            if (entity == null) return;

            try
            {
                await DataService.UpsertAsync(entity, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "邮件记录保存失败: To={To}, Subject={Subject}", entity.To, entity.Subject);
            }
        }
    }
}
