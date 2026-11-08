using System;
using System.IO;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.WeChatChannels.Tests;

/// <summary>
/// 通道底座测试宿主——生产路径（真实 DI：WeChatChannelsExtensionInitializer ConfigureServices 三态注册 +
/// FreeSql SQLite <b>文件模式</b> + 真实 BindScope）。对齐 WeChatOperationsTestHost 宿主形态，仅按
/// 通道底座自身依赖裁剪（无平台库/Federation 门面/FeatureManagement/网关——底座零运营依赖）。
/// <para>数据库形态：文件模式（DateTimeKind=Utc）——对齐 WeChatOperationsTestHost 先例（SQLite :memory:
/// 连接池下每连接独立内存库，跨 UoW 写读错乱）；共享单 UoW = 单连接单库，裸 fsql.Select 断言同库可读。</para>
/// <para>测试类继承本类：BindScope() → <c>User.Use&lt;IChannelConfigService&gt;()</c> AOP 解析门面；
/// <c>using var _ = this</c> 触发 UnBindScope + 清理临时库文件。</para>
/// </summary>
public class WeChatChannelsTestHost : IDisposable
{
    private ServiceProvider? _provider;
    private string? _dbPath;
    private bool _disposed;

    /// <summary>绑定域作用域（生产路径等价）：真实 DI + FreeSql 文件库 + 真实 <see cref="DomainUser{TUserInfo}"/> BindScope。</summary>
    public (ServiceProvider Provider, DomainUser<TestUserInfo> User) BindScope()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var fsql = CreateFileFreeSql(out _dbPath);
        SyncStructure(fsql);
        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        ConfigureHost(services);
        var provider = services.BuildServiceProvider();
        DomainUser<TestUserInfo>.BindScope(provider);
        var user = new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("user-42", "测试用户") };
        _provider = provider;
        return (provider, user);
    }

    /// <summary>SQLite 文件模式库（每次新建唯一临时文件——对齐 FileManagement/WeChatOperations CreateFileFreeSql 先例；
    /// DateTimeKind=Utc 统一 UTC 存储语义）。</summary>
    private static IFreeSql CreateFileFreeSql(out string dbPath)
    {
        dbPath = Path.Combine(Path.GetTempPath(), "tkwf-wxchannels-db-" + Guid.NewGuid().ToString("N") + ".db");
        return new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, $"Data Source={dbPath};DateTimeKind=Utc")
            .UseAutoSyncStructure(true)
            .Build();
    }

    /// <summary>实体表结构同步（通道底座唯一实体）。</summary>
    protected void SyncStructure(IFreeSql fsql)
    {
        fsql.CodeFirst.SyncStructure<WeChatChannelConfigEntity>();
    }

    /// <summary>额外服务/基础设施注册（AddLogging + IFreeSql + UnitOfWorkManager 已在 BindScope 注册）。</summary>
    protected void ConfigureHost(IServiceCollection services)
    {
        // ① 扩展装配——生产形态（消费方白名单启用后三钩子执行 ConfigureServices：
        //    AddConstructibleService 门面 + keyed ISymmetricKeyProvider + DB 凭证解析器/通道来源两源并存）
        new WeChatChannelsExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // ② 测试桥接：IDomainUser 注册（DataService ctor 需 IDomainUser——接线型 DbWeChatCredentialResolver/
        //    WeChatChannelRegistrySource 的 GetRequiredService&lt;DataService&gt;() 需 DI 可构造；生产永不注册，D01）
        var user = new StubDomainUser();
        services.AddSingleton<IDomainUser>(sp => { user.ServiceProvider = sp; return user; });

        // ③ IEntityDAC&lt;T&gt; 基础设施（共享单 UoW——同连接同库数据可见，对齐 Settings/AuthCenter 先例）
        services.AddSingleton<IEntityDAC<WeChatChannelConfigEntity>>(sp => new FreeSqlEntityDAC<WeChatChannelConfigEntity>(sp.GetRequiredService<UnitOfWorkManager>()));

        // ④ DataService 可构造注册（测试镜像生产 ADR61 可构造工厂，非 throw-factory——接线型 GetRequiredService 需要）
        services.AddSingleton<WeChatChannelConfigEntityDataService>();

        // ⑤ Options 兜底默认值（守卫工厂 ActivatorUtilities 解析剩余参数需 IOptions 可解析；IsProduction=false——
        //    测试环境开发模式：FileSymmetricKeyProvider 随机兜底密钥，生产模式缺密钥文件 fail-fast 会拒绝启动）
        services.AddOptions<WeChatChannelsOptions>().Configure(o => o.IsProduction = false);
    }

    /// <summary>模板方法：UnBindScope + Provider 释放 + 清理临时库文件。</summary>
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
        _provider?.Dispose();
        if (_dbPath is not null && File.Exists(_dbPath))
        {
            try
            {
                File.Delete(_dbPath);
            }
            catch
            {
                // 库文件被占用（罕见）——忽略，系统 temp 清理兜底
            }
        }
    }
}