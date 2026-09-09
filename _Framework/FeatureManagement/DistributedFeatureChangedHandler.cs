using System;
using System.Threading.Tasks;
using TKW.Framework.Domain.Events;

namespace TKWF.Ext.FeatureManagement;

/// <summary>
/// 跨实例缓存失效 handler（v0.3.0）——收到 <see cref="FeatureValueChangedEvent"/>（远程实例发布 / 默认 LocalDistributedEventBus
/// 本地委派）→ <see cref="FeatureCacheVersionRegistry.BumpVersion"/>（版本递增 → 缓存 key 变化 → 立即新值）。
/// <para><b>必须 public sealed（C1 评审裁定）</b>：SG4 在消费方编译时经 <c>ReferencedAssemblySymbols</c> 扫描
/// <see cref="DomainEventHandlerAttribute"/> 生成 <c>typeof(Handler)</c> 引用并自动注册（Transient）——
/// internal 类型在消费方无 InternalsVisibleTo 时 CS0122 编译失败（测试经 IVT 无法捕获）。
/// 扩展 <see cref="FeatureManagementExtensionInitializer{TUserInfo}"/> <b>不手动注册</b>（SG4 消费方编译期自动接线）。</para>
/// <para>版本递增幂等：重复事件无功能性副作用（版本表效果幂等）；默认 LocalDistributedEventBus 下与写路径
/// <c>InvalidateCacheForName</c> 双重递增（+2）——功能无害（P1 评审：缓存仍失效，重复 bump 无功能性副作用）。</para>
/// </summary>
[DomainEventHandler]
public sealed class DistributedFeatureChangedHandler : IDistributedEventHandler<FeatureValueChangedEvent>
{
    private readonly FeatureCacheVersionRegistry _versionRegistry;

    public DistributedFeatureChangedHandler(FeatureCacheVersionRegistry versionRegistry)
        => _versionRegistry = versionRegistry ?? throw new ArgumentNullException(nameof(versionRegistry));

    /// <summary>收到 Feature 变更事件 → bump 版本表（本实例缓存立即失效——跨实例即时生效）。</summary>
    public Task HandleEventAsync(FeatureValueChangedEvent eventData)
    {
        _versionRegistry.BumpVersion(eventData.Name);
        return Task.CompletedTask;
    }
}
