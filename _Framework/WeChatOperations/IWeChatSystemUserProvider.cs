using System;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.WeChatOperations;

/// <summary>
/// 系统用户供给器（内部桥——作业帧内取 SystemActor 用户）。
/// <para>背景：<see cref="WeChatMessageForwardJob"/> 在 BackgroundJobWorker SystemActor 帧内执行
/// （框架 <c>BeginSystemScopeAsync</c> 已设 <see cref="TKW.Framework.Domain.StandaloneDomainUserAccessor{TUserInfo}"/>
/// ——三桥实证），但作业类<b>非泛型</b>（<c>[BackgroundJob]</c> 具体类，消费方 <c>AddBackgroundJobs&lt;TUserInfo&gt;()</c>
/// 入队），无法直接引用泛型访问器 <see cref="TKW.Framework.Domain.Interfaces.IDomainUserAccessor{TUserInfo}"/>——
/// 由泛型 Initializer 闭包 TUserInfo 注册本桥（内部类型，不公开），作业经本桥取系统用户后
/// <c>User.Use&lt;具体 DataService&gt;()</c> NoAop 直建（对齐 <c>TKWF.Ext.BackgroundJobs.JobExecutionRecorder&lt;TUserInfo&gt;</c>
/// SystemUser NoAop 直建先例——V4.10.55 ADR92/T3）。</para>
/// <para>⚠️ 使用前提：本桥只在 BackgroundJobWorker SystemActor 帧内可解析——帧外调用抛
/// <see cref="InvalidOperationException"/>（StandaloneDomainUserAccessor.DomainUser 缺绑定即抛——fail-fast）。</para>
/// </summary>
internal interface IWeChatSystemUserProvider
{
    /// <summary>取当前 SystemActor 帧内系统用户（BackgroundJobWorker BeginSystemScopeAsync 已设；帧外抛）。</summary>
    IDomainUser GetSystemUser();
}

/// <summary>委托形态实现（Initializer 闭包 accessor 注册——避免新 DI 类型）。</summary>
internal sealed class DelegateWeChatSystemUserProvider : IWeChatSystemUserProvider
{
    private readonly Func<IDomainUser> _factory;

    public DelegateWeChatSystemUserProvider(Func<IDomainUser> factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <inheritdoc />
    public IDomainUser GetSystemUser() => _factory();
}
