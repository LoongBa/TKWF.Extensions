using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Interception.Auditing;
using TKWF.Ext.AuditLogging;

namespace TKWF.Ext.AuditLogging.Tests;

/// <summary>
/// V4.10.11 生产 DI 缺口修复验证（扩展组跟进项 3）：
/// <para>
/// 缺口 3（扩展控制器暴露 DataService）：扩展内 <c>[GenerateController(FromDataService=true)]</c> 的 DataService
/// 在 SG1a RemoveAll 后不再进入扩展生成上下文 <c>GetServiceRegistrations()</c> → 生产 DI 无其注册 →
/// Store 构造注入解析失败（AuditLogging 迁移前已在 12 扩展缺陷清单）。框架 v4.10.11 修复（仅移除 Service、
/// 保留 DataService 元数据）。本测试直接读取扩展 DLL 生成的 ProjectMetaContext（生产消费方聚合的
/// 唯一来源），断言 DataService 注册存在 + Store 经生产注册路径可解析。</para>
/// <para>先例对齐：框架侧 EntityMetadataGeneratorTest.GenerateControllerFromDataService_MetaRetained。</para>
/// </summary>
public class ProductionDataServiceResolutionTests
{
    /// <summary>
    /// 生产契约回归锁：扩展生成上下文 GetServiceRegistrations() 必须含 AuditLogEntityDataService
    /// （MetaType.DataService）。v4.10.8 缺口 = 此注册缺失；v4.10.11 修复 = 恢复生成。
    /// 若框架 SG1a 再次裁剪 DataService，此测试红。
    /// </summary>
    [Fact]
    public void GeneratedContext_Registers_AuditLogEntityDataService()
    {
        var regs = TKWF.Ext.AuditLogging.Generated.ProjectMetaContext.GetOrCreateInstance()
            .GetServiceRegistrations().ToList();

        Assert.Contains(regs, r => r.Type == MetaType.DataService
                                && r.Implementation == typeof(AuditLogEntityDataService));
    }

    /// <summary>
    /// 生产 DI 解析验证：镜像 DomainHostInitializerBase.RegisterGeneratedServices 的扩展上下文聚合
    /// （Q2：扩展 DataService → AddConstructibleDataService 可构造工厂）→ 注册 IAuditLogStore → Store
    /// 构造注入 AuditLogEntityDataService 成功。修复前此链路会 Unable to resolve。
    /// </summary>
    [Fact]
    public void Store_Resolves_Through_ProductionRegistrationPath()
    {
        var services = new ServiceCollection();

        // 1. 生产聚合：遍历扩展生成上下文注册，MetaType.DataService → 可构造工厂（镜像 AddConstructibleDataService，
        //    测试环境用 DI IDomainUser 而非 AsyncLocal CurrentAopUser——对齐迁移期测试 Host 兜底工厂模式）
        var regs = TKWF.Ext.AuditLogging.Generated.ProjectMetaContext.GetOrCreateInstance()
            .GetServiceRegistrations().ToList();
        var dataServiceRegs = regs.Where(r => r.Type == MetaType.DataService).ToList();
        Assert.NotEmpty(dataServiceRegs); // 前置：DataService 注册必须存在（缺口回归锁）

        foreach (var reg in dataServiceRegs)
            AddConstructibleDataService(services, reg.Implementation);

        // 2. 消费方注册默认存储（AuditLogStore 构造注入 AuditLogEntityDataService）+ 空日志
        services.AddScoped<IAuditLogStore, AuditLogStore>();

        // 3. 基础设施：IDomainUser + IEntityDAC + ILogger（空日志）
        services.AddScoped<IDomainUser>(_ => new StubDomainUser());
        services.AddScoped<IEntityDAC<AuditLogEntity>>(_ => new InMemoryEntityDac());
        services.AddScoped<ILogger<AuditLogStore>>(_ => NullLogger<AuditLogStore>.Instance);

        var sp = services.BuildServiceProvider();

        // 4. 生产解析：Store 可解析（其内嵌 DataService 构造注入成功）
        var store = sp.GetRequiredService<IAuditLogStore>();
        Assert.IsType<AuditLogStore>(store);

        // 5. DataService 具体类本身生产可解析（缺口 3 核心断言）
        var dataService = sp.GetRequiredService<AuditLogEntityDataService>();
        Assert.NotNull(dataService);
    }

    /// <summary>测试版可构造工厂——镜像生产 AddConstructibleDataService（ActivatorUtilities + 域用户），
    /// 用户源为 DI IDomainUser（免 AsyncLocal 域作用域、xUnit 并行隔离安全）。</summary>
    private static void AddConstructibleDataService(IServiceCollection services, Type implementation)
    {
        services.AddScoped(implementation, sp =>
        {
            var user = sp.GetRequiredService<IDomainUser>();
            return ActivatorUtilities.CreateInstance(sp, implementation, user);
        });
    }

    /// <summary>最小 IDomainUser 桩——仅满足 DataService 构造。</summary>
    private sealed class StubDomainUser : IDomainUser
    {
        public string SessionKey => "test-session";
        public bool IsAuthenticated => false;
        public bool IsSystemActor => false;
        public IUserInfo? UserInfo => null;
        public long? TenantId => null;
        public bool IsNoAuditActive => false;
        public string? UserId => null;
        public string? UserName => null;
        public bool IsInRole(string role) => false;
        public TDomainService Use<TDomainService>() where TDomainService : IDomainService
            => throw new NotSupportedException("Stub: Use<T> not supported in unit tests");
        public TService GetService<TService>() where TService : notnull
            => throw new NotSupportedException("Stub: GetService<T> not supported in unit tests");
        public TService GetOptionalService<TService>() where TService : class => null!;
        public IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
    }

    /// <summary>内存桩 IEntityDAC&lt;AuditLogEntity&gt;（对齐测试既有桩）。</summary>
    private sealed class InMemoryEntityDac : IEntityDAC<AuditLogEntity>
    {
        private readonly List<AuditLogEntity> _items = new();
        private long _nextId = 1;

        public IReadOnlyList<AuditLogEntity> Items => _items;

        public IQueryable<AuditLogEntity> Query => _items.AsQueryable();

        public Task<AuditLogEntity?> FirstOrDefaultAsync(
            IQueryable<AuditLogEntity> query, CancellationToken ct = default)
            => Task.FromResult(query.FirstOrDefault());

        public Task<List<AuditLogEntity>> ToListAsync(
            IQueryable<AuditLogEntity> query, CancellationToken ct = default)
            => Task.FromResult(query.ToList());

        public Task<long> CountAsync(
            IQueryable<AuditLogEntity> query, CancellationToken ct = default)
            => Task.FromResult((long)query.Count());

        public Task<TResult?> FirstOrDefaultAsync<TResult>(
            IQueryable<AuditLogEntity> query,
            Expression<Func<AuditLogEntity, TResult>> selector,
            CancellationToken ct = default)
            => Task.FromResult(query.Select(selector).FirstOrDefault());

        public Task<List<TResult>> ToListAsync<TResult>(
            IQueryable<AuditLogEntity> query,
            Expression<Func<AuditLogEntity, TResult>> selector,
            CancellationToken ct = default)
            => Task.FromResult(query.Select(selector).ToList());

        public Task<AuditLogEntity> InsertAsync(
            AuditLogEntity entity, CancellationToken ct = default)
        {
            if (entity.Id == 0) entity.Id = _nextId++;
            _items.Add(entity);
            return Task.FromResult(entity);
        }

        public Task<List<AuditLogEntity>> InsertBatchAsync(
            IEnumerable<AuditLogEntity> entities, CancellationToken ct = default)
        {
            var list = entities.ToList();
            foreach (var entity in list) _items.Add(entity);
            return Task.FromResult(list);
        }

        public Task<bool> DeleteAsync(
            AuditLogEntity entity, CancellationToken ct = default)
            => Task.FromResult(_items.Remove(entity));

        public Task UpdateAsync(AuditLogEntity entity, CancellationToken ct = default)
        {
            var index = _items.FindIndex(x => x.Id == entity.Id);
            if (index >= 0) _items[index] = entity;
            return Task.CompletedTask;
        }

        public Task UpdateBatchAsync(
            IEnumerable<AuditLogEntity> entities, CancellationToken ct = default)
        {
            foreach (var entity in entities) UpdateAsync(entity, ct);
            return Task.CompletedTask;
        }

        public Task<int> UpdateColumnsBatchAsync<TColumns>(
            IEnumerable<AuditLogEntity> entities,
            Expression<Func<AuditLogEntity, TColumns>> columns,
            CancellationToken ct = default)
        {
            foreach (var entity in entities) UpdateAsync(entity, ct);
            return Task.FromResult(entities.Count());
        }
    }
}