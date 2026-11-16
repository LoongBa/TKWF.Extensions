using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Core.Hosting;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Hosting;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Session;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.SmsCode.Tests;

/// <summary>
/// 消费方宿主初始化器——模拟真实消费方的 <see cref="DomainHostInitializerBase{TUserInfo}"/> 子类。
/// <para>ADR47 白名单：短信验证码扩展——SG1b 聚合能力清单（对齐 WeChatChannels/AuthCenter ConsumerHostInitializer 形态；
/// 测试项目无 [DomainGenerateCode] 实体 → 用最小空桩 <see cref="TestMetaContext"/>）。</para>
/// </summary>
// 白名单：SmsCode 扩展（M1 门面 V0.1.0——SmsCodeExtensionInitializer 已落地）
[TKWFEnabledExtension(typeof(SmsCodeExtensionInitializer<>))]
public sealed class ConsumerHostInitializer : DomainHostInitializerBase<TestUserInfo>
{
    protected override IProjectMetaContext OnRegisterInfrastructureServices(
        IServiceCollection services, IConfiguration? configuration, IDomainHostOptions options)
        => new TestMetaContext();

    protected override DomainUserHelperBase<TestUserInfo> OnRegisterDomainServices(
        IServiceCollection services, IConfiguration? configuration)
        => new TestUserHelper();
}