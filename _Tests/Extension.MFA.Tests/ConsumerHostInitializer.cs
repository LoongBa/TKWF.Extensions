using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Core.Hosting;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Hosting;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Session;
using TKW.Framework.Enumerations;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.MFA.Tests;

/// <summary>
/// V4.9.85 (B2)：消费方宿主初始化器——模拟真实消费方的 <see cref="DomainHostInitializerBase{TUserInfo}"/> 子类。
/// <para>V4.9.85 (ADR47)：消费方显式启用 MFA 扩展——<see cref="TKWFEnabledExtensionAttribute"/>
/// 声明后，SG1b 将 MFA 的能力清单聚合进本消费方领域权威注册（发现不自动启用，三钩子才接线）。</para>
/// </summary>
[TKWFEnabledExtension(typeof(MFAExtensionInitializer<>))]
public sealed class ConsumerHostInitializer : DomainHostInitializerBase<TestUserInfo>
{
    protected override IProjectMetaContext OnRegisterInfrastructureServices(
        IServiceCollection services, IConfiguration? configuration, IDomainHostOptions options)
        => new TestMetaContext();

    protected override DomainUserHelperBase<TestUserInfo> OnRegisterDomainServices(
        IServiceCollection services, IConfiguration? configuration)
        => new TestUserHelper();
}
