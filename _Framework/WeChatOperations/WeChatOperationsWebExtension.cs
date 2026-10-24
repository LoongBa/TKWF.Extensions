using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Web.Extensions;

namespace TKWF.Ext.WeChatOperations;

/// <summary>
/// 微信公众号运营 Web 装配扩展（v0.6.0 重建——修复 B1-c 端点契约化后的悬空端点）。
/// <para><b>背景</b>：V0.4.0 曾删除本扩展（当时依赖消费方 SG1b 生成控制器路径）；框架 B4 门控精化
/// （v4.10.84 <c>referencesAspNetCore</c>）后，WeChatOperations 因含 <c>Microsoft.AspNetCore.App</c>
/// FrameworkReference 落入"发射 <c>[assembly: ExtensionEndpointRegistrations]</c> 注册表"分支，
/// 消费方 SG2 见该标记结构性让路 → 25 个 <c>/api/wechat-ops/*</c> 端点<b>两头落空（运行时 404）</b>。</para>
/// <para><b>修复</b>：本扩展 <see cref="DeclareEndpoints"/> 对 8 个 <c>[GenerateController]</c> 门面调用
/// <c>registry.MapGenerated&lt;TFacade&gt;()</c> 挂载其全部 <c>[ApiExpose]</c> 端点（组路径 =
/// <c>TKWF:Api:RestBasePath</c> 缺省 /api + <c>[assembly: ExtensionModule("WeChatOperations","wechat-ops")]</c>
/// 前缀 = /api/wechat-ops；25 处 <c>[ApiExpose]</c> 已去 /wechat-ops 段——前缀归 ExtensionModule 单一真源）。</para>
/// <para>消费方装配：<c>UseWebExtensions&lt;TUserInfo&gt;(e =&gt; e.Add&lt;WeChatOperationsWebExtension&gt;())</c>。
/// 端点均非匿名——HttpAuthentication 401 fail-closed + 已认证帧 user.Use 门面（<c>[RequirePermission]</c> 门控）。</para>
/// </summary>
public sealed class WeChatOperationsWebExtension : IWebExtension
{
    /// <summary>端点声明（B1-c 破坏性替换 ConfigureEndpoints）——挂载 8 个 [GenerateController] 门面全部
    /// [ApiExpose] 端点（MapGenerated<T> 经 SG2 生成的 Map{Facade}Endpoints 助手挂载）。
    /// <para>宿主注册（ConfigureServices）走 IWebExtension 默认空实现——本扩展无 Options 绑定
    /// （配置节 TKWF:WeChatOperations 由 <see cref="WeChatOperationsExtensionInitializer{TUserInfo}"/>
    /// 经 SG1 [Options] 绑定；门面已由 Initializer AddConstructibleService 注册）。</para></summary>
    public void DeclareEndpoints(IExtensionEndpointRegistry registry)
    {
        registry.MapGenerated<IChannelConfigService>();
        registry.MapGenerated<IMenuService>();
        registry.MapGenerated<IMaterialService>();
        registry.MapGenerated<IReplyRuleService>();
        registry.MapGenerated<ISubscriptionStatService>();
        registry.MapGenerated<ILogQueryService>();
        registry.MapGenerated<IMessageGatewayService>();
        registry.MapGenerated<IWeChatOperationsStatusService>();
    }
}
