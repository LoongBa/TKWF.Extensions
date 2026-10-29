using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Hosting;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Web.Extensions;
using TKW.Framework.Domain.Web.Hosting;
using TKWF.Ext.Testing.Shared;
using TKWF.Ext.WeChatOperations.DTOs;
using TKWF.Federation.WeChat;

namespace TKWF.Ext.WeChatOperations.Tests;

/// <summary>
/// T22 gate（2026-10-27 补，转达 DMP v0.9.21——SG2 门面聚合缺口）：<see cref="WeChatOperationsWebExtension"/>
/// 端点级黑盒冒烟宿主——DomainHost 进程内单例（"DomainHost 不能重复初始化"，全测试项目仅可建 1 个 Web 宿主）。
/// <para>装配链镜像 <c>AuthCenterEndpointHost</c>（V0.6.0 T4/T5 冒烟先例）：ConfigWebAppDomain + ConsumerHostInitializer
/// （白名单 WeChatOperations）+ RegisterServices 替换 8 门面为普通 Scoped Fake（守卫工厂替换——guest.Use 帧内解析，
/// 聚焦端点挂载/路由映射/门面绑定，不测领域逻辑——领域逻辑由门面单测锁定）+ UseWebSession + WeChatOperationsWebExtension
/// （DeclareEndpoints MapGenerated 挂载 25 端点）+ UseAspNetCoreRouting。</para>
/// <para>⚠️ 认证语义：25 生成端点全挂 <c>RequireAuthorization()</c>（ASP.NET 授权层），测试宿主 <c>HttpAuth.ExcludedPathPrefixes</c>
/// 排除 /api（对齐 AuthCenter 先例——401 语义不下沉门面，端点到门面绑定断言直达）——HttpAuthentication fail-closed
/// 归框架中间件测试域（AuthCenter JwtAuth 顺序冒烟覆盖），本宿主聚焦<b>端点已挂载 + handler→门面绑定</b>（断链 → 404 即测试红）。</para>
/// </summary>
internal static class WeChatOperationsEndpointHost
{
    private const string RestBasePath = "/api/wechat-ops";

    public static TestServer Server => _lazy.Value;
    private static readonly Lazy<TestServer> _lazy = new(CreateServer);

    // ─── Fake 门面（端点测试断言 Invoked/结果——守卫工厂替换，普通 Scoped） ───
    public static FakeChannelConfigService ChannelConfig { get; } = new();
    public static FakeMenuService Menu { get; } = new();
    public static FakeReplyRuleService ReplyRule { get; } = new();
    public static FakeMaterialService Material { get; } = new();
    public static FakeSubscriptionStatService SubscriptionStat { get; } = new();
    public static FakeLogQueryService LogQuery { get; } = new();
    public static FakeMessageGatewayService Gateway { get; } = new();
    public static FakeStatusService Status { get; } = new();

    private static TestServer CreateServer()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Environment.EnvironmentName = "Development";   // ValidateScopes=true 回归哨兵
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TKWDomain:Name"] = "WeChatOperationsEndpointSmoke",
            // WeChatOperationsOptions 领域配置节（保底——Fake 不依赖，防 SG1 [Options] 绑定缺节）
            ["TKWF:WeChatOperations:IsProduction"] = "false",
        });

        var app = builder.ConfigWebAppDomain<TestUserInfo, ConsumerHostInitializer, DomainWebOptions>(configure: cfg =>
        {
            cfg.UseNoEntityDAC();                               // 冒烟无数据存取（门面 Fake）
            cfg.HttpAuth.ExcludedPathPrefixes.Add("/api");      // 端点挂载断言直达（见类注释认证语义）
        })
        .RegisterServices((svc, _) =>
        {
            // 8 门面守卫工厂替换 → 普通 Scoped Fake（对齐 AuthCenter B3a/B3b 冒烟替换语义——
            //   AddConstructibleService 守卫工厂被 Replace 覆盖，guest.Use 帧内解析 Fake）
            svc.Replace(ServiceDescriptor.Scoped<IChannelConfigService>(_ => ChannelConfig));
            svc.Replace(ServiceDescriptor.Scoped<IMenuService>(_ => Menu));
            svc.Replace(ServiceDescriptor.Scoped<IReplyRuleService>(_ => ReplyRule));
            svc.Replace(ServiceDescriptor.Scoped<IMaterialService>(_ => Material));
            svc.Replace(ServiceDescriptor.Scoped<ISubscriptionStatService>(_ => SubscriptionStat));
            svc.Replace(ServiceDescriptor.Scoped<ILogQueryService>(_ => LogQuery));
            svc.Replace(ServiceDescriptor.Scoped<IMessageGatewayService>(_ => Gateway));
            svc.Replace(ServiceDescriptor.Scoped<IWeChatOperationsStatusService>(_ => Status));
        })
        .UseWebSession()
        .UseWebExtensions<TestUserInfo>(e =>
        {
            e.Add<WeChatOperationsWebExtension>();
        })
        .BeforeRouting((_, _) => { })
        .UseAspNetCoreRouting()
        .AfterRouting((_, _) => { })
        .Build();

        app.StartAsync().GetAwaiter().GetResult();
        return app.GetTestServer();
    }

    /// <summary>组装端点 URL（RestBasePath + Route）——集中常量防拼写漂移。</summary>
    public static string Url(string route) => RestBasePath + route;

    // ═══════════════════ Fake 门面 ═══════════════════

    /// <summary>公众号配置门面 Fake——Invoked/LastRequest 捕获 + 固定 DTO 列表。</summary>
    internal sealed class FakeChannelConfigService : IChannelConfigService
    {
        public bool ListInvoked { get; private set; }
        public bool SaveInvoked { get; private set; }
        public SaveChannelRequest? LastRequest { get; private set; }

        public Task<WeChatChannelConfigDto?> GetAsync(string appId, CancellationToken ct = default)
            => Task.FromResult<WeChatChannelConfigDto?>(null);

        public Task<IReadOnlyList<WeChatChannelConfigDto>> ListAsync(CancellationToken ct = default)
        {
            ListInvoked = true;
            return Task.FromResult<IReadOnlyList<WeChatChannelConfigDto>>(
            [
                new WeChatChannelConfigDto(1, "wechat-mp-main", "wx_main", "主公众号", null, true, "wechat_event", "main", true,
                    new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)),
            ]);
        }

        public Task<long> SaveAsync(SaveChannelRequest request, CancellationToken ct = default)
        {
            SaveInvoked = true;
            LastRequest = request;
            return Task.FromResult(42L);
        }

        public Task<bool> DeleteAsync(long id, CancellationToken ct = default)
            => Task.FromResult(true);
    }

    /// <summary>菜单门面 Fake——Invoked 捕获 + 固定返回（版本历史空列表/操作 null——零实体构造）。</summary>
    internal sealed class FakeMenuService : IMenuService
    {
        public bool GetCurrentInvoked { get; private set; }
        public bool PublishInvoked { get; private set; }
        public bool ListVersionsInvoked { get; private set; }
        public bool RollbackInvoked { get; private set; }

        public Task<WeChatSelfMenuInfoResult?> GetCurrentAsync(string channelId, CancellationToken ct = default)
        {
            GetCurrentInvoked = true;
            return Task.FromResult<WeChatSelfMenuInfoResult?>(null);
        }

        public Task<WeChatSelfMenuInfoResult?> PublishAsync(string channelId, PublishMenuRequest request, CancellationToken ct = default)
        {
            PublishInvoked = true;
            return Task.FromResult<WeChatSelfMenuInfoResult?>(null);
        }

        public Task<string?> GetSnapshotAsync(string channelId, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task<IReadOnlyList<MenuVersionEntity>> ListVersionsAsync(string channelId, CancellationToken ct = default)
        {
            ListVersionsInvoked = true;
            return Task.FromResult<IReadOnlyList<MenuVersionEntity>>(Array.Empty<MenuVersionEntity>());
        }

        public Task<WeChatSelfMenuInfoResult?> RollbackAsync(string channelId, RollbackMenuRequest request, CancellationToken ct = default)
        {
            RollbackInvoked = true;
            return Task.FromResult<WeChatSelfMenuInfoResult?>(null);
        }

        public Task DeleteAsync(string channelId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<string>> GetClickKeysAsync(string channelId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    /// <summary>回复规则门面 Fake——Invoked 捕获 + 空规则列表。</summary>
    internal sealed class FakeReplyRuleService : IReplyRuleService
    {
        public bool ListInvoked { get; private set; }
        public bool SaveInvoked { get; private set; }

        public Task<IReadOnlyList<ReplyRuleEntity>> ListRulesAsync(string channelId, int? sceneType = null, CancellationToken ct = default)
        {
            ListInvoked = true;
            return Task.FromResult<IReadOnlyList<ReplyRuleEntity>>(Array.Empty<ReplyRuleEntity>());
        }

        public Task<long> SaveRuleAsync(ReplyRuleEntity rule, CancellationToken ct = default)
        {
            SaveInvoked = true;
            return Task.FromResult(7L);
        }

        public Task<bool> DeleteRuleAsync(long ruleId, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<ReplyRuleEntity?> GetWelcomeAsync(string channelId, CancellationToken ct = default)
            => Task.FromResult<ReplyRuleEntity?>(null);

        public Task<ReplyRuleEntity?> GetDefaultAsync(string channelId, CancellationToken ct = default)
            => Task.FromResult<ReplyRuleEntity?>(null);
    }

    /// <summary>素材门面 Fake——Invoked 捕获 + 空素材列表。</summary>
    internal sealed class FakeMaterialService : IMaterialService
    {
        public bool ListInvoked { get; private set; }
        public bool SaveInvoked { get; private set; }

        public Task<IReadOnlyList<MaterialEntity>> ListAsync(int? kind = null, CancellationToken ct = default)
        {
            ListInvoked = true;
            return Task.FromResult<IReadOnlyList<MaterialEntity>>(Array.Empty<MaterialEntity>());
        }

        public Task<long> SaveAsync(MaterialEntity material, CancellationToken ct = default)
        {
            SaveInvoked = true;
            return Task.FromResult(9L);
        }

        public Task<bool> DeleteAsync(long materialId, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<IReadOnlyList<MaterialReferenceDto>> GetReferencesAsync(long materialId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MaterialReferenceDto>>(Array.Empty<MaterialReferenceDto>());
    }

    /// <summary>订阅统计门面 Fake——Invoked 捕获 + 空趋势/零汇总。</summary>
    internal sealed class FakeSubscriptionStatService : ISubscriptionStatService
    {
        public bool TrendInvoked { get; private set; }
        public bool SummaryInvoked { get; private set; }

        public Task<IReadOnlyList<SubscriptionStatPointDto>> GetTrendAsync(string channelId, DateTime from, DateTime to, string? source = null, CancellationToken ct = default)
        {
            TrendInvoked = true;
            return Task.FromResult<IReadOnlyList<SubscriptionStatPointDto>>(Array.Empty<SubscriptionStatPointDto>());
        }

        public Task<SubscriptionStatSummaryDto> GetSummaryAsync(string channelId, DateTime from, DateTime to, string? source = null, CancellationToken ct = default)
        {
            SummaryInvoked = true;
            return Task.FromResult(new SubscriptionStatSummaryDto(0, 0, 0, 0, false, 0, 0, 0, null, true));
        }
    }

    /// <summary>日志查询门面 Fake——Invoked 捕获 + 空日志列表。</summary>
    internal sealed class FakeLogQueryService : ILogQueryService
    {
        public bool InteractionsInvoked { get; private set; }
        public bool PublishesInvoked { get; private set; }

        public Task<IReadOnlyList<InteractionLogEntity>> ListInteractionsAsync(
            string channelId, string? openId = null, DateTime? from = null, DateTime? to = null,
            int? triggerType = null, CancellationToken ct = default)
        {
            InteractionsInvoked = true;
            return Task.FromResult<IReadOnlyList<InteractionLogEntity>>(Array.Empty<InteractionLogEntity>());
        }

        public Task<IReadOnlyList<MenuPublishLogEntity>> ListPublishLogsAsync(string channelId, CancellationToken ct = default)
        {
            PublishesInvoked = true;
            return Task.FromResult<IReadOnlyList<MenuPublishLogEntity>>(Array.Empty<MenuPublishLogEntity>());
        }
    }

    /// <summary>网关门面 Fake——Invoked 捕获 + 空列表/固定状态（RouteAsync 内部入口不测——throw 保真）。</summary>
    internal sealed class FakeMessageGatewayService : IMessageGatewayService
    {
        public bool ListRulesInvoked { get; private set; }
        public bool SaveRuleInvoked { get; private set; }
        public bool DeleteRuleInvoked { get; private set; }
        public bool ListEndpointsInvoked { get; private set; }
        public bool SaveEndpointInvoked { get; private set; }
        public bool DeleteEndpointInvoked { get; private set; }
        public bool StatusInvoked { get; private set; }
        public bool SetEnabledInvoked { get; private set; }
        public bool ForwardLogsInvoked { get; private set; }

        public Task<IReadOnlyList<MessageRouteRuleEntity>> ListRulesAsync(string channelId, string? msgType = null, string? eventType = null, CancellationToken ct = default)
        {
            ListRulesInvoked = true;
            return Task.FromResult<IReadOnlyList<MessageRouteRuleEntity>>(Array.Empty<MessageRouteRuleEntity>());
        }

        public Task<long> SaveRuleAsync(MessageRouteRuleEntity rule, CancellationToken ct = default)
        {
            SaveRuleInvoked = true;
            return Task.FromResult(11L);
        }

        public Task<bool> DeleteRuleAsync(long ruleId, CancellationToken ct = default)
        {
            DeleteRuleInvoked = true;
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<DownstreamEndpointDto>> ListEndpointsAsync(string channelId, CancellationToken ct = default)
        {
            ListEndpointsInvoked = true;
            return Task.FromResult<IReadOnlyList<DownstreamEndpointDto>>(Array.Empty<DownstreamEndpointDto>());
        }

        public Task<long> SaveEndpointAsync(SaveEndpointRequest request, CancellationToken ct = default)
        {
            SaveEndpointInvoked = true;
            return Task.FromResult(13L);
        }

        public Task<bool> DeleteEndpointAsync(long id, CancellationToken ct = default)
        {
            DeleteEndpointInvoked = true;
            return Task.FromResult(true);
        }

        public Task<WeChatGatewayStatusDto> GetGatewayStatusAsync(CancellationToken ct = default)
        {
            StatusInvoked = true;
            return Task.FromResult(new WeChatGatewayStatusDto(true, 0, 0));
        }

        public Task SetGatewayEnabledAsync(SetGatewayEnabledRequest request, CancellationToken ct = default)
        {
            SetEnabledInvoked = true;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MessageForwardLogEntity>> ListForwardLogsAsync(string channelId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
        {
            ForwardLogsInvoked = true;
            return Task.FromResult<IReadOnlyList<MessageForwardLogEntity>>(Array.Empty<MessageForwardLogEntity>());
        }

        public Task<GatewayRouteResult> RouteAsync(WechatEventContext ctx, CancellationToken ct = default)
            => throw new NotSupportedException("RouteAsync 为事件内部入口，不生成 REST 端点（端点冒烟不涉）。");
    }

    /// <summary>状态卡门面 Fake——Invoked 捕获 + 固定未连接状态（零凭证零数据依赖）。</summary>
    internal sealed class FakeStatusService : IWeChatOperationsStatusService
    {
        public bool Invoked { get; private set; }

        public Task<WeChatOperationsStatusDto> GetStatusSummaryAsync(string channelId, CancellationToken ct = default)
        {
            Invoked = true;
            return Task.FromResult(new WeChatOperationsStatusDto(false, "no channel config", 0, 0, 0));
        }
    }
}