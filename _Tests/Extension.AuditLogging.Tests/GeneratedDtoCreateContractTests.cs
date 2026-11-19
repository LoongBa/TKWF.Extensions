using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.CodeGeneration;
using TKWF.Ext.AuditLogging.DTOs;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.AuditLogging.Tests;

/// <summary>
/// 生成 DTO → <c>DataService.CreateAsync(TDto)</c> 契约测试（B3，两阶段）。
/// <para><b>阶段 1（已结束，2026-11-20 v4.10.122 部署）</b>：框架缺陷 F-03 期间，xCodeGen 生成的 DTO 的
/// <c>IDomainDto&lt;TEntity&gt;.ToEntity</c> 是抛 <see cref="NotSupportedException"/> 的桩，
/// 而基类 <c>CreateAsync</c>/<c>CreateBatchAsync</c> 经接口调用它 → 生成 DTO 的 DTO 写入主路径必抛；
/// 本仓历史零调用点（全员绕行 <c>EntityCreateAsync(entity)</c>），缺陷被生态惯性掩盖。</para>
/// <para><b>阶段 2（当前）</b>：框架侧修复（A1a 基类改走 <c>ApplyToEntity</c>、A1b 模板改真实现）已随 v4.10.122 部署并重生成
/// 本扩展生成物后，去掉 Skip → 转绿 → 合入，作为该修复的扩展侧回归网（框架测试树只有手写 DTO，覆盖不到生成 DTO 层）。</para>
/// <para><b>断言口径说明</b>：本仓生成实体当前无一实现 <c>IEntityTracked</c>/<c>IEntityAuditable</c>
/// （<c>*.g.cs</c> 全仓 0 命中，2026-11-20 实测），故只断言 <c>Id</c> 由数据库分配、DTO 无法伪造；
/// UId/CreateTime 等托管覆写语义待框架确认 AutoManagedFields 是否落到托管接口实现后，由具备该接口的实体补断言。</para>
/// </summary>
public class GeneratedDtoCreateContractTests
{
    /// <summary>安装 SG1 生成的项目元数据（ApplyToEntity 映射的前置）：
    /// <see cref="TKW.Framework.Domain.xCodeGen.ValidationCache{T}"/>.Meta 读 <see cref="ProjectMetaContextBase.Instance"/>
    /// 的 GetPropertyMap；缺失时 Meta 为空 → CreateMapper 无属性元数据 → 映射静默不发生（生成 DTO 写入落空）。</summary>
    static GeneratedDtoCreateContractTests()
        => MetaContextInstaller.Install(TKWF.Ext.AuditLogging.Generated.ProjectMetaContext.GetOrCreateInstance());

    private static IFreeSql CreateInMemoryFreeSql()
    {
        var fsql = new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
        fsql.CodeFirst.SyncStructure<AuditLogEntity>();
        return fsql;
    }

    /// <summary>消费方生产形态 DI（镜像 <see cref="AuditLogProductionPathTests"/>）：Initializer.ConfigureServices + FreeSql 基础设施。
    /// DataService 经 <c>User.Use&lt;具体类&gt;()</c> NoAop 直建（IEntityDAC 从 DI 解析，数据访问红线合规）。</summary>
    private static ServiceProvider CreateProvider(IFreeSql fsql)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new AuditLoggingExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        services.AddSingleton<IEntityDAC<AuditLogEntity>, FreeSqlEntityDAC<AuditLogEntity>>();

        // 仅测试桩：普通 DI 可构造解析源（生产 IDomainUser 永不注册 DI，经 DomainServiceBase 基类 User）
        services.AddScoped<IDomainUser>(_ => new StubDomainUser());
        services.AddScoped<AuditLogEntityDataService>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task CreateAsync_GeneratedDto_PersistsRow_DbAssignsId()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        DomainUser<TestUserInfo>.BindScope(sp);
        var user = new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("user-42", "测试用户") };
        var ds = user.Use<AuditLogEntityDataService>();

        // 生成 DTO（xCodeGen 产物：required ServiceName/MethodName/CreateTime）；Id 置 0 = 不伪造主键
        var dto = new AuditLogEntityDto
        {
            Id = 0,
            UserName = "alice",
            UserId = "u1",
            ServiceName = "GeneratedDtoContract",
            MethodName = "CreateAsync",
            ExecutionTime = DateTime.UtcNow,
            DurationMs = 12,
            Success = true,
            CorrelationId = "corr-dto",
            CreateTime = DateTimeOffset.Now
        };

        var created = await ds.CreateAsync(dto, TestContext.Current.CancellationToken);

        // C1/C2：DTO 写入成功，且 Id 由数据库分配（DTO 置 0 不生效）
        Assert.True(created.Id > 0);
        Assert.Equal("GeneratedDtoContract", created.ServiceName);

        var stored = await fsql.Select<AuditLogEntity>().Where(x => x.Id == created.Id).FirstAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(stored);
        Assert.Equal("alice", stored!.UserName);
        Assert.Equal("CreateAsync", stored.MethodName);
    }

    [Fact]
    public async Task CreateBatchAsync_GeneratedDtos_PersistsTwoRows()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        DomainUser<TestUserInfo>.BindScope(sp);
        var user = new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("user-42", "测试用户") };
        var ds = user.Use<AuditLogEntityDataService>();

        var dtos = new[]
        {
            new AuditLogEntityDto
            {
                ServiceName = "GeneratedDtoContract",
                MethodName = "BatchA",
                UserName = "alice",
                ExecutionTime = DateTime.UtcNow,
                DurationMs = 1,
                Success = true,
                CreateTime = DateTimeOffset.Now
            },
            new AuditLogEntityDto
            {
                ServiceName = "GeneratedDtoContract",
                MethodName = "BatchB",
                UserName = "bob",
                ExecutionTime = DateTime.UtcNow,
                DurationMs = 2,
                Success = false,
                CreateTime = DateTimeOffset.Now
            }
        };

        var created = await ds.CreateBatchAsync(dtos, TestContext.Current.CancellationToken);

        // C3：批量 DTO 写入两行、Id 互异
        Assert.Equal(2, created.Count);
        Assert.All(created, d => Assert.True(d.Id > 0));
        Assert.Equal(2, created.Select(d => d.Id).Distinct().Count());

        var total = await fsql.Select<AuditLogEntity>().Where(x => x.ServiceName == "GeneratedDtoContract").CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, total);
    }
    /// <summary>ProjectMetaContextBase.Instance 安装器（setter 为 protected，经子类公开）——对齐全仓测试先例（FakeMetaContext）。</summary>
    private sealed class MetaContextInstaller : ProjectMetaContextBase
    {
        public static void Install(IProjectMetaContext context) => Instance = context;

        // 仅安装器所需的抽象成员占位（对齐 Navigation FakeMetaContext 先例：Configuration/ChangeLog 不被消费）
        public override ProjectConfiguration Configuration => null!;
        public override MetadataChangeLog ChangeLog => null!;
        public override string MetadataSchemaVersion => "test";
    }
}