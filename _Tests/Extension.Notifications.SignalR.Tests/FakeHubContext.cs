using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace TKWF.Ext.Notifications.SignalR.Tests;

/// <summary>捕获型 IHubContext 桩——记录 (userIdentifier, method, payload) 投递调用，供断言。
/// <para>net10 中 <see cref="IHubContext{THub}.Clients"/> 类型为 <see cref="IHubClients"/>
/// （继承 <see cref="IHubClients{T}"/>，T=IClientProxy）；<c>Clients/Groups/Users</c> 均返回<b>单个</b> T
/// （集合目标内部管理，接口面单 proxy）。</para></summary>
internal sealed class FakeHubContext : IHubContext<NotificationsHub>
{
    public List<CapturedCall> Calls { get; } = new();

    public IHubClients Clients => new FakeHubClients(this);

    public IGroupManager Groups => throw new NotSupportedException("Groups 测试不需要");

    /// <summary>捕获的一次 SendAsync 调用。</summary>
    public sealed record CapturedCall(string UserIdentifier, string Method, object? Payload);
}

/// <summary>捕获型 IHubClients 桩——User(userId) 返回记录型 IClientProxy，记录调用。
/// <para>net10 结构：<see cref="IHubClients"/> 继承 <see cref="IHubClients{T}"/>（T=IClientProxy），
/// 成员均返回<b>单个</b> T；<c>Client</c> 在 <see cref="IHubClients"/> 有 DIM 默认实现
/// （包装为 NonInvokingSingleClientProxy）——实现类只须匹配泛型接口的 <c>IClientProxy Client</c>。</para></summary>
internal sealed class FakeHubClients : IHubClients
{
    private readonly FakeHubContext _hub;

    public FakeHubClients(FakeHubContext hub) => _hub = hub;

    public IClientProxy All => new FakeClientProxy(_hub);
    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => new FakeClientProxy(_hub);
    public IClientProxy Client(string connectionId) => new FakeClientProxy(_hub);
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new FakeClientProxy(_hub);
    public IClientProxy Group(string groupName) => new FakeClientProxy(_hub);
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new FakeClientProxy(_hub);
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => new FakeClientProxy(_hub);
    public IClientProxy User(string userId) => new FakeClientProxy(_hub, userId);
    public IClientProxy Users(IReadOnlyList<string> userIds) => new FakeClientProxy(_hub);
}

/// <summary>捕获型 IClientProxy 桩——SendCoreAsync 记录调用（notifier 仅用 User().SendAsync，无需 ISingleClientProxy）。</summary>
internal sealed class FakeClientProxy : IClientProxy
{
    private readonly FakeHubContext _hub;
    private readonly string? _userIdentifier;

    public FakeClientProxy(FakeHubContext hub, string? userIdentifier = null)
    {
        _hub = hub;
        _userIdentifier = userIdentifier;
    }

    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
        _hub.Calls.Add(new FakeHubContext.CapturedCall(_userIdentifier ?? "<all>", method, args.FirstOrDefault()));
        return Task.CompletedTask;
    }
}

/// <summary>可抛异常的捕获型 IClientProxy 桩——模拟 SendAsync 失败（best-effort 不重抛）。</summary>
internal sealed class ThrowingClientProxy : IClientProxy
{
    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("模拟 SignalR 发送失败");
}