using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Core.Hosting;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Hosting;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Session;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.Captcha.Tests;

/// <summary>
/// 消费方宿主初始化器——模拟真实消费方的 <see cref="DomainHostInitializerBase{TUserInfo}"/> 子类。
/// <para>ADR47 白名单：人机验证扩展（v0.1.0 M2——CaptchaExtensionInitializer 已落地）——SG1b 聚合能力清单
/// （对齐 WeChatChannels/AuthCenter ConsumerHostInitializer 形态；测试项目无 [DomainGenerateCode] 实体 →
/// 用最小空桩 <see cref="TestMetaContext"/>）。Captcha 纯内存型扩展零实体——本初始化器仅承载白名单声明语义。</para>
/// </summary>
// 白名单：Captcha 扩展（v0.1.0——CaptchaExtensionInitializer 已落地）
[TKWFEnabledExtension(typeof(CaptchaExtensionInitializer<>))]
public sealed class ConsumerHostInitializer : DomainHostInitializerBase<TestUserInfo>
{
    protected override IProjectMetaContext OnRegisterInfrastructureServices(
        IServiceCollection services, IConfiguration? configuration, IDomainHostOptions options)
        => new TestMetaContext();

    protected override DomainUserHelperBase<TestUserInfo> OnRegisterDomainServices(
        IServiceCollection services, IConfiguration? configuration)
        => new TestUserHelper();
}