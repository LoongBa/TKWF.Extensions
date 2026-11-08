using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Permissions.Abstractions;

namespace TKWF.Ext.WeChatChannels.Tests;

/// <summary>
/// 通道底座契约形态测试（2026-11-05 拆分下沉——契约在 WeChatChannels.Abstractions）。
/// <para>覆盖：契约接口身份（IDomainService——User.Use 帧内解析准入）、权限键（WeChatChannels.Query/Manage——
/// 与运营扩展 WeChatOperations.* 独立授权，D6）、[ApiExpose] 相对路由常量（D5——REST 前缀归
/// ExtensionModule 单一真源，RestRoute 应为相对路径）、新增 GetByChannelIdAsync（O2——按 channelId 查询入口）。</para>
/// <para>通道 CRUD 行为/凭证两源并存/alias 守卫/加密往返等生产路径断言随运营宿主（双扩展装配——
/// WeChatChannels + WeChatOperations，镜像 Merchant 消费方真实形态）运行于 Extension.WeChatOperations.Tests。</para>
/// </summary>
public class IChannelConfigServiceContractTests
{
    private static readonly Type Contract = typeof(IChannelConfigService);

    /// <summary>契约身份：继承 IDomainService（消费方 User.Use&lt;T&gt;() 编译期约束 + DI005 契约缺口门控）。</summary>
    [Fact]
    public void Interface_Inherits_IDomainService()
        => Assert.True(typeof(IDomainService).IsAssignableFrom(Contract));

    /// <summary>契约在 Abstractions 包（L2 扩展间只引契约；namespace TKWF.Ext.WeChatChannels——O5 跨程序集同）。</summary>
    [Fact]
    public void Interface_Lives_In_Abstractions_Assembly()
    {
        Assert.NotNull(Contract.Assembly.GetName().Name);
        Assert.Contains("WeChatChannels.Abstractions", Contract.Assembly.GetName().Name!);
    }

    /// <summary>权限键：读操作 WeChatChannels.Query / 写操作 WeChatChannels.Manage（D6 独立授权——不复用运营键）。</summary>
    [Fact]
    public void PermissionKeys_ChannelScoped()
    {
        var read = Contract.GetMethod(nameof(IChannelConfigService.ListChannelsAsync))!;
        var write = Contract.GetMethod(nameof(IChannelConfigService.SaveChannelAsync))!;

        Assert.NotNull(read.GetCustomAttribute<RequirePermissionAttribute>());
        Assert.Equal("WeChatChannels.Query", read.GetCustomAttribute<RequirePermissionAttribute>()!.Permissions[0]);
        Assert.NotNull(write.GetCustomAttribute<RequirePermissionAttribute>());
        Assert.Equal("WeChatChannels.Manage", write.GetCustomAttribute<RequirePermissionAttribute>()!.Permissions[0]);
    }

    /// <summary>REST 路由：非 Web 扩展形态（对齐 Identity `/identity/user-roles` 先例）——RestRoute 为**全段**
    /// （含模块段，不含 RestBasePath /api）；URL = RestBasePath + RestRoute = /api/wechat-channels/channels（D5）。</summary>
    [Fact]
    public void RestRoutes_Are_FullSegment()
    {
        var list = Contract.GetMethod(nameof(IChannelConfigService.ListChannelsAsync))!
            .GetCustomAttribute<ApiExposeAttribute>();
        Assert.NotNull(list);
        Assert.Equal("/wechat-channels/channels", list!.RestRoute);
        Assert.Equal("GET", list.RestMethod);

        var save = Contract.GetMethod(nameof(IChannelConfigService.SaveChannelAsync))!
            .GetCustomAttribute<ApiExposeAttribute>();
        Assert.NotNull(save);
        Assert.Equal("/wechat-channels/channels", save!.RestRoute);
        Assert.Equal("POST", save.RestMethod);
    }

    /// <summary>O2 新增：GetByChannelIdAsync（MenuService 门面化 channelId 查询入口）——契约存在 + ExposeRest=false
    /// （内部能力不扩暴露面）+ 读权限键。</summary>
    [Fact]
    public void GetByChannelIdAsync_Contract_Declared()
    {
        var m = Contract.GetMethod(nameof(IChannelConfigService.GetByChannelIdAsync));
        Assert.NotNull(m);
        Assert.Equal(typeof(Task<WeChatChannelConfigDto?>), m!.ReturnType);
        Assert.Equal(typeof(string), m.GetParameters()[0].ParameterType);
        var expose = m.GetCustomAttribute<ApiExposeAttribute>();
        Assert.NotNull(expose);
        Assert.False(expose!.ExposeRest);
        Assert.Equal("WeChatChannels.Query", m.GetCustomAttribute<RequirePermissionAttribute>()!.Permissions[0]);
    }

    /// <summary>wire DTO：SaveChannelRequest 承载凭证明文可选（null = 保持既有密文）；同契约包（消费方零传输层漂移）。</summary>
    [Fact]
    public void WireDtos_CoLocated_With_Contract()
    {
        Assert.Equal(Contract.Assembly, typeof(SaveChannelRequest).Assembly);
        Assert.Equal(Contract.Assembly, typeof(WeChatChannelConfigDto).Assembly);
        Assert.NotNull(typeof(SaveChannelRequest).GetProperty(nameof(SaveChannelRequest.AppSecret)));
        Assert.NotNull(typeof(SaveChannelRequest).GetProperty(nameof(SaveChannelRequest.Token)));
        Assert.NotNull(typeof(SaveChannelRequest).GetProperty(nameof(SaveChannelRequest.EncodingAesKey)));
    }

    /// <summary>投影 DTO 不含凭证字段（Oracle P1 安全——密文/明文均不回显）。</summary>
    [Fact]
    public void ProjectionDto_No_Credential_Fields()
    {
        Assert.Null(typeof(WeChatChannelConfigDto).GetProperty("AppSecretCipher"));
        Assert.Null(typeof(WeChatChannelConfigDto).GetProperty("TokenCipher"));
        Assert.Null(typeof(WeChatChannelConfigDto).GetProperty("EncodingAESKeyCipher"));
        Assert.Null(typeof(WeChatChannelConfigDto).GetProperty("AppSecret"));
        Assert.NotNull(typeof(WeChatChannelConfigDto).GetProperty("HasCredentials"));
    }

    /// <summary>模块前缀声明（[assembly: ExtensionModule("WeChatChannels","wechat-channels")]——D5 新段，供 WebExtension 组路径；
/// 声明于主包 AssemblyInfo——用实现类型程序集反射读取，校验 Key/DefaultPrefix。</summary>
    [Fact]
    public void ExtensionModule_Declared_With_NewSegment()
    {
        var attr = typeof(ChannelConfigService).Assembly.GetCustomAttributes()
            .FirstOrDefault(a => a.GetType().Name == "ExtensionModuleAttribute");
        Assert.NotNull(attr);

        var type = attr!.GetType();
        var key = type.GetProperty("Key")?.GetValue(attr) as string;
        var prefix = type.GetProperty("DefaultPrefix")?.GetValue(attr) as string;
        Assert.Equal("WeChatChannels", key);
        Assert.Equal("wechat-channels", prefix);
    }
}