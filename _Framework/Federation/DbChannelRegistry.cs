using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.KeyManagement;

namespace TKWF.Ext.Federation;

/// <summary>
/// DB 通道注册表实现（多通道联邦 v0.3.0 Phase 2——DB 动态权威层，方案 docs/Federation/多通道联邦-开发方案.md §3.2/3.3）。
/// <para>**只读 DB，纯净无回退依赖**——来源 = <see cref="SsoChannelRegistryEntity"/>（SG1 实体 + DataService）；
/// 不依赖 <see cref="StaticChannelRegistry"/>（跨实现解耦归 <see cref="CompositeChannelRegistry"/> 组合单点）。
/// <see cref="GetAllAsync"/> 枚举全部 DB 行，<see cref="GetAsync"/> 按 ChannelId 精确定位（唯一 UX 索引），
/// <see cref="GetDefaultAsync"/> 返回 IsDefault 标记行（无则为 null——组合层回退静态）。</para>
/// <para>加密语义（方案 M6/M12）：<see cref="SsoChannelRegistryEntity.AppSecretEncrypted"/>/<see cref="SsoChannelRegistryEntity.ExtraJsonEncrypted"/>
/// 均为 AES-GCM 密文列——本实现读行后经 keyed <see cref="ISymmetricKeyProvider"/>（键
/// <see cref="SymmetricKeyProviderKeys.Federation"/>，<c>FederationOptions.SecretEncryptionKeyPath</c> 密钥——
/// FileSymmetricKeyProvider，对齐 <see cref="SsoClientService"/> 既有模式）解密映射 <see cref="ChannelConfig"/>：
/// AppSecret=Decrypt(AppSecretEncrypted)，Extra=JsonSerializer.Deserialize&lt;Dictionary&gt;(Decrypt(ExtraJsonEncrypted))
/// （空串/解密失败 → 空字典容错）；明文不落库。</para>
/// <para>注册偏置：<c>AddScoped&lt;DbChannelRegistry&gt;()</c>（普通 DI——<see cref="CompositeChannelRegistry"/>
/// 守卫工厂 ActivatorUtilities 解析依赖；本实现经基类 <c>User.Use&lt;DbChannelRegistry&gt;()</c> 帧内直建（NoAop）——
/// IDomainUser 永不注册 DI，tkwf-extension §4.3 铁律）。<c>[DiContractIgnore]</c>：运行时手写注册豁免 DI001。</para>
/// </summary>
[DiContractIgnore]
public sealed class DbChannelRegistry : DomainServiceBase, IChannelRegistry
{
    private SsoChannelRegistryEntityDataService? _dataService;
    private readonly ISymmetricKeyProvider _keys;
    private readonly ILogger<DbChannelRegistry> _logger;

    private SsoChannelRegistryEntityDataService DataService => _dataService ??= User.Use<SsoChannelRegistryEntityDataService>();

    /// <summary>构造——IDomainUser 帧内供给（NoAop 直建）；keyed 对称密钥提供者注入（解密 AppSecret/ExtraJson 密文）。</summary>
    public DbChannelRegistry(
        IDomainUser user,
        [FromKeyedServices(SymmetricKeyProviderKeys.Federation)] ISymmetricKeyProvider keys,
        ILogger<DbChannelRegistry> logger)
        : base(user)
    {
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <remarks>DB 读行 → 解密 → 映射 <see cref="ChannelConfig"/>；不存在返回 null（组合层转 CHANNEL_NOT_FOUND）。</remarks>
    public async Task<ChannelConfig?> GetAsync(string channelId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(channelId)) return null;

        var entity = await DataService.EntityGetAsync(m => m.ChannelId == channelId, ct);
        return entity is null ? null : Map(entity);
    }

    /// <inheritdoc />
    /// <remarks>枚举全部 DB 通道（构建通道集合 / 校验 / 管理面）。</remarks>
    public async Task<IReadOnlyList<ChannelConfig>> GetAllAsync(CancellationToken ct = default)
    {
        var entities = await DataService.EntitySelectAsync(null, 0, 10_000, q => q.OrderBy(e => e.ChannelId), ct);
        var result = new List<ChannelConfig>(entities.Count);
        foreach (var entity in entities)
            result.Add(Map(entity));
        return result;
    }

    /// <inheritdoc />
    /// <remarks>默认选区：IsDefault=true 行（多行取首，组合层按序回退静态默认）。</remarks>
    public async Task<ChannelConfig?> GetDefaultAsync(CancellationToken ct = default)
    {
        var entity = await DataService.EntityGetAsync(m => m.IsDefault, ct);
        return entity is null ? null : Map(entity);
    }

    /// <summary>实体 → 统一配置（解密 AppSecret / ExtraJson——AES-GCM 密文列明文不落库）。</summary>
    private ChannelConfig Map(SsoChannelRegistryEntity entity)
        => new()
        {
            ChannelId = entity.ChannelId,
            PlatformType = entity.PlatformType,
            AppId = entity.AppId,
            AppSecret = DecryptNullable(entity.AppSecretEncrypted, entity.ChannelId),
            Extra = DeserializeExtra(entity.ExtraJsonEncrypted, entity.ChannelId),
            IsDefault = entity.IsDefault,
            IsEnabled = entity.IsEnabled,
        };

    /// <summary>解密 AppSecret（空串 → null；解密失败记录 Warning + null——对齐仓库降级矩阵惯例）。</summary>
    private string? DecryptNullable(string? ciphertext, string channelId)
    {
        if (string.IsNullOrEmpty(ciphertext)) return null;
        try
        {
            return _keys.Decrypt(ciphertext);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("通道注册表 AppSecret 解密失败（ChannelId={ChannelId}）——密文损坏：{Ex}",
                channelId, ex.Message);
            return null;
        }
    }

    /// <summary>解密 ExtraJson → 字典（空串/解密失败 → 空字典容错；对齐方案 M6/M12 容错语义）。</summary>
    private IReadOnlyDictionary<string, string?> DeserializeExtra(string? ciphertext, string channelId)
    {
        if (string.IsNullOrEmpty(ciphertext)) return new Dictionary<string, string?>(StringComparer.Ordinal);
        try
        {
            var decrypted = _keys.Decrypt(ciphertext);
            if (string.IsNullOrEmpty(decrypted)) return new Dictionary<string, string?>(StringComparer.Ordinal);
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(decrypted)
                   ?? new Dictionary<string, string?>(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("通道注册表 ExtraJson 解密失败（ChannelId={ChannelId}）——密文损坏：{Ex}",
                channelId, ex.Message);
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }
    }
}