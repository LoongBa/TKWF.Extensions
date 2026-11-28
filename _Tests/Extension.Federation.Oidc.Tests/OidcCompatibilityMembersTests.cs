using System;
using System.Reflection;
using Xunit;

namespace TKWF.Federation.Oidc.Tests;

/// <summary>
/// OIDC 兼容占位成员回归网（代码审核 2026-11-23 P1 项 1）：
/// 976c5df 删除了 <c>OidcPlatformConfig.FetchUserInfo</c> 与 <c>OidcChannelConfigKeys.FetchUserInfo</c>
/// ——属 source-breaking（既有消费者编译失败）。本测试锁定二者以 [Obsolete] 形式恢复存在，
/// 且仅为 no-op 占位（不复活任何死消费逻辑）。
/// <para>用反射断言而非直接引用，避免 [Obsolete] 自身在本测试产生 CS0618 警告。</para>
/// </summary>
public class OidcCompatibilityMembersTests
{
    [Fact]
    public void OidcPlatformConfig_FetchUserInfo_Exists_AsObsoleteNoOpDefaultTrue()
    {
        var prop = typeof(OidcPlatformConfig).GetProperty("FetchUserInfo");
        Assert.NotNull(prop);
        Assert.Equal(typeof(bool), prop!.PropertyType);
        Assert.True(prop.CanRead);
        Assert.True(prop.CanWrite);
        Assert.NotNull(prop.GetCustomAttribute<ObsoleteAttribute>());

        // 默认值 true；读写仅为兼容占位（no-op，不参与任何认证逻辑）
        var instance = new OidcPlatformConfig();
        Assert.True((bool)prop.GetValue(instance)!);
        prop.SetValue(instance, false);
        Assert.False((bool)prop.GetValue(instance)!);
    }

    [Fact]
    public void OidcChannelConfigKeys_FetchUserInfo_Exists_AsObsoleteConst()
    {
        var field = typeof(OidcChannelConfigKeys).GetField(
            "FetchUserInfo", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(field);
        Assert.True(field!.IsLiteral);                 // const（编译期常量）
        Assert.Equal(typeof(string), field.FieldType);
        Assert.Equal("FetchUserInfo", field.GetRawConstantValue());
        Assert.NotNull(field.GetCustomAttribute<ObsoleteAttribute>());
    }
}
