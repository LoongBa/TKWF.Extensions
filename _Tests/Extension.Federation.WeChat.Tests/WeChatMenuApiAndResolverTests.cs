using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace TKWF.Federation.WeChat.Tests;

/// <summary>
/// 平台库 V0.4.1 菜单出站 API + 凭证解析可插拔契约测试（方案 docs/WeChatOperations/微信公众号运营扩展-开发方案.md §3.2）。
/// <para>覆盖：① 菜单 3 API 出站 URL/方法/JSON 透传/错误码断言（Stub handler 路由——见
/// <see cref="WeChatTestHost.CreateStubHandler"/> 菜单路径）；② <see cref="OptionsWeChatCredentialResolver"/>
/// 正负（Channels 有/无匹配 AppId）；③ 自定义 resolver 注入生效（凭证来源可插拔——DB 实现由扩展注册的契约面）。</para>
/// </summary>
public class WeChatMenuApiAndResolverTests : WeChatChannelTestHost
{
    private const string MenuJson =
        """{"button":[{"type":"click","name":"测试菜单","key":"test_key"}]}""";

    /// <summary>创建菜单：POST cgi-bin/menu/create + body=menuJson 透传 + errcode==0 成功返回 errmsg。</summary>
    [Fact]
    public async Task CreateMenu_Success_ReturnsErrmsg()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var client = new WeChatApiClient(
            new System.Net.Http.HttpClient(Handler),
            new OptionsWeChatCredentialResolver(Options.Create(new WeChatOptions())),
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<WeChatApiClient>());

        var result = await client.CreateMenuAsync("stub-token", MenuJson, CancellationToken.None);

        Assert.Equal("ok", result);
    }

    /// <summary>创建菜单：非法 JSON → ArgumentException（结构自由形态但必须合法 JSON 对象 + button 非空）。</summary>
    [Fact]
    public async Task CreateMenu_InvalidJson_Throws()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var client = new WeChatApiClient(
            new System.Net.Http.HttpClient(Handler),
            new OptionsWeChatCredentialResolver(Options.Create(new WeChatOptions())),
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<WeChatApiClient>());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.CreateMenuAsync("stub-token", "{json 损坏", CancellationToken.None));
    }

    /// <summary>创建菜单：微信返回 errcode!=0 → InvalidOperationException 含错误码。</summary>
    [Fact]
    public async Task CreateMenu_WeChatError_Throws()
    {
        using var _ = this;
        // 覆盖 stub——menu/create 返回错误码（40016 不合法的按钮个数）
        Handler = WeChatTestHost.CreateStubHandler(menuJson: """{"errcode":40016,"errmsg":"invalid button size"}""");
        var (_, user) = BindScope();
        var client = new WeChatApiClient(
            new System.Net.Http.HttpClient(Handler),
            new OptionsWeChatCredentialResolver(Options.Create(new WeChatOptions())),
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<WeChatApiClient>());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.CreateMenuAsync("stub-token", MenuJson, CancellationToken.None));

        Assert.Contains("40016", ex.Message);
    }

    /// <summary>查询当前菜单：GET cgi-bin/get_current_selfmenu_info → 结构化 poco（is_menu_open/selfmenu_info/button/sub_button）。</summary>
    [Fact]
    public async Task GetCurrentSelfMenuInfo_ReturnsStructuredPoco()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var client = new WeChatApiClient(
            new System.Net.Http.HttpClient(Handler),
            new OptionsWeChatCredentialResolver(Options.Create(new WeChatOptions())),
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<WeChatApiClient>());

        var result = await client.GetCurrentSelfMenuInfoAsync("stub-token", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, result!.IsMenuOpen);
        Assert.NotNull(result.SelfMenuInfo);
        Assert.Equal(2, result.SelfMenuInfo!.Button.Length);
        Assert.Equal("click", result.SelfMenuInfo.Button[0].Type);
        Assert.Equal("测试菜单", result.SelfMenuInfo.Button[0].Name);
        Assert.Equal("test_key", result.SelfMenuInfo.Button[0].Key);
        Assert.NotNull(result.SelfMenuInfo.Button[1].SubButton);
        Assert.Single(result.SelfMenuInfo.Button[1].SubButton!);
    }

    /// <summary>查询当前菜单：未配置菜单（is_menu_open=0 / 字段缺失）→ null（调用方按"无菜单"处理）。</summary>
    [Fact]
    public async Task GetCurrentSelfMenuInfo_NoMenu_ReturnsNull()
    {
        using var _ = this;
        Handler = WeChatTestHost.CreateStubHandler(menuJson: """{"is_menu_open":0}""");
        var (_, user) = BindScope();
        var client = new WeChatApiClient(
            new System.Net.Http.HttpClient(Handler),
            new OptionsWeChatCredentialResolver(Options.Create(new WeChatOptions())),
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<WeChatApiClient>());

        var result = await client.GetCurrentSelfMenuInfoAsync("stub-token", CancellationToken.None);

        Assert.Null(result);
    }

    /// <summary>v0.17.1 sub_button 官方对象形状（{"list":[...]}——get_current_selfmenu_info）→ 两层按钮正确反序列化。</summary>
    [Fact]
    public async Task GetCurrentSelfMenuInfo_SubButtonOfficialShape()
    {
        using var _ = this;
        Handler = WeChatTestHost.CreateStubHandler(menuJson:
            """{"is_menu_open":1,"selfmenu_info":{"button":[{"name":"菜单","sub_button":{"list":[{"type":"view","name":"搜索","url":"http://soso.com/"},{"type":"view","name":"视频","url":"http://v.qq.com/"}]}}]}}""");
        var client = new WeChatApiClient(
            new System.Net.Http.HttpClient(Handler),
            new OptionsWeChatCredentialResolver(Options.Create(new WeChatOptions())),
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<WeChatApiClient>());

        var result = await client.GetCurrentSelfMenuInfoAsync("stub-token", CancellationToken.None);

        var sub = Assert.IsType<WeChatMenuButton[]>(result!.SelfMenuInfo!.Button[0].SubButton);
        Assert.Equal(2, sub.Length);
        Assert.Equal("view", sub[0].Type);
        Assert.Equal("搜索", sub[0].Name);
        Assert.Equal("http://soso.com/", sub[0].Url);
        Assert.Equal("http://v.qq.com/", sub[1].Url);
    }

    /// <summary>v0.17.1 sub_button 边界归一：缺失 / null / 空对象 {} / list:null（含尾随属性）→ 均 null 不抛异常。</summary>
    [Theory]
    [InlineData("""{"is_menu_open":1,"selfmenu_info":{"button":[{"name":"菜单"}]}}""")]
    [InlineData("""{"is_menu_open":1,"selfmenu_info":{"button":[{"name":"菜单","sub_button":null}]}}""")]
    [InlineData("""{"is_menu_open":1,"selfmenu_info":{"button":[{"name":"菜单","sub_button":{}}]}}""")]
    [InlineData("""{"is_menu_open":1,"selfmenu_info":{"button":[{"name":"菜单","sub_button":{"list":null}}]}}""")]
    [InlineData("""{"is_menu_open":1,"selfmenu_info":{"button":[{"name":"菜单","sub_button":{"count":0,"list":null}}]}}""")]
    public async Task GetCurrentSelfMenuInfo_SubButtonMissingNullOrEmpty(string menuJson)
    {
        using var _ = this;
        Handler = WeChatTestHost.CreateStubHandler(menuJson: menuJson);
        var client = new WeChatApiClient(
            new System.Net.Http.HttpClient(Handler),
            new OptionsWeChatCredentialResolver(Options.Create(new WeChatOptions())),
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<WeChatApiClient>());

        var result = await client.GetCurrentSelfMenuInfoAsync("stub-token", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(result!.SelfMenuInfo!.Button[0].SubButton);
    }

    /// <summary>v0.17.1 sub_button 数组形状（menu/get 接口形状——converter 前向兼容）→ 正确反序列化。</summary>
    [Fact]
    public async Task GetCurrentSelfMenuInfo_SubButtonArrayShape()
    {
        using var _ = this;
        Handler = WeChatTestHost.CreateStubHandler(menuJson:
            """{"is_menu_open":1,"selfmenu_info":{"button":[{"name":"菜单","sub_button":[{"type":"view","name":"搜索","url":"http://soso.com/"},{"type":"view","name":"视频","url":"http://v.qq.com/"}]}]}}""");
        var client = new WeChatApiClient(
            new System.Net.Http.HttpClient(Handler),
            new OptionsWeChatCredentialResolver(Options.Create(new WeChatOptions())),
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<WeChatApiClient>());

        var result = await client.GetCurrentSelfMenuInfoAsync("stub-token", CancellationToken.None);

        var sub = Assert.IsType<WeChatMenuButton[]>(result!.SelfMenuInfo!.Button[0].SubButton);
        Assert.Equal(2, sub.Length);
        Assert.Equal("搜索", sub[0].Name);
    }

    /// <summary>v0.17.1 sub_button 递归三层（对象形状内嵌对象形状）→ 全深度正确（属性级 converter 自动递归）。</summary>
    [Fact]
    public async Task GetCurrentSelfMenuInfo_SubButtonRecursive()
    {
        using var _ = this;
        Handler = WeChatTestHost.CreateStubHandler(menuJson:
            """{"is_menu_open":1,"selfmenu_info":{"button":[{"name":"一级","sub_button":{"list":[{"name":"二级","sub_button":{"list":[{"type":"click","name":"三级","key":"L3_KEY"}]}}]}}]}}""");
        var client = new WeChatApiClient(
            new System.Net.Http.HttpClient(Handler),
            new OptionsWeChatCredentialResolver(Options.Create(new WeChatOptions())),
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<WeChatApiClient>());

        var result = await client.GetCurrentSelfMenuInfoAsync("stub-token", CancellationToken.None);

        var level1 = Assert.IsType<WeChatMenuButton[]>(result!.SelfMenuInfo!.Button[0].SubButton);
        var level2 = Assert.IsType<WeChatMenuButton[]>(level1[0].SubButton);
        Assert.Single(level2);
        Assert.Equal("二级", level1[0].Name);
        Assert.Equal("L3_KEY", level2[0].Key);
        Assert.Equal("click", level2[0].Type);
    }

    /// <summary>v0.17.1 sub_button 非预期形状（字符串）→ JsonException（数据错误不吞——非"空"语义）。</summary>
    [Fact]
    public async Task GetCurrentSelfMenuInfo_SubButtonInvalidShape_Throws()
    {
        using var _ = this;
        Handler = WeChatTestHost.CreateStubHandler(menuJson:
            """{"is_menu_open":1,"selfmenu_info":{"button":[{"name":"菜单","sub_button":"oops"}]}}""");
        var client = new WeChatApiClient(
            new System.Net.Http.HttpClient(Handler),
            new OptionsWeChatCredentialResolver(Options.Create(new WeChatOptions())),
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<WeChatApiClient>());

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() =>
            client.GetCurrentSelfMenuInfoAsync("stub-token", CancellationToken.None));
    }

    /// <summary>删除菜单：GET cgi-bin/menu/delete 成功无返回（errcode==0）。</summary>
    [Fact]
    public async Task DeleteMenu_Success_NoThrow()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var client = new WeChatApiClient(
            new System.Net.Http.HttpClient(Handler),
            new OptionsWeChatCredentialResolver(Options.Create(new WeChatOptions())),
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<WeChatApiClient>());

        await client.DeleteMenuAsync("stub-token", CancellationToken.None);
    }

    /// <summary>凭证解析缺省实现正：Channels 有匹配 AppId → 返回该 channel 配置（凭证自持 Oracle P2-4）。</summary>
    [Fact]
    public async Task OptionsResolver_Match_ReturnsChannelConfig()
    {
        var options = Options.Create(new WeChatOptions
        {
            Channels =
            {
                WeChatTestHost.CreateChannelConfig(channelId: "wx-main", appId: "wx0000000000000000"),
                WeChatTestHost.CreateChannelConfig(channelId: "wx-sub", appId: "wx1111111111111111"),
            },
        });
        var resolver = new OptionsWeChatCredentialResolver(options);

        var result = await resolver.ResolveAsync("wx1111111111111111", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("wx-sub", result!.ChannelId);
    }

    /// <summary>凭证解析缺省实现负：Channels 无匹配 AppId → null（调用方抛"凭证未配置"）。</summary>
    [Fact]
    public async Task OptionsResolver_NoMatch_ReturnsNull()
    {
        var options = Options.Create(new WeChatOptions
        {
            Channels =
            {
                WeChatTestHost.CreateChannelConfig(channelId: "wx-main", appId: "wx0000000000000000"),
            },
        });
        var resolver = new OptionsWeChatCredentialResolver(options);

        var result = await resolver.ResolveAsync("wx9999999999999999", CancellationToken.None);

        Assert.Null(result);
    }

    /// <summary>凭证解析可插拔：自定义 resolver 注入生效——WeChatApiClient 经自定义 resolver 取凭证（DB/KMS 契约面）。</summary>
    [Fact]
    public async Task ApiClient_CustomResolver_UsedForCredential()
    {
        // 自定义 resolver：AppId "wx-custom" → 假凭证（模拟 DB 解密返回）
        var custom = new FakeResolver("wx-custom", "custom-secret");
        var client = new WeChatApiClient(
            new System.Net.Http.HttpClient(Handler),
            custom,
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<WeChatApiClient>());

        var token = await client.GetAccessTokenAsync("wx-custom", "mp", CancellationToken.None);

        Assert.Equal("stub-token", token);   // Stub handler 返回固定 token——凭证解析成功走到出站
    }

    /// <summary>凭证解析：resolver 未命中 → InvalidOperationException 含 appId（现状 ResolveCredential 语义迁移）。</summary>
    [Fact]
    public async Task ApiClient_ResolverMiss_Throws()
    {
        var client = new WeChatApiClient(
            new System.Net.Http.HttpClient(Handler),
            new OptionsWeChatCredentialResolver(Options.Create(new WeChatOptions())),
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<WeChatApiClient>());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetAccessTokenAsync("wx-none", "mp", CancellationToken.None));

        Assert.Contains("wx-none", ex.Message);
    }

    /// <summary>自定义 resolver 桩（模拟 DB/KMS 实现——两源并存 DB 权威的契约面）。</summary>
    private sealed class FakeResolver(string targetAppId, string appSecret) : IWeChatCredentialResolver
    {
        public Task<WeChatChannelConfig?> ResolveAsync(string appId, CancellationToken ct = default)
            => Task.FromResult<WeChatChannelConfig?>(
                string.Equals(appId, targetAppId, StringComparison.Ordinal)
                    ? new WeChatChannelConfig { ChannelId = "wx-custom", AppId = appId, AppSecret = appSecret, Token = "custom-token" }
                    : null);
    }
}
