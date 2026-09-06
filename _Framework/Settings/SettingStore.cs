using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.Settings
{
    /// <summary>
    /// 设置存储实现——经 <see cref="SettingEntityDataService"/>（SG1/xCodeGen 生成的 DataService）委托持久化，
    /// 遵循数据访问红线（2026-09-07 用户裁定）：扩展不直接注入 IFreeSql / IEntityDAC，只依赖 DataService。
    /// <para>异常静默处理：操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。</para>
    /// </summary>
    internal sealed class SettingStore : ISettingStore
    {
        private readonly SettingEntityDataService _dataService;
        private readonly ILogger<SettingStore> _logger;

        public SettingStore(SettingEntityDataService dataService, ILogger<SettingStore> logger)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<SettingEntity?> GetAsync(string name, string providerName, string? providerKey, CancellationToken ct = default)
        {
            try
            {
                return await _dataService.GetByKeyAsync(name, providerName, providerKey, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "设置读取失败: Name={Name}, Provider={ProviderName}/{ProviderKey}", name, providerName, providerKey);
                return null;
            }
        }

        public async Task<IReadOnlyList<SettingEntity>> GetListAsync(string providerName, string? providerKey, CancellationToken ct = default)
        {
            try
            {
                return await _dataService.GetListByProviderAsync(providerName, providerKey, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "设置列表读取失败: Provider={ProviderName}/{ProviderKey}", providerName, providerKey);
                return Array.Empty<SettingEntity>();
            }
        }

        public async Task SetAsync(string name, string? value, string providerName, string? providerKey, string? description, CancellationToken ct = default)
        {
            try
            {
                var now = DateTimeOffset.Now;

                var entity = new SettingEntity
                {
                    Name = name,
                    Value = value,
                    ProviderName = providerName,
                    ProviderKey = providerKey,
                    Description = description,
                    IsVisibleToClients = true,
                    CreateTime = now,
                    UpdateTime = now
                };
                await _dataService.UpsertByKeyAsync(entity, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "设置写入失败: Name={Name}, Provider={ProviderName}/{ProviderKey}", name, providerName, providerKey);
            }
        }

        public async Task DeleteAsync(string name, string providerName, string? providerKey, CancellationToken ct = default)
        {
            try
            {
                await _dataService.DeleteByKeyAsync(name, providerName, providerKey, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "设置删除失败: Name={Name}, Provider={ProviderName}/{ProviderKey}", name, providerName, providerKey);
            }
        }
    }
}
