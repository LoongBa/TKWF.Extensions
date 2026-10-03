using FreeSql;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;

namespace TKWF.Ext.Testing.Shared;

/// <summary>
/// 测试宿主抽象基类——生产路径测试宿主公共样板（C 基座，2026-10-04 框架组裁定 C 先行）。
/// <para>API 面**镜像 A 精简版目标签名**（同名 <see cref="BindTestScope"/>）——框架 <c>TKWF.Domain.Testing</c>
/// 包定位核查确定、A 精简版落地迁移 = 替换 using + 包引用，非重写（转达文档 §6.1）。</para>
/// <para>设计约束（对齐 A 精简版安全设计，勿违背）：<c>BindTestScope</c> 走**真实 <c>DomainUser&lt;TestUserInfo&gt;.BindScope</c>**，
/// 不注入合成 CurrentAopUser（人体工学封装，非守卫绕过）；断言走真实 AOP 解析路径（见 <see cref="TestHostAssert"/>）。</para>
/// </summary>
public abstract class TestHostBase : IDisposable
{
    private ServiceProvider? _provider;
    private bool _disposed;

    /// <summary>测试用户构造（默认 <c>user-42/测试用户</c>，子类可覆写为测试专属用户）。</summary>
    protected virtual TestUserInfo CreateUser() => new("user-42", "测试用户");

    /// <summary>扩展特定：实体/VEntity 表结构同步（子类 override；无实体扩展可省略）。</summary>
    protected virtual void SyncStructure(IFreeSql fsql) { }

    /// <summary>扩展特定：额外服务/基础设施注册（子类 override；在 FreeSql 基础设施之后执行）。</summary>
    protected virtual void ConfigureHost(IServiceCollection services) { }

    /// <summary>
    /// 绑定域作用域（生产路径等价）：真实 DI + FreeSql 基础设施 + 真实 <see cref="DomainUser{TUserInfo}"/> BindScope。
    /// 返回 <c>(ServiceProvider, DomainUser&lt;TestUserInfo&gt;)</c>——经 <c>User.Use&lt;接口&gt;()</c> AOP 路径解析门面。
    /// </summary>
    protected (ServiceProvider Provider, DomainUser<TestUserInfo> User) BindTestScope(
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var fsql = CreateInMemoryFreeSql();
        SyncStructure(fsql);
        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        ConfigureHost(services);
        configure?.Invoke(services);

        var provider = services.BuildServiceProvider();
        DomainUser<TestUserInfo>.BindScope(provider);
        var user = new DomainUser<TestUserInfo> { UserInfo = CreateUser() };
        _provider = provider;
        return (provider, user);
    }

    /// <summary>SQLite 内存库（每次调用新连接 = 独立内存库）。</summary>
    protected static IFreeSql CreateInMemoryFreeSql()
    {
        var fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
        return fsql;
    }

    /// <summary>模板方法：UnBindScope + OnDispose + Provider 释放（子类跨测试生命周期清理在 <see cref="OnDispose"/>）。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            DomainUser<TestUserInfo>.UnBindScope();
        }
        catch
        {
            // BindScope 未建立的宿主 Dispose 不抛——忽略 UnBindScope 失败
        }
        OnDispose();
        _provider?.Dispose();
    }

    /// <summary>子类清理钩子（VEntity 视图外临时文件/目录、文件模式 DB 等）。</summary>
    protected virtual void OnDispose() { }
}