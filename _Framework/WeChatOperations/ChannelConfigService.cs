using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Transactions;
using TKWF.Ext.Permissions.Abstractions;

namespace TKWF.Ext.WeChatOperations;

/// <summary>
/// 公众号渠道配置门面（两源并存 DB 权威 + Options 降级——设计文档 §3.3.4）。
/// <para><b>凭证安全（Oracle P1）</b>：<see cref="SaveAsync"/> 入参三凭证字段（AppSecret/Token/EncodingAESKey）
/// 为<b>明文</b>——门面经 keyed <see cref="TKW.Framework.Domain.KeyManagement.ISymmetricKeyProvider"/>
/// AES-GCM 加密落库（<c>AppSecretCipher/TokenCipher/EncodingAESKeyCipher</c>）；DTO 投影
/// (<see cref="WeChatChannelConfigDto"/>) <b>不含凭证</b>——密文不回显，解密仅出站瞬时（DbWeChatCredentialResolver）。</para>
/// <para>消费方：<c>User.Use&lt;IChannelConfigService&gt;()</c>（AddConstructibleService 守卫工厂，帧内解析）。</para>
/// </summary>
public interface IChannelConfigService : IDomainService
{
    /// <summary>按 AppId 查询公众号配置（DTO 投影不含凭证密文；未命中 → null）。</summary>
    [RequirePermission("WeChatOperations.Query")]
    Task<WeChatChannelConfigDto?> GetAsync(string appId, CancellationToken ct = default);

    /// <summary>全部公众号配置列表（DTO 投影不含凭证密文）。</summary>
    [RequirePermission("WeChatOperations.Query")]
    Task<IReadOnlyList<WeChatChannelConfigDto>> ListAsync(CancellationToken ct = default);

    /// <summary>保存公众号配置——新（Id=0）创建 / 已有更新；<paramref name="appSecretPlain"/>/<paramref name="tokenPlain"/>/
    /// <paramref name="encodingAesKeyPlain"/> 为<b>明文</b>（null = 保持库中既有密文不变）——门面加密落库。</summary>
    [RequirePermission("WeChatOperations.Manage")]
    Task<long> SaveAsync(WeChatChannelConfigEntity entity, string? appSecretPlain = null, string? tokenPlain = null, string? encodingAesKeyPlain = null, CancellationToken ct = default);

    /// <summary>删除公众号配置（物理删——配置删除后出站凭证经 Options 降级解析）。</summary>
    [RequirePermission("WeChatOperations.Manage")]
    Task<bool> DeleteAsync(long id, CancellationToken ct = default);
}

/// <inheritdoc />
[DiContractIgnore]
internal sealed class ChannelConfigService : DomainServiceBase, IChannelConfigService
{
    private readonly TKW.Framework.Domain.KeyManagement.ISymmetricKeyProvider _keys;

    private WeChatChannelConfigEntityDataService? _ds;
    private WeChatChannelConfigEntityDataService Ds => _ds ??= User.Use<WeChatChannelConfigEntityDataService>();

    public ChannelConfigService(
        IDomainUser user,
        [Microsoft.Extensions.DependencyInjection.FromKeyedServices(WeChatOperationsKeyProviderKeys.WeChatOperations)]
        TKW.Framework.Domain.KeyManagement.ISymmetricKeyProvider keys) : base(user)
    {
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
    }

    /// <inheritdoc />
    public async Task<WeChatChannelConfigDto?> GetAsync(string appId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(appId))
            return null;
        var entity = await Ds.EntityGetAsync(e => e.AppId == appId, ct);
        return entity is null ? null : ToDto(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WeChatChannelConfigDto>> ListAsync(CancellationToken ct = default)
    {
        var entities = await Ds.EntitySelectAsync(null, 0, 1000, q => q.OrderBy(e => e.ChannelId), ct);
        var list = new List<WeChatChannelConfigDto>(entities.Count);
        foreach (var e in entities) list.Add(ToDto(e));
        return list;
    }

    /// <inheritdoc />
    public async Task<long> SaveAsync(WeChatChannelConfigEntity entity, string? appSecretPlain = null, string? tokenPlain = null, string? encodingAesKeyPlain = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (string.IsNullOrWhiteSpace(entity.ChannelId)) throw new ArgumentException("ChannelId 必填", nameof(entity));
        if (string.IsNullOrWhiteSpace(entity.AppId)) throw new ArgumentException("AppId 必填", nameof(entity));

        // ChannelId/AppId 冲突预查（唯一索引 UX——TOCTOU 由数据库兜底）
        var existing = await Ds.EntityGetAsync(e => e.ChannelId == entity.ChannelId || e.AppId == entity.AppId, ct);
        if (existing is not null && (existing.Id != entity.Id || (entity.Id == 0 && (existing.ChannelId == entity.ChannelId || existing.AppId == entity.AppId))))
        {
            // 新建时撞已有（ChannelId/AppId 任一重复）→ 拒绝；更新时其它行撞 → 拒绝
            var collide = existing.Id != entity.Id
                || (entity.Id == 0 && existing.ChannelId == entity.ChannelId)
                || (entity.Id == 0 && existing.AppId == entity.AppId);
            if (collide)
                throw new InvalidOperationException($"公众号配置冲突：ChannelId/AppId 已被占用（Id={existing.Id}）");
        }

        // 明文 → AES-GCM 加密落库（null = 保持既有密文不变——更新时不覆盖）
        var now = DateTime.UtcNow;
        if (entity.Id == 0)
        {
            entity.AppSecretCipher = EncryptOrEmpty(appSecretPlain);
            entity.TokenCipher = EncryptOrEmpty(tokenPlain);
            entity.EncodingAESKeyCipher = EncryptOrEmpty(encodingAesKeyPlain);
            entity.CreateTime = now;
            entity.UpdateTime = now;
            var created = await Ds.EntityCreateAsync(entity, ct);
            return created.Id;
        }

        // 更新——逐字段加密边界（变更字段才覆盖密文；其余保持）
        var db = await Ds.EntityGetAsync(e => e.Id == entity.Id, ct);
        if (db is null) throw new InvalidOperationException($"公众号配置不存在：Id={entity.Id}");

        db.ChannelId = entity.ChannelId;
        db.AppId = entity.AppId;
        db.DisplayName = entity.DisplayName;
        db.Remark = entity.Remark;
        db.IsEnabled = entity.IsEnabled;
        db.ChannelType = entity.ChannelType;
        if (appSecretPlain is not null) db.AppSecretCipher = _keys.Encrypt(appSecretPlain);
        if (tokenPlain is not null) db.TokenCipher = _keys.Encrypt(tokenPlain);
        if (encodingAesKeyPlain is not null) db.EncodingAESKeyCipher = _keys.Encrypt(encodingAesKeyPlain);
        db.UpdateTime = now;
        await Ds.EntityUpdateAsync(db, ct);
        return db.Id;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        var affected = await Ds.EntityDeleteBatchAsync(new[] { id }, ct);
        return affected > 0;
    }

    private string EncryptOrEmpty(string? plain)
        => string.IsNullOrWhiteSpace(plain) ? string.Empty : _keys.Encrypt(plain);

    /// <summary>DTO 投影——不含凭证密文（Oracle P1 安全：密文不回显）。</summary>
    private static WeChatChannelConfigDto ToDto(WeChatChannelConfigEntity e)
        => new(
            Id: e.Id,
            ChannelId: e.ChannelId,
            AppId: e.AppId,
            DisplayName: e.DisplayName,
            Remark: e.Remark,
            IsEnabled: e.IsEnabled,
            ChannelType: e.ChannelType,
            HasCredentials: !string.IsNullOrWhiteSpace(e.AppSecretCipher) || !string.IsNullOrWhiteSpace(e.TokenCipher) || !string.IsNullOrWhiteSpace(e.EncodingAESKeyCipher),
            CreateTime: e.CreateTime,
            UpdateTime: e.UpdateTime);
}

/// <summary>
/// 公众号配置投影 DTO（<b>不含凭证密文</b>——Oracle P1 DTO 投影安全：AppSecret/Token/EncodingAESKey
/// 明文与密文均不回显；<see cref="HasCredentials"/> 仅指示是否已配置凭证）。</summary>
/// <param name="Id">主键。</param>
/// <param name="ChannelId">channel 实例 id。</param>
/// <param name="AppId">公众号 AppId（出站凭证解析键）。</param>
/// <param name="DisplayName">显示名称。</param>
/// <param name="Remark">备注（可空）。</param>
/// <param name="IsEnabled">启用。</param>
/// <param name="ChannelType">通道类型（wechat_event / wechat_oauth 等）。</param>
/// <param name="HasCredentials">是否已配置凭证（AppSecret/Token/EncodingAESKey 任一密文非空）。</param>
/// <param name="CreateTime">创建时间（UTC）。</param>
/// <param name="UpdateTime">更新时间（UTC）。</param>
public sealed record WeChatChannelConfigDto(
    long Id,
    string ChannelId,
    string AppId,
    string DisplayName,
    string? Remark,
    bool IsEnabled,
    string ChannelType,
    bool HasCredentials,
    DateTime CreateTime,
    DateTime UpdateTime);