using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.BlobStoring
{
    /// <summary>
    /// Blob 记录存储实现——经 <see cref="BlobRecordEntityDataService"/>（SG1/xCodeGen 生成的 DataService）委托持久化，
    /// 遵循数据访问红线（2026-09-07 用户裁定）：扩展不直接注入 IFreeSql / IEntityDAC，只依赖 DataService。
    /// <para>V4.10.53（领域自治根治，ADR90 正确路线）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c>
    /// 获取用户上下文（IDomainUser 永不注册 DI，旧 TryAddScoped 构造注入 IDomainUser 生产解析必失败——
    /// v0.3.3 同根缺陷）；DataService 仍经 <c>User.Use&lt;BlobRecordEntityDataService&gt;()</c> NoAop 懒加载（DI004 零豁免）。</para>
    /// <para><c>[DiContractIgnore]</c>：运行时手写注册（AddConstructibleService），豁免 SG1a DI001 误报。</para>
    /// <para>异常静默处理：操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。</para>
    /// </summary>
    [DiContractIgnore]
    internal sealed class BlobRecordStore : DomainServiceBase, IBlobRecordStore
    {
        private BlobRecordEntityDataService? _dataService;
        private readonly ILogger<BlobRecordStore> _logger;

        private BlobRecordEntityDataService DataService => _dataService ??= User.Use<BlobRecordEntityDataService>();

        public BlobRecordStore(IDomainUser user, ILogger<BlobRecordStore> logger) : base(user)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<BlobRecordEntity?> GetAsync(long id, CancellationToken ct = default)
        {
            try
            {
                return await DataService.GetEntityByIdAsync(id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Blob 记录读取失败: Id={Id}", id);
                return null;
            }
        }

        public async Task<BlobRecordEntity?> GetByNameAsync(string name, CancellationToken ct = default)
        {
            try
            {
                return await DataService.GetByNameAsync(name, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Blob 记录按名称读取失败: Name={Name}", name);
                return null;
            }
        }

        public async Task<IReadOnlyList<BlobRecordEntity>> GetListAsync(
            string? contentType = null,
            int skip = 0,
            int take = 20,
            CancellationToken ct = default)
        {
            try
            {
                return await DataService.GetListByContentTypeAsync(contentType, skip, take, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Blob 记录列表读取失败: ContentType={ContentType}", contentType);
                return Array.Empty<BlobRecordEntity>();
            }
        }

        public async Task SaveAsync(BlobRecordEntity record, CancellationToken ct = default)
        {
            if (record == null) return;

            try
            {
                // 直接委托 DataService Upsert——按 Id 查存在：存在则更新（保留自增 Id），不存在则插入
                await DataService.UpsertAsync(record, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Blob 记录保存失败: Name={Name}", record.Name);
            }
        }

        public async Task DeleteAsync(long id, CancellationToken ct = default)
        {
            try
            {
                await DataService.DeleteByIdAsync(id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Blob 记录删除失败: Id={Id}", id);
            }
        }
    }
}
