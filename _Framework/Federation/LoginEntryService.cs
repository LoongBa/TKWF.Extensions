using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Ticket;

namespace TKWF.Ext.Federation;

/// <summary>
/// 免登入口门面实现（I2-8——方案 docs/Federation/I2-渠道关注关系域-开发方案.md §3.8；D03 1.3——签票载荷切换
/// <c>ISealedTicketService&lt;FederatedIdentity&gt;</c> → <c>ISealedTicketService&lt;FederatedTicketPayload&gt;</c>）。
/// <para>单一签票原语 + 单一 URL 组装点（R4）：签票经 <c>ISealedTicketService&lt;FederatedTicketPayload&gt;</c>
/// （主框架原语——与 <see cref="EventReplyHandlerBase"/> 同一个签票原语，不引入第二套票据实现）；
/// URL 组装经内部 static helper（<see cref="CombineUrl"/>——<see cref="EventReplyHandlerBase"/> 测试端点
/// URL 拼装委托复用，逐字节等价测试锁定）。</para>
/// <para>载荷组装（D03 §6.1 A7/P2）：经 <see cref="FederatedTicketPayloadBuilder"/> 单点——provider=wechat、
/// 通道形态（ChannelForm）经 <see cref="IChannelRegistry"/> 反查通道配置（Extra["channel_form"]）双写；
/// 协议通道类型（入参 channelType）进 <c>Extensions["protocol_type"]</c>（兑付侧适配转换回取）。
/// ⚠️ 命名碰撞红线（Oracle6 条件 4）：payload.ChannelType = 通道形态（mp_service...）非协议类型（wechat_event）。
/// 回退：通道未配 ChannelForm（存量）→ 协议类型映射表推断（<c>wechat_event→mp_subscription</c>）。</para>
/// <para>fail-closed 两层（方案 §3.8 C8）：① 未注册 = 装配错误 → <c>User.Use&lt;&gt;()</c> 守卫抛
/// （不捕获——Federation 未启用时整个链本不存在）；② 能力未开/基址缺失/签票失败 → null + Warning（不阻断回复）。</para>
/// <para>注册：Federation 初始器 <c>AddConstructibleService&lt;ILoginEntryService, LoginEntryService&gt;()</c>
/// （守卫工厂——消费方 <c>User.Use&lt;ILoginEntryService&gt;()</c> 帧内解析，DI004）。</para>
/// </summary>
[DiContractIgnore]
internal sealed class LoginEntryService : DomainServiceBase, ILoginEntryService
{
    private readonly IOptions<FederationLoginEntryOptions> _loginEntry;
    private readonly IOptions<FederationEventReplyOptions> _eventReply;
    private readonly IOptions<FederationChannelRegistryOptions> _channelRegistry;
    private readonly ILogger<LoginEntryService> _logger;

    private ISealedTicketService<FederatedTicketPayload>? _ticket;
    private ISsoChannelFactory? _factory;
    private IChannelRegistry? _registry;

    public LoginEntryService(
        IDomainUser user,
        IServiceProvider sp,
        IOptions<FederationLoginEntryOptions> loginEntry,
        IOptions<FederationEventReplyOptions> eventReply,
        IOptions<FederationChannelRegistryOptions> channelRegistry,
        ILogger<LoginEntryService> logger)
        : base(user)
    {
        _ = sp;
        _loginEntry = loginEntry ?? throw new ArgumentNullException(nameof(loginEntry));
        _eventReply = eventReply ?? throw new ArgumentNullException(nameof(eventReply));
        _channelRegistry = channelRegistry;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string?> BuildTicketUrlAsync(
        string channelId, string channelType, string externalUserId,
        int? ttlSeconds = null, string? requestBaseUrl = null, CancellationToken ct = default, string? uId = null)
    {
        // ① 能力开关（fail-closed 第一层——默认 false 显式启用）
        var options = _loginEntry.Value;
        if (!options.Enabled)
        {
            _logger.LogWarning("免登入口未启用（FederationLoginEntryOptions.Enabled=false）——不生成免登链接：ChannelId={ChannelId}", channelId);
            return null;
        }

        // ② 基址解析（三级：ChannelBaseUrls 按 ChannelId 覆盖 → TestTicketEndpointBaseUrl → 请求 Host 推导；
        //    全空 → CombineUrl 相对路径兜底——本站点同源，微信图文 link 恒有 requestBaseUrl 实际可用）
        if (!options.ChannelBaseUrls.TryGetValue(channelId, out var baseUrl))
            baseUrl = _eventReply.Value.TestTicketEndpointBaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = requestBaseUrl;

        // ③ 签密封票据（null-claimant——免登终端兑票对应 AuthCenter V0.10.0 POST /login/ticket 仅消费 null-claimant 票；
        //    uId 可空 = 未绑定 → 签空串（编排映射反查/自动建号兜底）；已绑定用户 uId 入载荷——落地端
        //    FederatedLoginOrchestrator UId 直读复用既有账号，免二次映射）
        //    D03 1.3：载荷 = FederatedTicketPayload（channelType 入参为协议类型——经 Builder 形态回退/Registry 反查双写）
        var payload = FederatedTicketPayloadBuilder.Build(
            provider: "wechat",
            protocolChannelType: channelType,
            channelId: channelId,
            externalUserId: externalUserId,
            platformUid: uId,
            assurance: "event",
            ttl: TimeSpan.FromSeconds(ttlSeconds ?? options.TicketTtlSeconds),
            resolver: ResolveChannelForm);
        try
        {
            var issued = await TicketService().IssueAsync(
                payload,
                TimeSpan.FromSeconds(ttlSeconds ?? options.TicketTtlSeconds),
                new SealedTicketOptions(),
                ct);

            // ④ URL 组装（{baseUrl}{RoutePrefix}{TicketPath}?ticket={ticket}——RoutePrefix 复用 eventReply，单一事实源）
            var url = CombineUrl(baseUrl, _eventReply.Value.RoutePrefix, $"{options.TicketPath}?ticket={issued.Ticket}");
            // 衔接测试机制（v0.16.0）：非生产模式输出完整 URL 到日志——扩展模块/消费方测试可复制日志
            // URL 直接访问（微信图文卡片链接无法从客户端复制）；生产（IsProduction=true）零输出——
            // §6.4 红线"ticket 全串不落日志"生产恒定（仅 dev 出口，测试方受控访问）
            if (_channelRegistry?.Value.IsProduction != true)
                _logger.LogInformation("免登链接已生成（开发模式）——测试方可直接访问：{Url}", url);
            return url;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 签票失败（票据后端不可用）→ null 不阻断回复（fail-closed 第二层）；ticket 全串不落日志（只记 channelId + externalUserId 前 6 位，§6.4）
            _logger.LogWarning(ex, "免登链接签票失败（票据后端不可用）——不生成免登链接：ChannelId={ChannelId} ExternalUserId={ExternalUserIdPrefix}",
                channelId, externalUserId.Length <= 6 ? externalUserId : externalUserId[..6]);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<string?> BuildTestTicketUrlAsync(
        string channelId, string platformId, string channelType, string externalUserId,
        int? ttlSeconds = null, string? requestBaseUrl = null, CancellationToken ct = default, string? uId = null)
    {
        // ① 基址解析（三级：ChannelBaseUrls 按 ChannelId 覆盖 → TestTicketEndpointBaseUrl → 请求 Host 推导；
        //    全空 → CombineUrl 相对路径兜底——本站点同源；微信图文 link 恒有 requestBaseUrl 实际可用）
        //    ⚠️ 不经 Enabled 门控——测试端点匿名兑票（HandleTestTicket 直兑 ISetaledTicketService），
        //    签票能力与免登入口独立（零配置冒烟：密钥/DB 就绪即可产测试链接）
        var options = _loginEntry.Value;
        if (!options.ChannelBaseUrls.TryGetValue(channelId, out var baseUrl))
            baseUrl = _eventReply.Value.TestTicketEndpointBaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = requestBaseUrl;

        // ② 签密封票据（null-claimant——与免登票同原语；测试端点 Redeem 后展示载荷；
        //    uId 可空 = 未绑定 → 签空串（零配置冒烟不依赖绑定；已绑定用户 uId 入载荷，展示页体现）
        //    D03 1.3：载荷 = FederatedTicketPayload（同 BuildTicketUrlAsync——协议类型入参经 Builder 双写）
        var payload = FederatedTicketPayloadBuilder.Build(
            provider: "wechat",
            protocolChannelType: channelType,
            channelId: channelId,
            externalUserId: externalUserId,
            platformUid: uId,
            assurance: "event",
            ttl: TimeSpan.FromSeconds(ttlSeconds ?? options.TicketTtlSeconds),
            resolver: ResolveChannelForm);
        try
        {
            var issued = await TicketService().IssueAsync(
                payload,
                TimeSpan.FromSeconds(ttlSeconds ?? options.TicketTtlSeconds),
                new SealedTicketOptions(),
                ct);

            // ③ URL 组装（测试端点路径 {baseUrl}{RoutePrefix}/{platformId}/test/{channelId}/{ticket}——
            //    与免登 URL 对照：免登走 {TicketPath}?ticket=，测试端点走 /test/{channelId}/{ticket} 路径段）
            var url = CombineUrl(baseUrl, _eventReply.Value.RoutePrefix, $"{platformId}/test/{channelId}/{issued.Ticket}");
            // 衔接测试机制（v0.16.0）：非生产模式输出完整 URL——与免登同款 dev 出口（生产 IsProduction=true 零输出）
            if (_channelRegistry?.Value.IsProduction != true)
                _logger.LogInformation("测试端点链接已生成（开发模式）——测试方可直接访问：{Url}", url);
            return url;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "测试端点签票失败（票据后端不可用）——不生成测试链接：ChannelId={ChannelId} ExternalUserId={ExternalUserIdPrefix}",
                channelId, externalUserId.Length <= 6 ? externalUserId : externalUserId[..6]);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<string?> BuildSilentAuthorizeUrlAsync(string channelId, string returnUrl, CancellationToken ct = default)
    {
        // 通道构造失败（未装配平台库 → CreateAsync null）→ null 非抛（fail-closed 降级）
        var channel = await Factory().CreateAsync(channelId, null, ct);
        if (channel is null)
            return null;

        // 委托通道 BuildAuthorizeUrlAsync（Scope=snsapi_base 静默授权——通道不支持 → DIM null → null）
        var result = await channel.BuildAuthorizeUrlAsync(
            new SsoChannelAuthorizeContext(returnUrl, null, "snsapi_base", new Dictionary<string, string?>()), ct);
        return result?.Url;
    }

    /// <summary>
    /// 内部 URL 组装 helper（单一组装点——基址 + RoutePrefix 合并 + 路径拼装；<see cref="EventReplyHandlerBase"/>
    /// 测试端点 URL 拼装委托复用——逐字节等价，测试锁定）。<c>baseUrl</c> 空 → 相对路径（以 / 开头指向本服务同源）。
    /// </summary>
    internal static string CombineUrl(string? baseUrl, string routePrefix, string path)
    {
        var relative = $"{routePrefix.Trim('/')}/{path.TrimStart('/')}";
        var clean = baseUrl?.TrimEnd('/');
        return string.IsNullOrEmpty(clean) ? $"/{relative}" : $"{clean}/{relative}";
    }

    private ISealedTicketService<FederatedTicketPayload> TicketService()
        => _ticket ??= User.Use<ISealedTicketService<FederatedTicketPayload>>();

    private ISsoChannelFactory Factory()
        => _factory ??= User.Use<ISsoChannelFactory>();

    /// <summary>
    /// 通道形态解析（D03 1.3——payload.ChannelType 取通道形态）：经 <see cref="IChannelRegistry"/> 反查
    /// <see cref="ChannelConfig"/>——<c>Extra["channel_form"]</c>（DB 源 1.2 投影 ChannelForm）+ <c>Alias</c>；
    /// Registry 不可达/通道未配置 → null（Builder 回退协议类型映射）。<b>⚠️ 命名碰撞红线（Oracle6 条件 4）</b>：
    /// 取 ChannelForm 而非 ChannelType（协议类型 wechat_event 不入票）。
    /// </summary>
    private (string? ChannelForm, string? ChannelAlias, string Provider) ResolveChannelForm(string channelId)
    {
        try
        {
            var cfg = Registry().GetByAliasOrIdAsync(channelId).GetAwaiter().GetResult();
            if (cfg is null)
                return (null, null, "wechat");
            var form = cfg.Extra?.TryGetValue("channel_form", out var f) == true && !string.IsNullOrEmpty(f)
                ? f
                : null;
            return (form, cfg.Alias, string.IsNullOrEmpty(cfg.PlatformType) ? "wechat" : cfg.PlatformType);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "免登签票通道形态解析不可达（Registry 降级——Builder 协议类型映射回退）：ChannelId={ChannelId}", channelId);
            return (null, null, "wechat");
        }
    }

    private IChannelRegistry Registry()
        => _registry ??= User.Use<IChannelRegistry>();
}