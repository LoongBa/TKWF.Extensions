namespace TKWF.Ext.WeChatOperations.Tests;

/// <summary>
/// 网关运行时开关静态状态隔离集合（2026-11-20，L2 偶发假红根治）。
/// <para>背景：<c>MessageGatewayService._gatewayEnabledOverride</c> 是**进程级静态字段**（管理面翻转 →
/// 路由面即时生效的生产语义），测试经反射 Set/Reset。xunit v3 默认按集合并行——网关开用例把 override
/// 置 true 的窗口内，<c>DefaultOff_Handler_ZeroRegression</c> 若并发执行会走到 RouteAsync 真实路径，
/// 产生幂等行/转发日志 → "零回归"断言偶发失败（本地 L2 两次命中，单项目复跑通过）。</para>
/// <para>处置：把触碰该静态或经 handler/路由链路的测试类归入本集合，并
/// <c>DisableParallelization = true</c> 使其与其它集合串行——不改生产代码（静态覆盖是生产语义），
/// 仅收敛测试并行面。</para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GatewayStaticStateCollection
{
    /// <summary>集合名（各测试类 <c>[Collection(GatewayStaticStateCollection.Name)]</c> 引用）。</summary>
    public const string Name = "WeChatOperationsGatewayStaticState";
}
