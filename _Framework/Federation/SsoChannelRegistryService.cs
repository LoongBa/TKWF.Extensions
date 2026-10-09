using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Exceptions;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.KeyManagement;

namespace TKWF.Ext.Federation;

/// <summary>
/// 通道注册表服务门面（多通道联邦 v0.3.0 Phase 2——DB 动态权威层写入契约，方案 docs/Federation/多通道联邦-开发方案.md §3.3）。
/// <para>**管理端点不建**（无 [GenerateController]——管理门户不在本扩展模块范围，用户裁定）；服务层方法保留
/// （供静态→DB seed 与消费方自建管理端点经 <c>User.Use&lt;ISsoChannelRegistryService&gt;()</c> 帧内消费）。</para>
    /// <para>写入语义：<see cref="RegisterAsync"/> 创建加密行（ChannelId 冲突抛 <see cref="DomainException"/> CONFLICT——
    /// fail-closed 显式语义，种子/覆盖请用 <see cref="UpdateAsync"/>）；<see cref="UpdateAsync"/> 按 channelId 全量重写
    /// （新 <see cref="ChannelConfig"/> 重加密落库）；<see cref="UnregisterAsync"/> 删行（不存在返回 false）。
    /// <b>对外别名（Phase 3，方案 §3.7 双键）</b>：<see cref="ChannelConfig.Alias"/> 写入
    /// <see cref="SsoChannelRegistryEntity.ChannelAlias"/> 列（可空——缺省 null = 对外用 ChannelId 零迁移）；
    /// alias 非空时校验同 alias 冲突（fail-closed，含 Update 排除本行）。</para>
/// <para>加密：AppSecret/ExtraJson AES-GCM 加密（keyed <see cref="ISymmetricKeyProvider"/>
/// <see cref="SymmetricKeyProviderKeys.Federation"/>），密文落库明文不落库——对齐 <see cref="SsoClientService"/>
/// 既有模式（读取侧 <see cref="DbChannelRegistry"/> 解密映射，本门面只写不读）。</para>
/// <para>数据访问红线：零 Store 伪层/零 IEntityDAC 注入——全部经 <see cref="SsoChannelRegistryEntityDataService"/>
/// 内部转发访问器（Entity*）委托；DataService 经基类 <c>User.Use&lt;具体类&gt;()</c> NoAop 懒加载。
/// IDomainUser 永不注册 DI（经基类 <c>User</c>）；<c>[DiContractIgnore]</c>：运行时手写注册豁免 DI001。</para>
/// </summary>
public interface ISsoChannelRegistryService : IDomainService
{
    /// <summary>注册通道——写入 AES-GCM 加密行；ChannelId 已存在抛 <see cref="DomainException"/>（CONFLICT，fail-closed）；
    /// <see cref="ChannelConfig.Alias"/> 非空时同 alias 二次注册亦抛 CONFLICT（方案 §3.7 双键冲突校验）。</summary>
    Task RegisterAsync(ChannelConfig config, CancellationToken ct = default);

    /// <summary>按 channelId 全量重写通道配置（新 config 重加密落库，<see cref="ChannelConfig.Alias"/> 重写含清空语义）；
    /// 通道不存在返回 false（CHANNEL_NOT_FOUND 语义）。</summary>
    Task<bool> UpdateAsync(string channelId, ChannelConfig config, CancellationToken ct = default);

    /// <summary>注销通道（删行）；通道不存在返回 false（CHANNEL_NOT_FOUND 语义）。</summary>
    Task<bool> UnregisterAsync(string channelId, CancellationToken ct = default);
}

/// <summary>
/// 通道注册表服务实现（internal sealed——对外契约仅 <see cref="ISsoChannelRegistryService"/> 接口）。
/// <para>DataService 经基类 <c>User.Use&lt;SsoChannelRegistryEntityDataService&gt;()</c> NoAop 懒加载；
/// 加解密经注入的 <see cref="ISymmetricKeyProvider"/>（键 <see cref="SymmetricKeyProviderKeys.Federation"/>——
/// <c>FederationOptions.SecretEncryptionKeyPath</c> 密钥，FileSymmetricKeyProvider）；密钥解析在服务层边界
/// （不依赖 DataService——SG1 DataService 纯持久化，对齐 AuthCenter V0.7.0 加密上移先例）。</para>
/// </summary>
[DiContractIgnore]
internal sealed class SsoChannelRegistryService : DomainServiceBase, ISsoChannelRegistryService
{
    private SsoChannelRegistryEntityDataService? _dataService;
    private readonly ISymmetricKeyProvider _keys;
    private readonly ILogger<SsoChannelRegistryService> _logger;

    private SsoChannelRegistryEntityDataService DataService => _dataService ??= User.Use<SsoChannelRegistryEntityDataService>();

    /// <summary>构造——IDomainUser 帧内供给；keyed 对称密钥提供者注入（AppSecret/ExtraJson 加密源）。</summary>
    public SsoChannelRegistryService(
        IDomainUser user,
        [FromKeyedServices(SymmetricKeyProviderKeys.Federation)] ISymmetricKeyProvider keys,
        IServiceProvider serviceProvider,
        ILogger<SsoChannelRegistryService> logger)
        : base(user)
    {
        _ = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <remarks>校验 ChannelId/PlatformType 非空 → 检查唯一 UX 冲突（存在 → DomainException CONFLICT）→
    /// Alias 非空时校验同 alias 冲突（fail-closed）→ AppSecret/Extra 加密落库（CreateTime/UpdateTime = UtcNow）。
    /// <see cref="ChannelConfig.Alias"/> 一并写入 <see cref="SsoChannelRegistryEntity.ChannelAlias"/> 列（方案 §3.7 双键）。</remarks>
    public async Task RegisterAsync(ChannelConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.ChannelId))
            throw new DomainException("通道注册失败：ChannelId 不能为空", DomainException.ErrorCodes.ValidationError);
        if (string.IsNullOrWhiteSpace(config.PlatformType))
            throw new DomainException("通道注册失败：PlatformType 不能为空", DomainException.ErrorCodes.ValidationError);

        var existing = await DataService.EntityGetAsync(m => m.ChannelId == config.ChannelId, ct);
        if (existing != null)
        {
            throw new DomainException(
                $"通道已存在：ChannelId={config.ChannelId}（请用 UpdateAsync 重写或先 UnregisterAsync）",
                DomainException.ErrorCodes.Conflict);
        }

        // 同 alias 二次注册冲突（alias 空白/null 跳过校验——缺省 null = 用 ChannelId 对外，不参与对外名冲突）
        await EnsureAliasAvailableAsync(config.Alias, config.ChannelId, ct);

        var now = DateTime.UtcNow;
        await DataService.EntityCreateAsync(new SsoChannelRegistryEntity
        {
            ChannelId = config.ChannelId,
            PlatformType = config.PlatformType,
            ChannelAlias = config.Alias,
            AppId = config.AppId,
            AppSecretEncrypted = EncryptSecret(config.AppSecret),
            ExtraJsonEncrypted = EncryptExtra(config.Extra),
            IsDefault = config.IsDefault,
            IsEnabled = config.IsEnabled,
            CreateTime = now,
            UpdateTime = now,
        }, ct);
    }

    /// <inheritdoc />
    /// <remarks>按 channelId 定位行（不存在 → false）→ Alias 冲突校验（同 alias 且非本行 → CONFLICT，fail-closed）→
    /// 新 <see cref="ChannelConfig"/> 全量重写（AppSecret/Extra 重加密 + IsDefault/IsEnabled 重写 +
    /// <see cref="SsoChannelRegistryEntity.ChannelAlias"/> = <see cref="ChannelConfig.Alias"/>（含清空语义——
    /// Alias null → ChannelAlias null），UpdateTime=UtcNow）。</remarks>
    public async Task<bool> UpdateAsync(string channelId, ChannelConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(channelId)) return false;

        var entity = await DataService.EntityGetAsync(m => m.ChannelId == channelId, ct);
        if (entity is null) return false;

        // 同 alias 冲突校验（排除本行——允许保留/重写自身 alias）
        await EnsureAliasAvailableAsync(config.Alias, channelId, ct);

        entity.PlatformType = config.PlatformType;
        entity.ChannelAlias = config.Alias;   // 含清空语义（Alias null → ChannelAlias null）
        entity.AppId = config.AppId;
        entity.AppSecretEncrypted = EncryptSecret(config.AppSecret);
        entity.ExtraJsonEncrypted = EncryptExtra(config.Extra);
        entity.IsDefault = config.IsDefault;
        entity.IsEnabled = config.IsEnabled;
        entity.UpdateTime = DateTime.UtcNow;

        await DataService.EntityUpdateAsync(entity, ct);
        return true;
    }

    /// <inheritdoc />
    /// <remarks>按 channelId 定位行（不存在 → false）→ 物理删行（无软删——hasSoftDelete=false）。</remarks>
    public async Task<bool> UnregisterAsync(string channelId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(channelId)) return false;

        var entity = await DataService.EntityGetAsync(m => m.ChannelId == channelId, ct);
        if (entity is null) return false;

        await DataService.EntityDeleteBatchAsync([entity.Id], ct);
        return true;
    }

    /// <summary>同 alias 冲突校验（方案 P3-3——同 alias 二次注册 fail-closed）：alias 空白/null 跳过校验（缺省
    /// null = 用 ChannelId 对外，不参与对外名冲突）；否则查同 alias 且 ChannelId ≠ 本行 → DomainException CONFLICT。</summary>
    private async Task EnsureAliasAvailableAsync(string? alias, string channelId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(alias)) return;

        var conflict = await DataService.EntityGetAsync(
            m => m.ChannelAlias == alias && m.ChannelId != channelId, ct);
        if (conflict != null)
        {
            throw new DomainException(
                $"通道别名已存在：Alias={alias}（已被 ChannelId={conflict.ChannelId} 占用——同 alias 二次注册 fail-closed，请换别名或先注销）",
                DomainException.ErrorCodes.Conflict);
        }
    }

    // ── 私有实现（对齐 SsoClientService 加密边界——服务层加解密，DataService 纯持久化） ──

    /// <summary>AES-GCM 加密 AppSecret（null → 空串密文占位——空等价 null 语义）。</summary>
    private string EncryptSecret(string? appSecret)
        => _keys.Encrypt(appSecret ?? "");

    /// <summary>Extra 字典 → JSON → AES-GCM 整段加密（空字典 → "{}" 密文，解密回读对称）。</summary>
    private string EncryptExtra(IReadOnlyDictionary<string, string?>? extra)
        => _keys.Encrypt(JsonSerializer.Serialize(extra ?? new Dictionary<string, string?>(StringComparer.Ordinal)));
}