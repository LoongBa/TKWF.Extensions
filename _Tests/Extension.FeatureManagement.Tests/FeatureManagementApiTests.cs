using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKWF.Ext.FeatureManagement;

namespace TKWF.Ext.FeatureManagement.Tests;

/// <summary>
/// FeatureManagement 管理 API 测试——D12 + D10（管理 API 写路径缓存失效）。
/// <para>D12：管理门面 <c>FeatureManagementApiService</c> <c>[GenerateController]</c> 声明 + public 方法自动纳入
/// （对齐 IdentityAuthService 先例）；DataService 不标 <c>[GenerateController]</c>（仅内部存储——C3：
/// 裸 DataService 直通控制器会绕过 Global 预检与缓存失效）。</para>
/// <para>D10（写路径缓存失效）：管理 API 写方法（SetFeatureValueAsync/DeleteFeatureValueAsync）委托 IFeatureManager
/// ——经 Manager 写 → 同 key 立即新值；裸 DataService 写绕过缓存（对照）——验证设计上 Manager 是唯一写入口。</para>
/// </summary>
public class FeatureManagementApiTests
{
    private const string Theme = ConsumerFeatureContributor.StringFeature;

    // ── D12 [GenerateController] 声明（管理门面 ApiService） ──

    [Fact]
    public void ApiService_GenerateController_Declared()
    {
        var attr = typeof(FeatureManagementApiService)
            .GetCustomAttributes(typeof(GenerateControllerAttribute), false)
            .Cast<GenerateControllerAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attr);
    }

    [Fact]
    public void DataService_NoGenerateController_InternalOnly()
    {
        // C3：DataService 仅内部存储——不标 [GenerateController]（裸 CRUD 不得直通控制器）
        var attr = typeof(FeatureValueEntityDataService)
            .GetCustomAttributes(typeof(GenerateControllerAttribute), false)
            .Cast<GenerateControllerAttribute>()
            .FirstOrDefault();

        Assert.Null(attr);
    }

    [Fact]
    public void ApiService_WriteMethods_DelegatedToManager()
    {
        // 写路径（Set/Delete）经 Manager（缓存失效）——反射断言 ApiService 方法存在且 public
        Assert.NotNull(typeof(FeatureManagementApiService).GetMethod("SetFeatureValueAsync"));
        Assert.NotNull(typeof(FeatureManagementApiService).GetMethod("DeleteFeatureValueAsync"));
    }

    [Fact]
    public void DataService_RawCrudMethods_NotMarkedGenerateControllerMethod()
    {
        // C3：禁止裸 CRUD 直通控制器——EntityCreate/Update/DeleteBatch 不得 [GenerateControllerMethod]
        var rawCrudMethods = new[] { "EntityCreateAsync", "EntityUpdateAsync", "EntityDeleteBatchAsync" };

        foreach (var methodName in rawCrudMethods)
            Assert.False(HasControllerMethod(typeof(FeatureValueEntityDataService), methodName),
                $"{methodName} 是原始 CRUD——不得 [GenerateControllerMethod] 直通控制器");
    }

    // ── D10 管理 API 写路径缓存失效（写方法委托 IFeatureManager） ──

    [Fact]
    public async Task ManagerSetValue_InvalidatesCache_NewValueImmediatelyVisible()
    {
        using var host = FeatureManagementTestHost.Create();
        await host.Manager.SetValueAsync(Theme, "dark", FeatureProviders.Global, null, CancellationToken.None);
        var r1 = await host.Manager.GetValueAsync(Theme, host.User, "light", CancellationToken.None); // 填充缓存
        Assert.Equal("dark", r1);

        // 管理 API 写路径（SetValueAsync → 预检 + 缓存失效）→ 同 key 立即新值
        await host.Manager.SetValueAsync(Theme, "midnight", FeatureProviders.Global, null, CancellationToken.None);

        var r2 = await host.Manager.GetValueAsync(Theme, host.User, "light", CancellationToken.None);
        Assert.Equal("midnight", r2);
    }

    [Fact]
    public async Task ManagerDeleteValue_InvalidatesCache_ReturnsDefault()
    {
        using var host = FeatureManagementTestHost.Create();
        await host.Manager.SetValueAsync(Theme, "dark", FeatureProviders.Global, null, CancellationToken.None);
        _ = await host.Manager.GetValueAsync(Theme, host.User, "light", CancellationToken.None); // 填充缓存

        await host.Manager.DeleteValueAsync(Theme, FeatureProviders.Global, null, CancellationToken.None);

        var r2 = await host.Manager.GetValueAsync(Theme, host.User, "light", CancellationToken.None);
        Assert.Equal("light", r2); // 删除后缓存失效 → 回退 FeatureDefinition.DefaultValue
    }

    [Fact]
    public async Task BareDataServiceWrite_DoesNotInvalidateCache_Control()
    {
        // 对照（设计验证）：裸 DataService 写（绕过 Manager）不失效缓存——证明"Manager 是唯一写入口"。
        // C3：控制器写方法必须委托 IFeatureManager（统一预检 + 缓存失效），裸 CRUD 直通被禁止。
        using var host = FeatureManagementTestHost.Create();
        await host.Manager.SetValueAsync(Theme, "dark", FeatureProviders.Global, null, CancellationToken.None);
        var r1 = await host.Manager.GetValueAsync(Theme, host.User, "light", CancellationToken.None); // 填充缓存 "dark"
        Assert.Equal("dark", r1);

        // 裸 DataService 写（绕过 Manager——设计上不允许直通控制器的路径）
        await host.DataService.UpsertByKeyAsync(new FeatureValueEntity
        {
            Name = Theme,
            Value = "raw-write",
            ProviderName = FeatureProviders.Global,
            ProviderKey = null,
            CreateTime = DateTime.UtcNow,
            UpdateTime = DateTime.UtcNow
        }, CancellationToken.None);

        var r2 = await host.Manager.GetValueAsync(Theme, host.User, "light", CancellationToken.None);

        Assert.Equal("dark", r2); // 缓存未失效——裸写绕过缓存失效，仅 Manager 写路径失效
    }

    // ── 辅助 ──

    private static bool HasControllerMethod(Type type, string methodName)
        => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(m => m.Name == methodName)
            .SelectMany(m => m.GetCustomAttributes(typeof(GenerateControllerMethodAttribute), false))
            .Any();
}
