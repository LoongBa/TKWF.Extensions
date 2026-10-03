using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Permissions;
using TKWF.Ext.Permissions.Abstractions;

namespace TKWF.Ext.Permissions.Consumer.Tests;

/// <summary>
/// V4.10.11 生产 DI 缺口修复验证（扩展组跟进项 3）+ V4.10.53 领域自治根治（DataService 注册契约更新）。
/// <para>
/// 缺口 3（扩展控制器暴露 DataService）：扩展内 <c>[GenerateController(FromDataService=true)]</c> 的 DataService
/// 在 SG1a RemoveAll 后不再进入扩展生成上下文 <c>GetServiceRegistrations()</c> → 生产 DI 无其注册 →
/// Store 构造注入解析失败（v4.10.8 迁移引入 Permissions 回归）。框架 v4.10.11 修复（仅移除 Service、
/// 保留 DataService 元数据）。本测试直接读取扩展 DLL 生成的 ProjectMetaContext（生产消费方聚合的
/// 唯一来源），断言 DataService 注册存在 + 注册形态符合 V4.10.53 正确路线（throw-factory，禁直接 DI 解析，
/// 运行期经 <c>Use&lt;PermissionGrantEntityDataService&gt;()</c> NoAop 直建）。</para>
/// <para>先例对齐：框架侧 EntityMetadataGeneratorTest.GenerateControllerFromDataService_MetaRetained。</para>
/// <para><b>2026-09-14 可靠性修复（全量并行 flaky）</b>：本类与 ConsumerIntegrationTests 共享全局静态
/// <see cref="ProjectMetaContextBase.Instance"/>（后者经 FakeConsumerMetaContext.Install 写入）——xunit v3
/// 默认跨测试类并行，Install 窗口期内 <c>GetOrCreateInstance()</c>（Instance ??=）会返回被污染实例 → 断言偶发失败。
/// 修复：① 本类与 ConsumerIntegrationTests 加入同一 <c>[Collection]</c> 串行化（消除竞态）；② 断言前经
/// <see cref="InstanceGuard"/> 清空/恢复 Instance（对齐 Navigation FakeMetaContext Install/Restore 先例，双保险）。</para>
/// </summary>
[Collection("SharedProjectMetaContextInstance")]
public class ProductionDataServiceResolutionTests
{
    /// <summary>
    /// 生产契约回归锁：扩展生成上下文 GetServiceRegistrations() 必须含 PermissionGrantEntityDataService
    /// （MetaType.DataService）。v4.10.8 缺口 = 此注册缺失；v4.10.11 修复 = 恢复生成。
    /// 若框架 SG1a 再次裁剪 DataService，此测试红。
    /// </summary>
    [Fact]
    public void GeneratedContext_Registers_PermissionGrantEntityDataService()
    {
        var original = ProjectMetaContextBase.Instance;
        try
        {
            InstanceGuard.Clear();   // 防并行污染：强制 GetOrCreateInstance 构造 Permissions 生成上下文（Instance ??=）

            var regs = TKWF.Ext.Permissions.Generated.ProjectMetaContext.GetOrCreateInstance()
                .GetServiceRegistrations().ToList();

            Assert.Contains(regs, r => r.Type == MetaType.DataService
                                    && r.Implementation == typeof(PermissionGrantEntityDataService));
        }
        finally
        {
            InstanceGuard.Restore(original);
        }
    }

    /// <summary>
    /// V4.10.53 生产 DI 契约验证：镜像 DomainHostInitializerBase.RegisterGeneratedServices 的扩展上下文聚合
    /// （DataService → AddService throw-factory——旧 AddConstructibleDataService 可构造工厂已删除）——
    /// DataService 禁止直接 DI 解析（领域架构守卫，防绕过 User.Use&lt;T&gt;() 创建）；运行期经
    /// <c>Use&lt;PermissionGrantEntityDataService&gt;()</c>（NoAop，ActivatorUtilities 直建 + IEntityDAC 从 DI 解析）。
    /// </summary>
    [Fact]
    public void DataService_RegisteredAsThrowFactory_ResolvesViaUse()
    {
        var original = ProjectMetaContextBase.Instance;
        try
        {
            InstanceGuard.Clear();   // 防并行污染（同 GeneratedContext 测试）

            var services = new ServiceCollection();

            // 1. 生产聚合：MetaType.DataService → throw-factory（V4.10.53 正确路线，镜像 AddService）
            var regs = TKWF.Ext.Permissions.Generated.ProjectMetaContext.GetOrCreateInstance()
                .GetServiceRegistrations().ToList();
            var dataServiceRegs = regs.Where(r => r.Type == MetaType.DataService).ToList();
            Assert.NotEmpty(dataServiceRegs); // 前置：DataService 注册必须存在（缺口回归锁）

            foreach (var reg in dataServiceRegs)
            {
                services.AddScoped(reg.Implementation,
                    _ => throw new InvalidOperationException(
                        $"[领域架构守卫] 类型 {reg.Implementation.Name} 必须通过 User.Use<T>() 解析，不允许直接从 DI 容器获取。"));
            }

            // 2. 基础设施：IEntityDAC（内存桩）——Use<DataService>() NoAop 直建所需
            services.AddScoped<IEntityDAC<PermissionGrantEntity>>(_ => new InMemoryEntityDac());

            var sp = services.BuildServiceProvider();

            // 3. 直接 DI 解析 → throw-factory 抛「领域架构守卫」（禁止绕过 Use<T> 创建——领域自治铁律）
            var ex = Assert.Throws<InvalidOperationException>(
                () => sp.GetRequiredService<PermissionGrantEntityDataService>());
            Assert.Contains("领域架构守卫", ex.Message);

            // 4. 生产路径：真实 DomainUser + BindScope → Use<PermissionGrantEntityDataService>()（NoAop 直建）
            DomainUser<ConsumerUserInfo>.BindScope(sp);
            var user = new DomainUser<ConsumerUserInfo>();
            var dataService = user.Use<PermissionGrantEntityDataService>();
            Assert.NotNull(dataService);
        }
        finally
        {
            InstanceGuard.Restore(original);
        }
    }

    /// <summary>
    /// Instance 访问器——基类 <see cref="ProjectMetaContextBase.Instance"/> setter 为 protected，
    /// 经子类暴露 Clear/Restore（对齐 <c>FakeConsumerMetaContext.Install/Restore</c> 先例，Navigation FakeMetaContext 同构）。
    /// </summary>
    private sealed class InstanceGuard : ProjectMetaContextBase
    {
        public static void Clear() => Instance = null;
        public static void Restore(IProjectMetaContext? original) => Instance = original;

        // 基类抽象成员（本桩仅作 Instance 访问载体，元数据内容不使用——返回最小默认）
        public override ProjectConfiguration Configuration => null!;
        public override MetadataChangeLog ChangeLog => null!;
        public override string MetadataSchemaVersion => "1.0";
    }

    /// <summary>内存桩 IEntityDAC&lt;PermissionGrantEntity&gt;（对齐 Extension.Permissions.Tests 既有桩）。</summary>
    private sealed class InMemoryEntityDac : IEntityDAC<PermissionGrantEntity>
    {
        private readonly List<PermissionGrantEntity> _items = new();
        private long _nextId = 1;

        public IReadOnlyList<PermissionGrantEntity> Items => _items;

        public IQueryable<PermissionGrantEntity> Query => _items.AsQueryable();

        public Task<PermissionGrantEntity?> FirstOrDefaultAsync(
            IQueryable<PermissionGrantEntity> query, CancellationToken ct = default)
            => Task.FromResult(query.FirstOrDefault());

        public Task<List<PermissionGrantEntity>> ToListAsync(
            IQueryable<PermissionGrantEntity> query, CancellationToken ct = default)
            => Task.FromResult(query.ToList());

        public Task<long> CountAsync(
            IQueryable<PermissionGrantEntity> query, CancellationToken ct = default)
            => Task.FromResult((long)query.Count());

        public Task<TResult?> FirstOrDefaultAsync<TResult>(
            IQueryable<PermissionGrantEntity> query,
            Expression<Func<PermissionGrantEntity, TResult>> selector,
            CancellationToken ct = default)
            => Task.FromResult(query.Select(selector).FirstOrDefault());

        public Task<List<TResult>> ToListAsync<TResult>(
            IQueryable<PermissionGrantEntity> query,
            Expression<Func<PermissionGrantEntity, TResult>> selector,
            CancellationToken ct = default)
            => Task.FromResult(query.Select(selector).ToList());

        public Task<PermissionGrantEntity> InsertAsync(
            PermissionGrantEntity entity, CancellationToken ct = default)
        {
            if (entity.Id == 0) entity.Id = _nextId++;
            _items.Add(entity);
            return Task.FromResult(entity);
        }

        public Task<List<PermissionGrantEntity>> InsertBatchAsync(
            IEnumerable<PermissionGrantEntity> entities, CancellationToken ct = default)
        {
            var list = entities.ToList();
            foreach (var entity in list) _items.Add(entity);
            return Task.FromResult(list);
        }

        public Task<bool> DeleteAsync(
            PermissionGrantEntity entity, CancellationToken ct = default)
            => Task.FromResult(_items.Remove(entity));

        public Task UpdateAsync(PermissionGrantEntity entity, CancellationToken ct = default)
        {
            var index = _items.FindIndex(x => x.Id == entity.Id);
            if (index >= 0) _items[index] = entity;
            return Task.CompletedTask;
        }

        public Task UpdateBatchAsync(
            IEnumerable<PermissionGrantEntity> entities, CancellationToken ct = default)
        {
            foreach (var entity in entities) UpdateAsync(entity, ct);
            return Task.CompletedTask;
        }

        public Task<int> UpdateColumnsBatchAsync<TColumns>(
            IEnumerable<PermissionGrantEntity> entities,
            Expression<Func<PermissionGrantEntity, TColumns>> columns,
            CancellationToken ct = default)
        {
            foreach (var entity in entities) UpdateAsync(entity, ct);
            return Task.FromResult(entities.Count());
        }

        public Task<int> UpdateWhereAsync<TColumns>(
            Expression<Func<PermissionGrantEntity, bool>> where,
            Expression<Func<PermissionGrantEntity, TColumns>> setColumns,
            CancellationToken ct = default)
            => throw new NotSupportedException("InMemoryEntityDac 不实现 UpdateWhereAsync（测试集未使用，ADR89 契约测试另设 fixture）。");
    }
}
