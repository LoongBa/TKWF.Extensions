using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.Settings
{
    /// <summary>
    /// FreeSql 设置存储实现——将设置映射为 <see cref="SettingEntity"/> 并持久化。
    /// <para>异常静默处理：操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。
    /// 与 <see cref="FreeSqlAuditLogStore"/> 模式一致。</para>
    /// </summary>
    internal sealed class FreeSqlSettingStore : ISettingStore
    {
        private readonly IFreeSql _freeSql;
        private readonly ILogger<FreeSqlSettingStore> _logger;

        public FreeSqlSettingStore(IFreeSql freeSql, ILogger<FreeSqlSettingStore> logger)
        {
            _freeSql = freeSql ?? throw new ArgumentNullException(nameof(freeSql));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<SettingEntity?> GetAsync(string name, string providerName, string? providerKey, CancellationToken ct = default)
        {
            try
            {
                return await _freeSql.Select<SettingEntity>()
                    .Where(s => s.Name == name && s.ProviderName == providerName && s.ProviderKey == providerKey)
                    .FirstAsync(ct);
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
                return await _freeSql.Select<SettingEntity>()
                    .Where(s => s.ProviderName == providerName && s.ProviderKey == providerKey)
                    .ToListAsync(ct);
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

                // Upsert: 先删后插（确保 SQLite/PostgreSQL 等全兼容）
                await _freeSql.Delete<SettingEntity>()
                    .Where(s => s.Name == name && s.ProviderName == providerName && s.ProviderKey == providerKey)
                    .ExecuteAffrowsAsync(ct);

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
                await _freeSql.Insert(entity).ExecuteAffrowsAsync(ct);
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
                await _freeSql.Delete<SettingEntity>()
                    .Where(s => s.Name == name && s.ProviderName == providerName && s.ProviderKey == providerKey)
                    .ExecuteAffrowsAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "设置删除失败: Name={Name}, Provider={ProviderName}/{ProviderKey}", name, providerName, providerKey);
            }
        }
    }
}
