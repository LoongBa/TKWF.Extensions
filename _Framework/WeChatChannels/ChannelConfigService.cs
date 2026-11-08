using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Exceptions;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Transactions;
using TKWF.Ext.Federation;

namespace TKWF.Ext.WeChatChannels;

/// <summary>
/// 公众号渠道配置门面实现（2026-11-05 拆分下沉——契约在 WeChatChannels.Abstractions，本类为主包实现）。
/// <para><b>凭证安全（Oracle P1）</b>：<see cref="SaveChannelAsync"/> 入参经 <see cref="SaveChannelRequest"/> wire DTO——
/// 三凭证字段（AppSecret/Token/EncodingAESKey）为<b>明文</b>经 body 传输（V0.4.0 起 GenerateController
/// 端点 [FromBody] 绑定，防 query string URL 泄漏）；门面经 keyed
/// <see cref="TKW.Framework.Domain.KeyManagement.ISymmetricKeyProvider"/> AES-GCM 加密落库
/// （<c>AppSecretCipher/TokenCipher/EncodingAESKeyCipher</c>）；DTO 投影 (<see cref="WeChatChannelConfigDto"/>)
/// <b>不含凭证</b>——密文不回显，解密仅出站瞬时（DbWeChatCredentialResolver）。</para>
/// <para>REST 端点（生成控制器——消费方 SG1b 经 <c>[GenerateController]</c> 聚合 ADR47；
/// 组路径 = <c>TKWF:Api:RestBasePath</c> + <c>[assembly: ExtensionModule("WeChatChannels","wechat-channels")]</c>）：</para>
/// <list type="bullet">
/// <item><c>GET /api/wechat-channels/channels</c> → <see cref="ListChannelsAsync"/>（List 前缀推导 GET）</item>
/// <item><c>POST /api/wechat-channels/channels</c> → <see cref="SaveChannelAsync"/>（Create 前缀推导 POST；单复杂参数 SaveChannelRequest [FromBody]）</item>
/// </list>
/// <para>消费方：<c>User.Use&lt;IChannelConfigService&gt;()</c>（AddConstructibleService 守卫工厂，帧内解析）。</para>
/// </summary>
[DiContractIgnore]
[GenerateController]
public sealed class ChannelConfigService : DomainServiceBase, IChannelConfigService
{
    private readonly TKW.Framework.Domain.KeyManagement.ISymmetricKeyProvider _keys;
    private readonly ILogger<ChannelConfigService>? _logger;

    private WeChatChannelConfigEntityDataService? _ds;
    private WeChatChannelConfigEntityDataService Ds => _ds ??= User.Use<WeChatChannelConfigEntityDataService>();

    /// <summary>D03 1.6 channel alias slug 规则——3-64 位小写字母/数字/连字符，首字符须字母或数字
    /// （值已 Trim + ToLowerInvariant 归一后匹配；请求空白时不参与校验）。</summary>
    /// <summary>审计时间秒级截断（框架 UpdateWhereAsync 参数化 DateTime 秒精度 vs CreateAsync 毫秒——两路径统一防 UpdateTime 倒退；框架缺陷已登记）。</summary>
    private static DateTime TruncateToSecond(DateTime value)
        => new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, value.Kind);

    private static readonly Regex AliasPattern = new("^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$", RegexOptions.Compiled);

    /// <summary>协议通道类型白名单（D03——wechat_oauth / wechat_event 双通道；平台库 ISsoChannel.ChannelType 选区）。
    /// <para>与 <see cref="ChannelTypeRegistry"/>（通道形态 wechat 域）正交——协议类型不经注册表；未知值拒透传存储
    /// （对齐相邻 ChannelForm/Verification 值校验 fail-fast——防下游 ISsoChannel 选区永不命中）。</para></summary>
    private static readonly System.Collections.Generic.HashSet<string> LegalChannelTypes = new(
        System.StringComparer.OrdinalIgnoreCase)
    {
        "wechat_oauth",
        "wechat_event",
    };

    public ChannelConfigService(
        IDomainUser user,
        [Microsoft.Extensions.DependencyInjection.FromKeyedServices(TKW.Framework.Domain.KeyManagement.SymmetricKeyProviderKeys.WeChatChannels)]
        TKW.Framework.Domain.KeyManagement.ISymmetricKeyProvider keys,
        ILogger<ChannelConfigService>? logger = null) : base(user)
    {
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _logger = logger;
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
    public async Task<WeChatChannelConfigDto?> GetByChannelIdAsync(string channelId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(channelId))
            return null;
        var entity = await Ds.EntityGetAsync(e => e.ChannelId == channelId, ct);
        return entity is null ? null : ToDto(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WeChatChannelConfigDto>> ListChannelsAsync(CancellationToken ct = default)
    {
        // 上限 1000 无分页（EntitySelectAsync count 参数硬编码）——通道数量级为个位数~几十（平台手工维护），
        // 一次性全量投影语义保留；超出请评审增分页重载（对齐 ListChannelsAsync 契约——不静默截断）。
        var entities = await Ds.EntitySelectAsync(null, 0, 1000, q => q.OrderBy(e => e.ChannelId), ct);
        var list = new List<WeChatChannelConfigDto>(entities.Count);
        foreach (var e in entities) list.Add(ToDto(e));
        return list;
    }

    /// <inheritdoc />
    public async Task<long> SaveChannelAsync(SaveChannelRequest request, CancellationToken ct = default)
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

        // 协议通道类型白名单校验（fail-fast——未知类型拒透传存储，防下游 ISsoChannel 选区永不命中；对齐 ChannelForm/Verification 值校验）
        var channelType = string.IsNullOrWhiteSpace(request.ChannelType) ? "wechat_event" : request.ChannelType.Trim().ToLowerInvariant();
        if (!LegalChannelTypes.Contains(channelType))
            throw new ArgumentException($"ChannelType 非法：{request.ChannelType}（合法值 wechat_oauth / wechat_event）", nameof(request));

        // 构造实体（wire DTO → 领域实体——凭证承载于 DTO 明文，服务端加密落库）
        var entity = new WeChatChannelConfigEntity
        {
            Id = request.Id ?? 0,
            ChannelId = request.ChannelId,
            AppId = request.AppId,
            DisplayName = request.DisplayName ?? "",
            Remark = request.Remark,
            IsEnabled = request.IsEnabled,
            ChannelType = channelType,        // 协议通道类型（whitelist 校验后归一存储——D03 白名单）
            ChannelForm = channelForm,          // D03 1.2 新列（形态——默认 mp_service 存量兼容）
            Verification = verification,        // D03 1.2 新列（认证状态——默认 none）
            Alias = string.IsNullOrWhiteSpace(request.Alias) ? null : request.Alias.Trim().ToLowerInvariant(),   // D03 1.2 新列（对外别名——1.6 守卫另立）
        };

        // D03 1.6 channel alias 规则——slug 格式校验（仅当请求提供了非空 Alias；新建 + 更新共用，fail-fast 于 DB 访问前）
        if (entity.Alias is not null && !AliasPattern.IsMatch(entity.Alias))
            throw new DomainException($"channel alias 格式非法：'{request.Alias}'（要求 3-64 位小写字母/数字/连字符，首字符须字母或数字）") { ErrorCode = "CHANNEL_ALIAS_INVALID" };

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
        var now = TruncateToSecond(DateTime.UtcNow);
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
        // CH-12 目标不存在归因（ErrorCode 化——对齐 alias 守卫错误码形态；预查与 CAS 之间的并发删除由下方 0 行重查兜底）
        if (db is null) throw new DomainException($"公众号配置不存在：Id={entity.Id}") { ErrorCode = "CHANNEL_NOT_FOUND" };

        // D03 1.6 alias 不可变 fail-fast（正常路径友好错误；并发窗口由下方 CAS WHERE 兜底——审核 P1-2）
        if (!string.IsNullOrWhiteSpace(db.Alias) && entity.Alias is not null && db.Alias != entity.Alias)
            throw new DomainException($"channel alias 不可修改（既有 '{db.Alias}'，请求 '{entity.Alias}'），如需改名请新建通道") { ErrorCode = "CHANNEL_ALIAS_IMMUTABLE" };

        var appSecretCipher = request.AppSecret is not null ? _keys.Encrypt(request.AppSecret) : db.AppSecretCipher;
        var tokenCipher = request.Token is not null ? _keys.Encrypt(request.Token) : db.TokenCipher;
        var aesKeyCipher = request.EncodingAesKey is not null ? _keys.Encrypt(request.EncodingAesKey) : db.EncodingAESKeyCipher;

        // CAS 条件更新（审核 P1-2——替代整行回写：① 请求无 alias 时不 SET Alias 列（并发写入的 alias 不会被抹掉）；
        // ② 请求有 alias 时 WHERE 要求库中 alias 为空或同值（并发设置 alias 的败者 0 行 → IMMUTABLE））
        int affected;
        if (entity.Alias is not null)
        {
            affected = await Ds.EntityUpdateWhereAsync(
                e => e.Id == entity.Id && (e.Alias == null || e.Alias == entity.Alias),
                e => new WeChatChannelConfigEntity
                {
                    ChannelId = entity.ChannelId,
                    AppId = entity.AppId,
                    DisplayName = entity.DisplayName,
                    Remark = entity.Remark,
                    IsEnabled = entity.IsEnabled,
                    ChannelType = entity.ChannelType,
                    ChannelForm = entity.ChannelForm,
                    Verification = entity.Verification,
                    Alias = entity.Alias,
                    AppSecretCipher = appSecretCipher,
                    TokenCipher = tokenCipher,
                    EncodingAESKeyCipher = aesKeyCipher,
                    UpdateTime = now,
                }, ct);
        }
        else
        {
            affected = await Ds.EntityUpdateWhereAsync(
                e => e.Id == entity.Id,
                e => new WeChatChannelConfigEntity
                {
                    ChannelId = entity.ChannelId,
                    AppId = entity.AppId,
                    DisplayName = entity.DisplayName,
                    Remark = entity.Remark,
                    IsEnabled = entity.IsEnabled,
                    ChannelType = entity.ChannelType,
                    ChannelForm = entity.ChannelForm,
                    Verification = entity.Verification,
                    AppSecretCipher = appSecretCipher,
                    TokenCipher = tokenCipher,
                    EncodingAESKeyCipher = aesKeyCipher,
                    UpdateTime = now,
                }, ct);
        }
        if (affected == 0)
            {
                // CH-12 归因区分：0 行 ≠ 恒 alias 冲突——预查与 CAS 之间目标行可能被并发删除（→ CHANNEL_NOT_FOUND）；
                // 目标仍存在才归因 alias CAS 失败（→ CHANNEL_ALIAS_IMMUTABLE）
                var stillExists = await Ds.EntityGetAsync(e => e.Id == entity.Id, ct);
                if (stillExists is null)
                    throw new DomainException($"公众号配置不存在：Id={entity.Id}") { ErrorCode = "CHANNEL_NOT_FOUND" };
                throw new DomainException("channel alias 并发冲突：alias 已被并发设置或修改，请刷新后重试") { ErrorCode = "CHANNEL_ALIAS_IMMUTABLE" };
            }

        await PublishChangedAsync(entity.ChannelId, now.Ticks);   // D03 三机制-事件失效（落库后发布）
        return entity.Id;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteChannelAsync(long id, CancellationToken ct = default)
    {
        var db = await Ds.EntityGetAsync(e => e.Id == id, ct);
        var affected = await Ds.EntityDeleteBatchAsync(new[] { id }, ct);
        if (affected > 0 && db is not null)
            await PublishChangedAsync(db.ChannelId, db.UpdateTime.Ticks);   // D03 三机制-事件失效（删除后发布）
        return affected > 0;
    }

    /// <summary>
    /// 落库后发布 <see cref="ChannelConfigChanged"/> 域事件（D03 §4.2.3 事件失效——<c>ChannelConfigChangedHandler</c>
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
            // 事件失效为旁路（TTL 兜底仍在）——发布失败不阻断写路径（降级矩阵惯例）。
            // 门面（AddConstructibleService 守卫工厂）禁止构造注入新依赖——logger 为可选注入
            // （对齐 WeChatOperationsStatusService 先例）：守卫构造路径 logger=null 降级 Debug 留痕，
            // 宿主 DI 解析路径经 ILogger 进宿主日志链路。
            _logger?.LogWarning(ex, "[ChannelConfigService] ChannelConfigChanged 发布失败：{Message}", ex.Message);
            System.Diagnostics.Debug.WriteLine($"[ChannelConfigService] ChannelConfigChanged 发布失败：{ex.Message}");
        }
    }

    private string EncryptOrEmpty(string? plain)
        => string.IsNullOrWhiteSpace(plain) ? string.Empty : _keys.Encrypt(plain);

    /// <summary>DTO 投影——不含凭证密文（Oracle P1 安全：密文不回显；契约 DTO 在 Abstractions）。</summary>
    private static WeChatChannelConfigDto ToDto(WeChatChannelConfigEntity e)
        => new(
            Id: e.Id,
            ChannelId: e.ChannelId,
            AppId: e.AppId,
            DisplayName: e.DisplayName,
            Remark: e.Remark,
            IsEnabled: e.IsEnabled,
            ChannelType: e.ChannelType,
            Alias: e.Alias,
            ChannelForm: e.ChannelForm,
            Verification: e.Verification,
            HasCredentials: !string.IsNullOrWhiteSpace(e.AppSecretCipher) || !string.IsNullOrWhiteSpace(e.TokenCipher) || !string.IsNullOrWhiteSpace(e.EncodingAESKeyCipher),
            CreateTime: e.CreateTime,
            UpdateTime: e.UpdateTime);
}