using System;
using System.Linq;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKWF.Ext.Approval;

namespace TKWF.Ext.Approval.Tests;

/// <summary>
/// D11：Initializer DI 注册测试——验证 [TKWFExtension] 属性 + ConfigureServices 注册正确 + TryAddScoped 语义。
/// </summary>
public class ApprovalExtensionInitializerTests
{
    [Fact]
    public void ShouldHaveTKWFExtensionAttribute()
    {
        var attr = typeof(ApprovalExtensionInitializer<>)
            .GetCustomAttributes(typeof(TKWFExtensionAttribute), false)
            .FirstOrDefault() as TKWFExtensionAttribute;

        Assert.NotNull(attr);
        Assert.Equal("Approval", attr!.Name);
    }

    [Fact]
    public void ConfigureServices_ShouldRegisterServices()
    {
        var services = new ServiceCollection();

        // DataServices + 基础设施由消费者注册（Initializer 不注册）
        var fsql = ApprovalTestSupport.CreateInMemoryFreeSql();
        ApprovalTestSupport.SyncStructure(fsql);
        var stubUser = new StubDomainUser();
        services.AddSingleton(fsql);
        services.AddSingleton<TKW.Framework.Domain.Interfaces.IDomainUser>(stubUser);
        // v4.10.8 (ADR61) 迁移：DataService 经 DI 兜底工厂注册（镜像生产 AddConstructibleDataService，
        // 用户源 = DI IDomainUser，免域作用域）；IEntityDAC<T> 基础设施注册同生产 Host
        services.AddScoped<UnitOfWorkManager>();
        services.AddScoped<IEntityDAC<ApprovalFlowEntity>>(sp => new FreeSqlEntityDAC<ApprovalFlowEntity>(sp.GetRequiredService<UnitOfWorkManager>()));
        services.AddScoped<IEntityDAC<ApprovalInstanceEntity>>(sp => new FreeSqlEntityDAC<ApprovalInstanceEntity>(sp.GetRequiredService<UnitOfWorkManager>()));
        services.AddScoped<IEntityDAC<ApprovalTaskEntity>>(sp => new FreeSqlEntityDAC<ApprovalTaskEntity>(sp.GetRequiredService<UnitOfWorkManager>()));
        services.AddScoped<IEntityDAC<ApprovalAppendEntity>>(sp => new FreeSqlEntityDAC<ApprovalAppendEntity>(sp.GetRequiredService<UnitOfWorkManager>()));
        services.AddScoped<IEntityDAC<ApprovalCCEntity>>(sp => new FreeSqlEntityDAC<ApprovalCCEntity>(sp.GetRequiredService<UnitOfWorkManager>()));
        AddTestConstructibleDataService<ApprovalFlowEntityDataService>(services);
        AddTestConstructibleDataService<ApprovalInstanceEntityDataService>(services);
        AddTestConstructibleDataService<ApprovalTaskEntityDataService>(services);
        AddTestConstructibleDataService<ApprovalAppendEntityDataService>(services);
        AddTestConstructibleDataService<ApprovalCCEntityDataService>(services);
        services.AddSingleton<TKW.Framework.Domain.Transactions.ITransactionManager>(
            new NoopTransactionManager());
        services.AddSingleton<TKW.Framework.Domain.Events.ILocalEventBus>(
            new EventCollector());
        services.AddLogging();

        var initializer = new ApprovalExtensionInitializer<TestUserInfo>();
        initializer.ConfigureServices(services);

        var provider = services.BuildServiceProvider();

        // IApprovalService / ApprovalManager
        var approvalService = provider.GetService<IApprovalService>();
        Assert.NotNull(approvalService);
        Assert.IsType<ApprovalManager>(approvalService);

        // IApprovalQueryService / ApprovalQueryService
        var queryService = provider.GetService<IApprovalQueryService>();
        Assert.NotNull(queryService);
        Assert.IsType<ApprovalQueryService>(queryService);

        // IApprovalAssigneeResolver / DefaultApprovalAssigneeResolver
        var resolvers = provider.GetServices<IApprovalAssigneeResolver>().ToList();
        Assert.Contains(resolvers, r => r is DefaultApprovalAssigneeResolver);

        // v0.2.0：IApprovalTimeoutService / ApprovalTimeoutService（P10）
        var timeoutService = provider.GetService<IApprovalTimeoutService>();
        Assert.NotNull(timeoutService);
        Assert.IsType<ApprovalTimeoutService>(timeoutService);
    }

    /// <summary>v4.10.8 (ADR61) 迁移：测试版可构造 DataService 工厂——镜像生产 AddConstructibleDataService
    ///（ActivatorUtilities.CreateInstance + 域用户），用户源 = DI IDomainUser（免域作用域、xUnit 并行安全）。</summary>
    private static void AddTestConstructibleDataService<T>(IServiceCollection services)
        where T : class
    {
        services.AddScoped<T>(sp =>
        {
            var user = sp.GetRequiredService<TKW.Framework.Domain.Interfaces.IDomainUser>();
            return (T)ActivatorUtilities.CreateInstance(sp, typeof(T), user);
        });
    }

    [Fact]
    public void ConfigureServices_TryAddScoped_ShouldNotOverrideConsumerRegistration()
    {
        var services = new ServiceCollection();
        // 先注册消费者自定义实现
        services.TryAddScoped<IApprovalService, CustomApprovalService>();
        services.TryAddScoped<IApprovalQueryService, CustomApprovalQueryService>();

        var initializer = new ApprovalExtensionInitializer<TestUserInfo>();
        initializer.ConfigureServices(services);

        var provider = services.BuildServiceProvider();

        // TryAddScoped 不覆盖消费者注册
        Assert.IsType<CustomApprovalService>(provider.GetRequiredService<IApprovalService>());
        Assert.IsType<CustomApprovalQueryService>(provider.GetRequiredService<IApprovalQueryService>());
    }

    // 消费者自定义实现桩
    private sealed class CustomApprovalService : IApprovalService
    {
        public Task<long> CreateFlowAsync(string code, string name, System.Collections.Generic.IReadOnlyList<ApprovalStepDefinition> steps, string? description = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateFlowAsync(long flowId, string name, System.Collections.Generic.IReadOnlyList<ApprovalStepDefinition> steps, string? description = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task EnableFlowAsync(long flowId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DisableFlowAsync(long flowId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long> StartAsync(string businessType, string businessId, string flowCode, string submitter, string? businessDataJson = null, CancellationToken ct = default, string[]? ccUserIds = null) => throw new NotImplementedException();
        public Task SubmitAsync(long instanceId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task ApproveAsync(long taskId, string approverUserId, string? comment = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RejectAsync(long taskId, string approverUserId, string reason, CancellationToken ct = default) => throw new NotImplementedException();
        public Task TransferAsync(long taskId, string fromUserId, string toUserId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task WithdrawAsync(long instanceId, string userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DelegateTaskAsync(long taskId, string fromUserId, string toUserId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task ResolveTaskAsync(long taskId, string delegateUserId, string? comment = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AppendApproverAsync(long taskId, string operatedByUserId, string[] appenderUserIds, ApprovalAppendMode mode = ApprovalAppendMode.Participate, string? remark = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AddCCAsync(long instanceId, string[] userIds, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class CustomApprovalQueryService : IApprovalQueryService
    {
        public Task<ApprovalInstancePagedResult> GetInstancesAsync(ApprovalInstanceQueryInput input, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ApprovalTaskPagedResult> GetPendingTasksAsync(ApprovalTaskQueryInput input, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ApprovalInstanceDetailDto?> GetInstanceDetailAsync(long instanceId, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
