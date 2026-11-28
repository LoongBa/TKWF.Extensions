using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using TKWF.Federation.QQ;
using TKWF.Ext.Federation;
using Xunit;

// 故意放在与 TKWF.Federation.QQ 无嵌套关系的命名空间——模拟外部消费方：
// 仅凭 using TKWF.Federation.QQ; + using Microsoft.Extensions.DependencyInjection;
// 即可发现 AddQqFederationChannels；若命名空间仍错置于 Microsoft.Extensions.DependencyInjection
// 或两处重复定义（歧义），本文件将编译失败。
namespace QqConsumerCompat.Tests;

/// <summary>
/// QQ 注册扩展命名空间回归网（代码审核 2026-11-23 P1 项 2）：
/// 976c5df 将 QqFederationServiceCollectionExtensions 从 TKWF.Federation.QQ 迁至
/// Microsoft.Extensions.DependencyInjection，偏离 DingTalk/WeCom/WeChat 平台库定式。
/// 本测试以消费方视角锁定扩展方法归属本库命名空间且可被正常解析。
/// </summary>
public class QqNamespaceCompatibilityTests
{
    [Fact]
    public void AddQqFederationChannels_DiscoverableFromOwningNamespace_AndRegistersServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var returned = services.AddQqFederationChannels(o =>
            o.Channels = [new QqChannelConfig
            {
                ChannelId = "qq-main",
                AppId = "test-appid",
                AppSecret = "test-secret"
            }]);

        Assert.Same(services, returned);
        Assert.Contains(services, d => d.ServiceType == typeof(QqApiClient));
        Assert.Contains(services, d =>
            d.ServiceType == typeof(IChannelSource) && d.ImplementationType == typeof(QqChannelSource));
    }
}
