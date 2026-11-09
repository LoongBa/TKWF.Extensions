using System.Reflection;
using TKW.Framework.CodeGeneration;
using TKWF.Ext.WeChatOperations;

namespace TKWF.Ext.WeChatOperations.Consumer.Tests;

/// <summary>
/// V0.10.0（WARN009 契约形状纠正）消费方集成验证——P11 载体断言（C3 硬条件）。
/// <para>验证目标：<c>RouteAsync</c> 撤出 <see cref="IMessageGatewayService"/> 后——
/// ① 管理契约 <c>IMessageGatewayService</c> 仍在 SG1b catalog（10 管理端点/DTO 面不消失）；
/// ② 进程内路由接口 <c>IWeChatMessageRouter</c> <b>逃逸</b> catalog（非 [GenerateController] 契约面，
/// SG2a 不再枚举其签名 → 运行库/平台库类型退出 DTO 图 → WARN009 根除）；
/// ③ 契约签名反射断言（机制级防回归门——SG2a 收集输入 = 契约签名，签名无运行库类型 = 不可能再触发
/// WARN009；仓库内无 Web SDK 观察点（SG2a 需 DMP WebApi 形态才运行），变异实弹的 "WARN009 复燃"
/// 现象级观测委托 DMP 复验——其 WebApi.csproj 即现成观察点）。</para>
/// </summary>
[Collection("SharedProjectMetaContextInstance")]
public class ConsumerContractShapeTests
{
    /// <summary>
    /// SG1b 编译期发现：管理契约接口名在消费方记录——<c>IMessageGatewayService</c> 必须仍在 catalog。
    /// </summary>
    [Fact]
    public void Sg1b_InterfaceNames_IncludesMessageGatewayService()
    {
        const string expectedIface = "TKWF.Ext.WeChatOperations.IMessageGatewayService";

        Assert.Contains(expectedIface, TKW.Framework.SourceGen.GeneratedControllerRegistrations.InterfaceNames);
        Assert.DoesNotContain("TKWF.Ext.WeChatOperations.Controllers.", TKW.Framework.SourceGen.GeneratedControllerRegistrations.InterfaceNames);
    }

    /// <summary>
    /// 进程内路由接口必须【不在】catalog——IWeChatMessageRouter 非 [GenerateController] 契约面
    /// （声明顺序契约锚定的消费方视角断言：catalog 只记录 IMessageGatewayService）。
    /// </summary>
    [Fact]
    public void Sg1b_InterfaceNames_ExcludesWeChatMessageRouter()
    {
        const string routerIface = "TKWF.Ext.WeChatOperations.IWeChatMessageRouter";

        Assert.DoesNotContain(routerIface, TKW.Framework.SourceGen.GeneratedControllerRegistrations.InterfaceNames);
    }

    /// <summary>
    /// 契约签名反射断言（机制级防回归门）：SG2a 收集输入 = 契约接口签名——签名无运行库类型
    /// （ForwardAction/ForwardDegradeStage/MessageRouteRuleDescriptor）即从机制上不可能再触发 WARN009
    /// （DLL 模式缺 MessageGateway.dll 引用集下符号解析需求不存在）。管理契约 = 9 管理方法，无 RouteAsync。
    /// </summary>
    [Fact]
    public void Contract_ManagementInterface_HasNoRouteAsync()
    {
        var methods = typeof(IMessageGatewayService).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name).ToArray();

        Assert.DoesNotContain("RouteAsync", methods);
        Assert.Contains("ListRouteRulesAsync", methods);
        Assert.Contains("ListEndpointsAsync", methods);
        Assert.Contains("ListForwardLogsAsync", methods);
        Assert.Equal(9, methods.Length);
    }

    /// <summary>进程内路由接口契约存在且承载 RouteAsync（handler 消费面不换方法只换接口，返回编排结果类型）。</summary>
    [Fact]
    public void Contract_RouterInterface_CarriesRouteAsync()
    {
        var router = typeof(IWeChatMessageRouter);
        var route = router.GetMethod("RouteAsync", BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(route);
        Assert.Equal(
            "TKWF.Ext.WeChatOperations.GatewayRouteResult",
            route!.ReturnType.IsGenericType ? route.ReturnType.GetGenericArguments()[0].FullName : route.ReturnType.FullName);
        Assert.Equal("TKWF.Federation.WeChat.WechatEventContext", route.GetParameters()[0].ParameterType.FullName);
    }
}