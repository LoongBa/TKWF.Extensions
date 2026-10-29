using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Transactions;
using TKWF.Ext.Federation;
using TKWF.Ext.Permissions.Abstractions;
using TKWF.Ext.WeChatOperations.DTOs;

namespace TKWF.Ext.WeChatOperations;

/// <summary>
/// 公众号渠道配置门面（两源并存 DB 权威 + Options 降级——设计文档 §3.3.4）。
/// <para><b>凭证安全（Oracle P1）</b>：<see cref="SaveAsync"/> 入参经 <see cref="SaveChannelRequest"/> wire DTO——
/// 三凭证字段（AppSecret/Token/EncodingAESKey）为<b>明文</b>经 body 传输（V0.4.0 起 GenerateController
/// 端点 [FromBody] 绑定，防 query string URL 泄漏）；门面经 keyed
/// <see cref="TKW.Framework.Domain.KeyManagement.ISymmetricKeyProvider"/> AES-GCM 加密落库
/// （<c>AppSecretCipher/TokenCipher/EncodingAESKeyCipher</c>）；DTO 投影 (<see cref="WeChatChannelConfigDto"/>)
/// <b>不含凭证</b>——密文不回显，解密仅出站瞬时（DbWeChatCredentialResolver）。</para>
/// <para>REST 端点（V0.4.0 生成控制器——消费方 SG1b 经 <c>[GenerateController]</c> 聚合 ADR47）：</para>
/// <list type="bullet">
/// <item><c>GET /api/wechat-ops/channels</c> → <see cref="ListAsync"/>（List 前缀推导 GET）</item>
/// <item><c>POST /api/wechat-ops/channels</c> → <see cref="SaveAsync"/>（Create 前缀推导 POST；单复杂参数 SaveChannelRequest [FromBody]）</item>
/// </list>
/// <para>消费方：<c>User.Use&lt;IChannelConfigService&gt;()</c>（AddConstructibleService 守卫工厂，帧内解析）。</para>
/// </summary>
public interface IChannelConfigService : IDomainService
{
    /// <summary>按 AppId 查询公众号配置（DTO 投影不含凭证密文；未命中 → null）。
    /// <para><c>ExposeRest=false</c>：单查为运营内部能力（列表已含全部配置），不生成 REST 端点——对齐既有 15 端点映射。</para></summary>
    [RequirePermission("WeChatOperations.Query")]
    [ApiExpose(ExposeRest = false)]
    Task<WeChatChannelConfigDto?> GetAsync(string appId, CancellationToken ct = default);

    /// <summary>全部公众号配置列表（DTO 投影不含凭证密文）。<para>REST：<c>GET /api/wechat-ops/channels</c>。</para></summary>
    [RequirePermission("WeChatOperations.Query")]
    [ApiExpose(RestRoute = "/channels", RestMethod = "GET")]
    Task<IReadOnlyList<WeChatChannelConfigDto>> ListAsync(CancellationToken ct = default);

    /// <summary>保存公众号配置——新（Id=0）创建 / 已有更新；<paramref name="request"/> 承载凭证明文
    /// （AppSecret/Token/EncodingAESKey 为<b>明文</b>，null = 保持库中既有密文不变）——门面加密落库。
    /// <para>REST：<c>POST /api/wechat-ops/channels</c>（单复杂参数 SaveChannelRequest → [FromBody] 防 URL 凭证泄漏）。</para></summary>
    [RequirePermission("WeChatOperations.Manage")]
    [ApiExpose(RestRoute = "/channels", RestMethod = "POST")]
    Task<long> SaveAsync(SaveChannelRequest request, CancellationToken ct = default);

    /// <summary>删除公众号配置（物理删——配置删除后出站凭证经 Options 降级解析）。
    /// <para><c>ExposeRest=false</c>：删除为高操作权限能力，对齐既有 15 端点映射（现无删除端点）不新增暴露面。</para></summary>
    [RequirePermission("WeChatOperations.Manage")]
    [ApiExpose(ExposeRest = false)]
    Task<bool> DeleteAsync(long id, CancellationToken ct = default);
}

/// <inheritdoc />
[DiContractIgnore]
[GenerateController]
public sealed class ChannelConfigService : DomainServiceBase, IChannelConfigService
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
    public async Task<long> SaveAsync(SaveChannelRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ChannelId)) throw new ArgumentException("ChannelId 必填", nameof(request));
        if (string.IsNullOrWhiteSpace(request.AppId)) throw new ArgumentException("AppId 必填", nameof(request));

        // D03 1.2 通道形态校验（fail-fast——误传协议类型 wechat_oauth/wechat_event 被拦截，Oracle6 条件 4 红线）
        var channelForm = string.IsNullOrWhiteSpace(request.ChannelForm) ? "mp_service" : request.ChannelForm.Trim().ToLowerInvariant();
        if (!ChannelTypeRegistry.IsLegal("wechat", channelForm))
            throw new ArgumentException($"通道形态非法：{request.ChannelForm}（合法值见 ChannelTypeRegistry wechat 域）", nameof(request));
        var verification = string.IsNullOrWhiteSpace(request.Verification) ? "none" : request.Verification.Trim().ToLowerInvariant();
        if (verification is not ("none" or "verified"))
            throw new ArgumentException($"认证状态非法：{request.Verification}（none/verified）", nameof(request));

        // 构造实体（wire DTO → 领域实体——凭证承载于 DTO 明文，服务端加密落库）
        var entity = new WeChatChannelConfigEntity
        {
            Id = request.Id ?? 0,
            ChannelId = request.ChannelId,
            AppId = request.AppId,
            DisplayName = request.DisplayName ?? "",
            Remark = request.Remark,
            IsEnabled = request.IsEnabled,
            ChannelType = request.ChannelType ?? "wechat_event",
            ChannelForm = channelForm,          // D03 1.2 新列（形态——默认 mp_service 存量兼容）
            Verification = verification,        // D03 1.2 新列（认证状态——默认 none）
            Alias = string.IsNullOrWhiteSpace(request.Alias) ? null : request.Alias.Trim().ToLowerInvariant(),   // D03 1.2 新列（对外别名——1.6 守卫另立）
        };

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
            entity.AppSecretCipher = EncryptOrEmpty(request.AppSecret);
            entity.TokenCipher = EncryptOrEmpty(request.Token);
            entity.EncodingAESKeyCipher = EncryptOrEmpty(request.EncodingAesKey);
            entity.CreateTime = now;
            entity.UpdateTime = now;
            var created = await Ds.EntityCreateAsync(entity, ct);
            await PublishChangedAsync(created.ChannelId, created.UpdateTime.Ticks);   // D03 三机制-事件失效（落库后发布）
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
        db.ChannelForm = entity.ChannelForm;      // D03 1.2 新列透传
        db.Verification = entity.Verification;    // D03 1.2 新列透传
        db.Alias = entity.Alias;                  // D03 1.2 新列透传（1.6 不可变守卫另立）
        if (request.AppSecret is not null) db.AppSecretCipher = _keys.Encrypt(request.AppSecret);
        if (request.Token is not null) db.TokenCipher = _keys.Encrypt(request.Token);
        if (request.EncodingAesKey is not null) db.EncodingAESKeyCipher = _keys.Encrypt(request.EncodingAesKey);
        db.UpdateTime = now;
        await Ds.EntityUpdateAsync(db, ct);
        await PublishChangedAsync(db.ChannelId, db.UpdateTime.Ticks);   // D03 三机制-事件失效（落库后发布）
        return db.Id;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        var db = await Ds.EntityGetAsync(e => e.Id == id, ct);
        var affected = await Ds.EntityDeleteBatchAsync(new[] { id }, ct);
        if (affected > 0 && db is not null)
            await PublishChangedAsync(db.ChannelId, db.UpdateTime.Ticks);   // D03 三机制-事件失效（删除后发布）
        return affected > 0;
    }

    /// <summary>
    /// 落库后发布 <see cref="ChannelConfigChanged"/> 域事件（D03 §4.2.3 事件失效——<see cref="ChannelConfigChangedHandler"/>
    /// 订阅失效缓存命中通道；对齐 <see cref="FollowChangedEvent"/> 发布形态——经 <c>User.GetService&lt;ILocalEventBus&gt;()</c>
    /// AOP 帧内收集，UoW 提交后派发）。<paramref name="dataVersion"/> = 数据版本快照（UpdateTime.Ticks——Obs5 定稿）。
    /// </summary>
    private async Task PublishChangedAsync(string channelId, long dataVersion)
    {
        try
        {
            var evt = new ChannelConfigChanged(channelId, dataVersion);
            var bus = User.GetService<TKW.Framework.Domain.Events.ILocalEventBus>();
            if (bus is not null)
                await bus.PublishAsync(evt);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 事件失效为旁路（TTL 兜底仍在）——发布失败不阻断写路径（降级矩阵惯例），Debug 留痕
            System.Diagnostics.Debug.WriteLine($"[ChannelConfigService] ChannelConfigChanged 发布失败：{ex.Message}");
        }
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