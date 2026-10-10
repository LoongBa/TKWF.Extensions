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
using TKWF.Ext.TrustCenter;

namespace TKWF.Ext.Federation.Tests;

/// <summary>
/// 测试项目共享 Web 宿主（T3 装配面）——<c>DomainHost&lt;TUserInfo&gt;.Root</c> 进程内单例
/// （"DomainHost 不能重复初始化"），全测试项目**仅可建 1 个 Web 宿主**——FederationWebExtension
/// 端点冒烟（FederationWebExtensionTests）专用。
/// <para>含：<see cref="FederationWebExtension{TUserInfo}"/>（端点映射）+ Fake 门面（端点行为断言——
/// 普通 Scoped 经 RegisterServices 替换守卫工厂——ISsoLogin/ISsoChannelFactory 由 Federation 初始器
/// AddConstructibleService 注册后替换；IToken2Service 契约在 TrustCenter.Abstractions（经 Federation 传递），
/// 测试项目不引 TrustCenter 主包 → AddScoped 直接注册 Fake）+ FakeSsoChannel（AuthenticateAsync 断言
/// SsoChannelAuthContext 参数透传）。宿主参数对齐 AuthCenterEndpointHost 先例：Development
/// （ValidateScopes=true 回归哨兵）+ TKWDomain:Name + UseNoEntityDAC + ExcludedPathPrefixes /sso。</para>
/// </summary>
internal static class FederationEndpointHost
{
    public static TestServer Server => _lazy.Value;
    private static readonly Lazy<TestServer> _lazy = new(CreateServer);

    // ─── Fake 门面（端点测试断言 Invoked/结果） ───
    public static FakeSsoLogin Login { get; } = new();
    public static FakeSsoChannelFactory Factory { get; } = new();
    public static FakeSsoChannel Channel { get; } = new("wechat-mp");
    public static FakeToken2Service Token2 { get; } = new();

    /// <summary>共享宿主状态复位（跨用例防污染——负向/正向断言前置调用，对齐 AuthCenter V0.8.0 复位范式）。</summary>
    public static void Reset()
    {
        Login.Reset();
        Factory.Reset();
        Channel.Reset();
        Channel.Result = new SsoChannelAuthResult(true, "ext-100", null, 2);
        Factory.Channel = Channel;
        Login.Factory = Factory;    // Fake 登录编排复用宿主工厂（共享实例——默认通道/选区走同一通道源）
        Token2.Reset();
    }

    private static TestServer CreateServer()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Environment.EnvironmentName = "Development";   // ValidateScopes=true 回归哨兵
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TKWDomain:Name"] = "FederationEndpointSmoke"
        });

        var app = builder.ConfigWebAppDomain<TestUserInfo, ConsumerHostInitializer, DomainWebOptions>(configure: cfg =>
        {
            cfg.UseNoEntityDAC();                               // 冒烟无数据存取（门面 Fake）
            cfg.HttpAuth.ExcludedPathPrefixes.Add("/sso");      // 匿名端点免认证双保险
        })
        .RegisterServices((svc, _) =>
        {
            svc.Replace(ServiceDescriptor.Scoped<ISsoLogin>(_ => Login));
            svc.Replace(ServiceDescriptor.Scoped<ISsoChannelFactory>(_ => Factory));
            // IToken2Service：契约在 TrustCenter.Abstractions（经 Federation 传递），测试项目未引 TrustCenter
            // 主包 → 无既有注册，AddScoped 直接注册 Fake（GetGuest.Use<T>() → GetRequiredService 解析）
            svc.AddScoped<IToken2Service>(_ => Token2);
        })
        .UseWebSession()
        .UseWebExtensions<TestUserInfo>(e =>
        {
            e.Add<FederationWebExtension<TestUserInfo>>();
        })
        .BeforeRouting((_, _) => { })              // 返回 BeforeRoutingBuilder——UseAspNetCoreRouting 为其成员
        .UseAspNetCoreRouting()
        .AfterRouting((_, _) => { })
        .Build();

        app.StartAsync().GetAwaiter().GetResult();
        return app.GetTestServer();
    }

    // ─── Fake 门面 / 通道 ───

    /// <summary>Fake 登录编排门面——镜像真实 SsoLogin 编排（经 Fake 工厂构造通道 → AuthenticateAsync），
    /// 供端点参数透传断言（SsoChannelAuthContext 到通道）。</summary>
    internal sealed class FakeSsoLogin : ISsoLogin
    {
        public FakeSsoChannelFactory Factory { get; set; } = new();
        public bool Invoked { get; private set; }
        public bool DefaultInvoked { get; private set; }
        public string? LastChannelId { get; private set; }
        public SsoChannelAuthContext? LastContext { get; private set; }

        public void Reset() { Invoked = false; DefaultInvoked = false; LastChannelId = null; LastContext = null; }

        public async Task<SsoChannelAuthResult> LoginAsync(string channelId, SsoChannelAuthContext context, CancellationToken ct = default)
        {
            Invoked = true;
            LastChannelId = channelId;
            LastContext = context;
            var channel = await Factory.CreateAsync(channelId, null, ct);
            return channel is null
                ? new SsoChannelAuthResult(false, null, "CHANNEL_NOT_FOUND", 0)
                : await channel.AuthenticateAsync(context, ct);
        }

        public async Task<SsoChannelAuthResult> LoginDefaultAsync(SsoChannelAuthContext context, CancellationToken ct = default)
        {
            DefaultInvoked = true;
            LastContext = context;
            var channel = await Factory.CreateDefaultAsync(ct);
            return channel is null
                ? new SsoChannelAuthResult(false, null, "CHANNEL_NOT_FOUND", 0)
                : await channel.AuthenticateAsync(context, ct);
        }
    }

    /// <summary>Fake 通道工厂——按 ChannelId 精确匹配返回 <see cref="Channel"/>（未知 id → null → CHANNEL_NOT_FOUND 404）。</summary>
    internal sealed class FakeSsoChannelFactory : ISsoChannelFactory
    {
        public FakeSsoChannel? Channel { get; set; }
        public bool CreateInvoked { get; private set; }
        public string? LastChannelId { get; private set; }

        public void Reset() { CreateInvoked = false; LastChannelId = null; }

        public Task<ISsoChannel?> CreateAsync(string channelId, string? channelType = null, CancellationToken ct = default)
        {
            CreateInvoked = true;
            LastChannelId = channelId;
            return Task.FromResult(Channel is not null && string.Equals(Channel.ChannelId, channelId, StringComparison.Ordinal)
                ? (ISsoChannel?)Channel
                : null);
        }

        public Task<ISsoChannel?> CreateDefaultAsync(CancellationToken ct = default)
            => Task.FromResult(Channel is null ? null : (ISsoChannel)Channel);
    }

    /// <summary>Fake 通道——AuthenticateAsync 记录上下文 + 返回可配置 Result（默认成功 ext-100/AuthLevel 2）。</summary>
    internal sealed class FakeSsoChannel : ISsoChannel
    {
        public FakeSsoChannel(string channelId) => ChannelId = channelId;

        public string ChannelType => "fake_oauth";
        public string ChannelId { get; }
        public SsoChannelAuthResult Result { get; set; } = new(true, "ext-100", null, 2);
        public SsoChannelAuthContext? LastContext { get; private set; }
        public bool Authenticated { get; private set; }

        public void Reset() { LastContext = null; Authenticated = false; }

        public Task<SsoChannelAuthResult> AuthenticateAsync(SsoChannelAuthContext context, CancellationToken ct = default)
        {
            Authenticated = true;
            LastContext = context;
            return Task.FromResult(Result);
        }
    }

    /// <summary>Fake token2 服务（信任内核契约——测试项目不引 TrustCenter 主包，Web 层直注 Fake）——JWKS 分发断言。</summary>
    internal sealed class FakeToken2Service : IToken2Service
    {
        public string JwksJson { get; set; } = """{"keys":[{"kid":"sso-key-1","kty":"EC","crv":"P-256"}]}""";
        public bool JwksInvoked { get; private set; }

        public void Reset() => JwksInvoked = false;

        public string GetJwksJson()
        {
            JwksInvoked = true;
            return JwksJson;
        }

        public Task<Token2IssueResult> IssueToken2Async(Token2IssueRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Token2ValidationResult> ValidateToken2Async(string token, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}