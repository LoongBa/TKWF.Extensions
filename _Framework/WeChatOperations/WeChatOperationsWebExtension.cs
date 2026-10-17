using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Hosting;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Web.Extensions;

namespace TKWF.Ext.WeChatOperations;

/// <summary>
/// 微信公众号运营 Web 装配扩展（v4.10.45 Web 装配钩子 ADR87/D22/G18——自描述 Web 装配需求，消费方
/// <c>UseWebExtensions</c> 一次声明）——运营管理端点（minimal API 手写——非 REST CRUD，组合门面业务编排，
/// 对齐 AuthCenterWebExtension 内建端点范式）。
/// <para>配置分层（AGENTS §8）：领域决策在 <see cref="WeChatOperationsOptions"/>（TKWF:WeChatOperations）；
/// 暴露面（RoutePrefix + 端点开关）在 <see cref="WeChatOperationsEndpointOptions"/>（TKWF:WeChatOperations:Web——
/// POCO 不镜像领域配置）。</para>
/// <para>⚠️ 认证：运营管理端点<b>非匿名</b>（不标 AllowAnonymous）——经框架 HttpAuthentication
/// （v4.10.76 匿名端点排除机制：非 AllowAnonymous 未认证 → 401 fail-closed）；已认证请求经
/// <c>ctx.Items["DomainUser"]</c> 已认证帧 <c>user.Use&lt;门面&gt;()</c> 调用守卫工厂门面。</para>
/// <para>⚠️ 装配约束：须与 <c>UseWebSession</c> 同装配（ContextExtraction 写 DomainUser）——对齐
/// AuthCenterWebExtension 先例（未装配 → 抛守卫 = 正确 fail 非静默降级）。</para>
/// </summary>
/// <typeparam name="TUserInfo">消费方用户类型。</typeparam>
public sealed class WeChatOperationsWebExtension<TUserInfo> : IWebExtension
    where TUserInfo : class, IUserInfo, new()
{
    /// <summary>端点 Options 编程式配置（可选）——后于配置节 <c>TKWF:WeChatOperations:Web</c> 应用。</summary>
    public Action<WeChatOperationsEndpointOptions>? ConfigureOptions { get; set; }

    /// <summary>宿主注册（Build 前立即执行；configure 委托先于本方法——实例属性已就绪，零延迟闭包）。</summary>
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddOptions<WeChatOperationsEndpointOptions>();
        if (ConfigureOptions is not null)
            services.Configure(ConfigureOptions);
    }

    /// <summary>中间件挂载（默认锚点 AfterRouting——纯端点扩展无中间件）。</summary>
    public void ConfigureMiddleware(IApplicationBuilder app, DomainWebOptions options)
    {
    }

    /// <summary>端点映射（AfterRouting 排空）——运营管理端点全集（设计文档 §3.3.5；RoutePrefix 默认 /api/wechat-ops）。</summary>
    public void ConfigureEndpoints(IEndpointRouteBuilder endpoints, DomainWebOptions options)
    {
        var endpointOptions = ResolveOptions(endpoints.ServiceProvider);
        var prefix = (endpointOptions.RoutePrefix ?? "/api/wechat-ops").TrimEnd('/');

        // 公众号渠道配置
        if (endpointOptions.ChannelsEndpointEnabled)
        {
            endpoints.MapGet($"{prefix}/channels", HandleListChannels);
            endpoints.MapPost($"{prefix}/channels", HandleSaveChannel);
        }

        // 菜单（拉取/发布/版本/回滚——组合门面）
        if (endpointOptions.MenuEndpointEnabled)
        {
            endpoints.MapGet($"{prefix}/channels/{{channelId}}/menu", HandleGetMenu);
            endpoints.MapPost($"{prefix}/channels/{{channelId}}/menu", HandlePublishMenu);
            endpoints.MapGet($"{prefix}/channels/{{channelId}}/menu/versions", HandleListMenuVersions);
            endpoints.MapPost($"{prefix}/channels/{{channelId}}/menu/rollback", HandleRollbackMenu);
        }

        // 回复规则
        if (endpointOptions.RulesEndpointEnabled)
        {
            endpoints.MapGet($"{prefix}/channels/{{channelId}}/rules", HandleListRules);
            endpoints.MapPost($"{prefix}/channels/{{channelId}}/rules", HandleSaveRule);
        }

        // 素材库
        if (endpointOptions.MaterialsEndpointEnabled)
        {
            endpoints.MapGet($"{prefix}/materials", HandleListMaterials);
            endpoints.MapPost($"{prefix}/materials", HandleSaveMaterial);
        }

        // 订阅统计
        if (endpointOptions.SubscriptionStatsEndpointEnabled)
        {
            endpoints.MapGet($"{prefix}/channels/{{channelId}}/subscription-stats", HandleListSubscriptionStats);
            endpoints.MapGet($"{prefix}/channels/{{channelId}}/subscription-stats/summary", HandleGetSubscriptionStatSummary);
        }

        // 系统状态（DMP-Lite 5.8 四张状态卡聚合——P2-1）
        if (endpointOptions.StatusEndpointEnabled)
            endpoints.MapGet($"{prefix}/channels/{{channelId}}/status", HandleGetStatusSummary);

        // 日志
        if (endpointOptions.LogsEndpointEnabled)
        {
            endpoints.MapGet($"{prefix}/channels/{{channelId}}/logs/interactions", HandleListInteractions);
            endpoints.MapGet($"{prefix}/channels/{{channelId}}/logs/publishes", HandleListPublishLogs);
        }
    }

    // ─────────────────────────── 端点处理器（已认证帧——运营管理端点非匿名） ───────────────────────────

    private static async Task<IResult> HandleListChannels(HttpContext ctx, CancellationToken ct)
        => Results.Ok(await GetUser(ctx).Use<IChannelConfigService>().ListAsync(ct));

    private static async Task<IResult> HandleSaveChannel(HttpContext ctx, [Microsoft.AspNetCore.Mvc.FromBody] SaveChannelRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.ChannelId) || string.IsNullOrWhiteSpace(req.AppId))
            return Results.BadRequest(new ErrorResponse("INVALID_ARGUMENT", "channelId/appId 必填"));
        try
        {
            var entity = new WeChatChannelConfigEntity
            {
                Id = req.Id ?? 0,
                ChannelId = req.ChannelId,
                AppId = req.AppId,
                DisplayName = req.DisplayName ?? "",
                Remark = req.Remark,
                IsEnabled = req.IsEnabled,
                ChannelType = req.ChannelType ?? "wechat_event",
            };
            var id = await GetUser(ctx).Use<IChannelConfigService>()
                .SaveAsync(entity, req.AppSecret, req.Token, req.EncodingAesKey, ct);
            return Results.Ok(new SaveChannelResponse(id));
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new ErrorResponse("CHANNEL_CONFLICT", ex.Message));
        }
    }

    private static async Task<IResult> HandleGetMenu(HttpContext ctx, string channelId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(channelId)) return Results.BadRequest(new ErrorResponse("INVALID_ARGUMENT", "channelId 必填"));
        var result = await GetUser(ctx).Use<IMenuService>().GetCurrentAsync(channelId, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> HandlePublishMenu(HttpContext ctx, string channelId, [Microsoft.AspNetCore.Mvc.FromBody] PublishMenuRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(channelId) || req is null || string.IsNullOrWhiteSpace(req.MenuJson))
            return Results.BadRequest(new ErrorResponse("INVALID_ARGUMENT", "channelId/menuJson 必填"));
        try
        {
            var result = await GetUser(ctx).Use<IMenuService>().PublishAsync(channelId, req.MenuJson, ct);
            return Results.Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new ErrorResponse("MENU_PUBLISH_FAILED", ex.Message));
        }
    }

    private static async Task<IResult> HandleListMenuVersions(HttpContext ctx, string channelId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(channelId)) return Results.BadRequest(new ErrorResponse("INVALID_ARGUMENT", "channelId 必填"));
        return Results.Ok(await GetUser(ctx).Use<IMenuService>().ListVersionsAsync(channelId, ct));
    }

    private static async Task<IResult> HandleRollbackMenu(HttpContext ctx, string channelId, [Microsoft.AspNetCore.Mvc.FromBody] RollbackMenuRequest req, CancellationToken ct)
    {
        if (req is null || req.VersionId <= 0) return Results.BadRequest(new ErrorResponse("INVALID_ARGUMENT", "versionId 必填"));
        try
        {
            var result = await GetUser(ctx).Use<IMenuService>().RollbackAsync(req.VersionId, ct);
            return Results.Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new ErrorResponse("MENU_ROLLBACK_FAILED", ex.Message));
        }
    }

    private static async Task<IResult> HandleListRules(HttpContext ctx, string channelId, CancellationToken ct)
    {
        var sceneRaw = ctx.Request.Query["scene_type"].ToString();
        int? sceneType = int.TryParse(sceneRaw, out var s) ? s : null;
        return Results.Ok(await GetUser(ctx).Use<IReplyRuleService>().ListRulesAsync(channelId, sceneType, ct));
    }

    private static async Task<IResult> HandleSaveRule(HttpContext ctx, string channelId, [Microsoft.AspNetCore.Mvc.FromBody] ReplyRuleEntity rule, CancellationToken ct)
    {
        if (rule is null || string.IsNullOrWhiteSpace(channelId)) return Results.BadRequest(new ErrorResponse("INVALID_ARGUMENT", "channelId/rule 必填"));
        try
        {
            rule.ChannelId = channelId;
            var id = await GetUser(ctx).Use<IReplyRuleService>().SaveRuleAsync(rule, ct);
            return Results.Ok(new SaveChannelResponse(id));
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new ErrorResponse("RULE_INVALID", ex.Message));
        }
    }

    private static async Task<IResult> HandleListMaterials(HttpContext ctx, CancellationToken ct)
    {
        var kindRaw = ctx.Request.Query["kind"].ToString();
        int? kind = int.TryParse(kindRaw, out var k) ? k : null;
        return Results.Ok(await GetUser(ctx).Use<IMaterialService>().ListAsync(kind, ct));
    }

    private static async Task<IResult> HandleSaveMaterial(HttpContext ctx, [Microsoft.AspNetCore.Mvc.FromBody] MaterialEntity material, CancellationToken ct)
    {
        if (material is null) return Results.BadRequest(new ErrorResponse("INVALID_ARGUMENT", "material 必填"));
        try
        {
            var id = await GetUser(ctx).Use<IMaterialService>().SaveAsync(material, ct);
            return Results.Ok(new SaveChannelResponse(id));
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new ErrorResponse("MATERIAL_INVALID", ex.Message));
        }
    }

    private static async Task<IResult> HandleListSubscriptionStats(HttpContext ctx, string channelId, CancellationToken ct)
    {
        var fromRaw = ctx.Request.Query["from"].ToString();
        var toRaw = ctx.Request.Query["to"].ToString();
        var sourceRaw = ctx.Request.Query["source"].ToString();   // V0.3.0 来源维度——可空 = 仅汇总行
        var from = DateTime.TryParse(fromRaw, out var f) ? f : DateTime.UtcNow.Date.AddDays(-30);
        var to = DateTime.TryParse(toRaw, out var t) ? t : DateTime.UtcNow.Date;
        return Results.Ok(await GetUser(ctx).Use<ISubscriptionStatService>()
            .GetTrendAsync(channelId, from, to, string.IsNullOrWhiteSpace(sourceRaw) ? null : sourceRaw, ct));
    }

    /// <summary>订阅统计区间汇总（DMP-Lite 5.7 §2 统计卡——区间新增/取消/日均/净增/累计增长率；from/to 可空回落近 30 天）。</summary>
    private static async Task<IResult> HandleGetSubscriptionStatSummary(HttpContext ctx, string channelId, CancellationToken ct)
    {
        var fromRaw = ctx.Request.Query["from"].ToString();
        var toRaw = ctx.Request.Query["to"].ToString();
        var sourceRaw = ctx.Request.Query["source"].ToString();   // 可空 = 仅汇总行
        var from = DateTime.TryParse(fromRaw, out var f) ? f : DateTime.UtcNow.Date.AddDays(-30);
        var to = DateTime.TryParse(toRaw, out var t) ? t : DateTime.UtcNow.Date;
        return Results.Ok(await GetUser(ctx).Use<ISubscriptionStatService>()
            .GetSummaryAsync(channelId, from, to, string.IsNullOrWhiteSpace(sourceRaw) ? null : sourceRaw, ct));
    }

    /// <summary>系统状态四卡聚合（DMP-Lite 5.8——公众号连接状态/今日消息量/自动回复命中率/转发成功率；只读无凭证）。</summary>
    private static async Task<IResult> HandleGetStatusSummary(HttpContext ctx, string channelId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(channelId)) return Results.BadRequest(new ErrorResponse("INVALID_ARGUMENT", "channelId 必填"));
        return Results.Ok(await GetUser(ctx).Use<IWeChatOperationsStatusService>().GetStatusSummaryAsync(channelId, ct));
    }

    private static async Task<IResult> HandleListInteractions(HttpContext ctx, string channelId, CancellationToken ct)
    {
        var openId = ctx.Request.Query["open_id"].ToString();
        var triggerRaw = ctx.Request.Query["trigger_type"].ToString();
        int? triggerType = int.TryParse(triggerRaw, out var tr) ? tr : null;
        DateTime? from = DateTime.TryParse(ctx.Request.Query["from"].ToString(), out var f) ? f : null;
        DateTime? to = DateTime.TryParse(ctx.Request.Query["to"].ToString(), out var t) ? t : null;
        return Results.Ok(await GetUser(ctx).Use<ILogQueryService>()
            .ListInteractionsAsync(channelId, string.IsNullOrWhiteSpace(openId) ? null : openId, from, to, triggerType, ct));
    }

    private static async Task<IResult> HandleListPublishLogs(HttpContext ctx, string channelId, CancellationToken ct)
        => Results.Ok(await GetUser(ctx).Use<ILogQueryService>().ListPublishLogsAsync(channelId, ct));

    // ─────────────────────────── 辅助 ───────────────────────────

    /// <summary>已认证帧（运营管理端点非匿名——HttpAuthentication 已认证后写入 DomainUser；未认证 401 fail-closed）。</summary>
    private static DomainUser<TUserInfo> GetUser(HttpContext ctx)
    {
        if (ctx.Items["DomainUser"] is not DomainUser<TUserInfo> user || !user.IsAuthenticated)
            throw new InvalidOperationException(
                "WeChatOperations 运营端点需要已认证帧——未认证请求应由框架 HttpAuthentication 拦截（401）；请确认 UseWebSession 装配 + Bearer 认证配置。");
        return user;
    }

    /// <summary>端点 Options 解析（HealthCheck/AuthCenter 先例——IOptions → IConfiguration 直读 → 默认值 三段兜底）。</summary>
    private static WeChatOperationsEndpointOptions ResolveOptions(IServiceProvider services)
    {
        var options = services.GetService<IOptions<WeChatOperationsEndpointOptions>>()?.Value;
        if (options is not null) return options;
        var fallback = new WeChatOperationsEndpointOptions();
        services.GetService<IConfiguration>()?.GetSection("TKWF:WeChatOperations:Web").Bind(fallback);
        return fallback;
    }
}

// ─────────────────────────── 端点 Options + Web DTO ───────────────────────────

/// <summary>
/// 运营端点暴露面 Options（<c>TKWF:WeChatOperations:Web</c> 节——配置分层 AGENTS §8：仅暴露面
/// （RoutePrefix + 端点开关），POCO 不镜像领域配置；领域安全行为（凭证加密/规则匹配）Web 层不可关闭）。
/// </summary>
public class WeChatOperationsEndpointOptions
{
    /// <summary>端点路由前缀（默认 /api/wechat-ops）。</summary>
    public string RoutePrefix { get; set; } = "/api/wechat-ops";

    /// <summary>公众号渠道配置端点开关（GET/POST /channels）。</summary>
    public bool ChannelsEndpointEnabled { get; set; } = true;

    /// <summary>菜单端点开关（GET/POST .../menu + versions/rollback）。</summary>
    public bool MenuEndpointEnabled { get; set; } = true;

    /// <summary>回复规则端点开关（GET/POST .../rules）。</summary>
    public bool RulesEndpointEnabled { get; set; } = true;

    /// <summary>素材端点开关（GET/POST /materials）。</summary>
    public bool MaterialsEndpointEnabled { get; set; } = true;

    /// <summary>订阅统计端点开关（GET .../subscription-stats + .../subscription-stats/summary）。</summary>
    public bool SubscriptionStatsEndpointEnabled { get; set; } = true;

    /// <summary>系统状态端点开关（GET .../status——DMP-Lite 5.8 四张状态卡：连接状态/今日消息量/命中率/转发成功率）。</summary>
    public bool StatusEndpointEnabled { get; set; } = true;

    /// <summary>日志端点开关（GET .../logs/interactions + /logs/publishes）。</summary>
    public bool LogsEndpointEnabled { get; set; } = true;
}

/// <summary>保存公众号配置请求（凭证字段可选——null = 保持既有；<b>不落服务端日志</b>——Oracle P1）。</summary>
public sealed record SaveChannelRequest(
    long? Id,
    string ChannelId,
    string AppId,
    string? DisplayName,
    string? Remark,
    bool IsEnabled = true,
    string? ChannelType = null,
    string? AppSecret = null,
    string? Token = null,
    string? EncodingAesKey = null);

/// <summary>保存结果响应（Id——新建返回自增主键）。</summary>
public sealed record SaveChannelResponse(long Id);

/// <summary>发布菜单请求（raw JSON 透传——编辑器自由形态）。</summary>
public sealed record PublishMenuRequest(string MenuJson);

/// <summary>回滚菜单请求（目标版本 Id——加载该版本 AfterJson 重新发布）。</summary>
public sealed record RollbackMenuRequest(long VersionId);

/// <summary>统一错误响应（错误码 + 可选消息）。</summary>
public sealed record ErrorResponse(string Code, string? Message);