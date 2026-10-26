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
/// 免登入口门面实现（I2-8——方案 docs/Federation/I2-渠道关注关系域-开发方案.md §3.8）。
/// <para>单一签票原语 + 单一 URL 组装点（R4）：签票经 <c>ISealedTicketService&lt;FederatedIdentity&gt;</c>
/// （主框架原语——与 <see cref="EventReplyHandlerBase"/> 同一个签票原语，不引入第二套票据实现）；
/// URL 组装经内部 static helper（<see cref="CombineUrl"/>——<see cref="EventReplyHandlerBase"/> 测试端点
/// URL 拼装委托复用，逐字节等价测试锁定）。</para>
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
    private readonly ILogger<LoginEntryService> _logger;

    private ISealedTicketService<FederatedIdentity>? _ticket;
    private ISsoChannelFactory? _factory;

    public LoginEntryService(
        IDomainUser user,
        IServiceProvider sp,
        IOptions<FederationLoginEntryOptions> loginEntry,
        IOptions<FederationEventReplyOptions> eventReply,
        ILogger<LoginEntryService> logger)
        : base(user)
    {
        _ = sp;
        _loginEntry = loginEntry ?? throw new ArgumentNullException(nameof(loginEntry));
        _eventReply = eventReply ?? throw new ArgumentNullException(nameof(eventReply));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string?> BuildTicketUrlAsync(
        string channelId, string channelType, string externalUserId,
        int? ttlSeconds = null, string? requestBaseUrl = null, CancellationToken ct = default)
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

        // ③ 签密封票据（null-claimant——免登终端兑票对应 AuthCenter V0.10.0 POST /login/ticket 仅消费 null-claimant 票）
        var identity = new FederatedIdentity(
            UId: string.Empty,
            ChannelId: channelId,
            ExternalUserId: externalUserId,
            ChannelType: channelType);
        try
        {
            var issued = await TicketService().IssueAsync(
                identity,
                TimeSpan.FromSeconds(ttlSeconds ?? options.TicketTtlSeconds),
                new SealedTicketOptions(),
                ct);

            // ④ URL 组装（{baseUrl}{RoutePrefix}{TicketPath}?ticket={ticket}——RoutePrefix 复用 eventReply，单一事实源）
            return CombineUrl(baseUrl, _eventReply.Value.RoutePrefix, $"{options.TicketPath}?ticket={issued.Ticket}");
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
        var identity = new FederatedIdentity(
            UId: uId ?? string.Empty,
            ChannelId: channelId,
            ExternalUserId: externalUserId,
            ChannelType: channelType);
        try
        {
            var issued = await TicketService().IssueAsync(
                identity,
                TimeSpan.FromSeconds(ttlSeconds ?? options.TicketTtlSeconds),
                new SealedTicketOptions(),
                ct);

            // ③ URL 组装（测试端点路径 {baseUrl}{RoutePrefix}/{platformId}/test/{channelId}/{ticket}——
            //    与免登 URL 对照：免登走 {TicketPath}?ticket=，测试端点走 /test/{channelId}/{ticket} 路径段）
            return CombineUrl(baseUrl, _eventReply.Value.RoutePrefix, $"{platformId}/test/{channelId}/{issued.Ticket}");
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

    private ISealedTicketService<FederatedIdentity> TicketService()
        => _ticket ??= User.Use<ISealedTicketService<FederatedIdentity>>();

    private ISsoChannelFactory Factory()
        => _factory ??= User.Use<ISsoChannelFactory>();
}