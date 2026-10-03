using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Settings
{
    /// <summary>
    /// 设置存储实现——经 <see cref="SettingEntityDataService"/>（SG1/xCodeGen 生成的 DataService）委托持久化，
    /// 遵循数据访问红线（2026-09-07 用户裁定）：扩展不直接注入 IFreeSql / IEntityDAC，只依赖 DataService。
    /// <para>异常静默处理：操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。</para>
    /// <para>ADR88/DI004（A 批整改）：DataService 不再构造注入——经 <see cref="IDomainUser.Use{TDomainService}()"/> 懒加载解析。</para>
    /// </summary>
    internal sealed class SettingStore : ISettingStore
    {
        private readonly IDomainUser _user;
        private readonly ILogger<SettingStore> _logger;

        private SettingEntityDataService? _dataService;
        private SettingEntityDataService DataService => _dataService ??= _user.Use<SettingEntityDataService>();

        public SettingStore(IDomainUser user, ILogger<SettingStore> logger)
        {
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<SettingEntity?> GetAsync(string name, string providerName, string? providerKey, CancellationToken ct = default)
        {
            try
            {
                return await DataService.GetByKeyAsync(name, providerName, providerKey, ct);
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
                return await DataService.GetListByProviderAsync(providerName, providerKey, ct);
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
                await DataService.UpsertByKeyAsync(entity, ct);
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
                await DataService.DeleteByKeyAsync(name, providerName, providerKey, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "设置删除失败: Name={Name}, Provider={ProviderName}/{ProviderKey}", name, providerName, providerKey);
            }
        }
    }
}