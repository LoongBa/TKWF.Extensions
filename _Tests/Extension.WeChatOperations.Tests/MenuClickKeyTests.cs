using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.WeChatOperations.Tests;

/// <summary>
/// <see cref="IMenuService.GetClickKeysAsync"/> 菜单树 click key 收集测试（DMP-Lite 5.4——"自动从菜单树递归收集
/// click 类型按钮标识"）：发布菜单（<see cref="IMenuService.PublishAsync"/> 经 Stub 微信 handler——
/// get_current_selfmenu_info 返回嵌套菜单 JSON → 本地基准快照）→ 递归收集 click 按钮 Key（一级 + 二级 SubButton
/// 递归）/ 非 click 类型（view/miniprogram）不收集 / 重复键去重（Distinct Ordinal）/ 无快照空列表 /
/// 快照损坏（非法 JSON）空列表不抛。
/// <para>生产路径：<c>user.Use&lt;IMenuService&gt;()</c> AOP（AddConstructibleService 守卫工厂）；
/// 快照数据源 = <see cref="MenuSnapshotEntity.MenuJson"/>（发布/拉取确认序列化的结构化 poco）。</para>
/// </summary>
public class MenuClickKeyTests : WeChatOperationsTestHost
{
    private const string Ch = "ch-menu-key";

    /// <summary>
    /// 嵌套菜单树（get_current_selfmenu_info 响应形态）：一级 click（MAIN_CLICK）+ 一级 view（不收集）+
    /// 父菜单容器（无 type）SubButton 递归（click ×3 含重复 MAIN_CLICK / view / miniprogram 不收集）。
    /// 期望收集（Distinct Ordinal）：MAIN_CLICK、SUB_CLICK_1、SUB_CLICK_2。
    /// </summary>
    private const string NestedMenuJson = """
        {
          "is_menu_open": 1,
          "selfmenu_info": {
            "button": [
              { "type": "click", "name": "一级点击", "key": "MAIN_CLICK" },
              { "type": "view", "name": "一级外链", "url": "https://example.com" },
              { "name": "父菜单", "sub_button": [
                { "type": "click", "name": "子点击一", "key": "SUB_CLICK_1" },
                { "type": "view", "name": "子外链", "url": "https://example.com/sub" },
                { "type": "miniprogram", "name": "小程序", "app_id": "wx-app", "page_path": "pages/index" },
                { "type": "click", "name": "子点击二", "key": "SUB_CLICK_2" },
                { "type": "click", "name": "重复键", "key": "MAIN_CLICK" }
              ] }
            ]
          }
        }
        """;

    /// <summary>发布嵌套菜单（PublishAsync——快照 = get_current_selfmenu_info 确认结构）→ GetClickKeysAsync
    /// 递归收集 click Key（一级 + 二级 SubButton 递归）；view/miniprogram 不收集；重复键去重。</summary>
    [Fact]
    public async Task PublishAsync_ThenGetClickKeys_RecursiveCollect_ExcludesNonClick_Deduplicates()
    {
        using var _ = this;
        Handler = WeChatOperationsTestSupport.CreateStubHandler(menuJson: NestedMenuJson);
        var (_, user) = BindScope();
        await SeedConfigAsync(user);
        var service = user.Use<IMenuService>();

        // 发布菜单（menu/create raw 透传——快照取拉取确认的嵌套菜单结构）
        await service.PublishAsync(Ch, """{"button":[{"type":"click","name":"提交菜单","key":"RAW"}]}""",
            TestContext.Current.CancellationToken);

        var keys = await service.GetClickKeysAsync(Ch, TestContext.Current.CancellationToken);

        // 递归收集：一级 click（MAIN_CLICK）+ 二级 SubButton click（SUB_CLICK_1/SUB_CLICK_2）；
        // 非 click 类型（一级 view / 二级 view + miniprogram）不收集——若被收集清单会含 5 键；
        // 重复键（父菜单容器内 MAIN_CLICK 二次出现）经 Distinct(Ordinal) 去重——仅 3 键
        Assert.Equal(new[] { "MAIN_CLICK", "SUB_CLICK_1", "SUB_CLICK_2" }, keys);
    }

    /// <summary>无快照（channel 无 MenuSnapshot）→ 空列表（GetClickKeysAsync 不抛）。</summary>
    [Fact]
    public async Task GetClickKeysAsync_NoSnapshot_EmptyList()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<IMenuService>();

        var keys = await service.GetClickKeysAsync("ch-nosnap", TestContext.Current.CancellationToken);

        Assert.Empty(keys);
    }

    /// <summary>快照损坏（MenuJson 非法 JSON——直接种子快照行）→ 空列表不抛（对齐"快照空/解析失败 → 空列表"语义）。</summary>
    [Fact]
    public async Task GetClickKeysAsync_CorruptSnapshot_EmptyList_NoThrow()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var ds = user.Use<MenuSnapshotEntityDataService>();
        await ds.EntityCreateAsync(new MenuSnapshotEntity
        {
            ChannelId = "ch-corrupt",
            MenuJson = "{invalid json!!",
            UpdatedAt = DateTime.UtcNow,
            CreateTime = DateTime.UtcNow,
            UpdateTime = DateTime.UtcNow,
        }, TestContext.Current.CancellationToken);
        var service = user.Use<IMenuService>();

        var keys = await service.GetClickKeysAsync("ch-corrupt", TestContext.Current.CancellationToken);

        Assert.Empty(keys);   // 非法 JSON → JsonException 捕获 → 空列表不抛
    }

    /// <summary>种子公众号配置（经 IChannelConfigService 生产路径写入——PublishAsync 前置）。</summary>
    private static async Task SeedConfigAsync(DomainUser<TestUserInfo> user)
    {
        var service = user.Use<IChannelConfigService>();
        await service.SaveAsync(new WeChatChannelConfigEntity
        {
            ChannelId = Ch,
            AppId = "wx-menu-key",
            DisplayName = "菜单键号",
            IsEnabled = true,
            ChannelType = "wechat_event",
        }, appSecretPlain: "menu-key-secret", ct: TestContext.Current.CancellationToken);
    }
}
