using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.WeChatOperations.Tests;

/// <summary>
/// <see cref="IMaterialService.GetReferencesAsync"/> 素材引用反查测试（DMP-Lite 5.2——"引用 N 处" + 清单可跳转解绑）：
/// 无引用空列表 / 多条规则跨 SceneType 聚合（MaterialId/ReplyRuleId/SceneType/TriggerKey/IsEnabled 逐行断言——
/// 禁用规则仍计入引用，生效性由 IsEnabled 区分）/ 与删除保护联动（清单行含 ReplyRuleId——据此
/// <see cref="IReplyRuleService.DeleteRuleAsync"/> 解绑后素材可删——清单行可跳转解绑语义）。
/// <para>生产路径：<c>user.Use&lt;IMaterialService&gt;()</c> AOP（AddConstructibleService 守卫工厂）；
/// 规则种子经 <c>user.Use&lt;ReplyRuleEntityDataService&gt;()</c> NoAop 直写（对齐 MaterialServiceTests 既有风格）。</para>
/// </summary>
public class MaterialReferenceTests : WeChatOperationsTestHost
{
    private const string Ch = "ch-mat-ref";

    /// <summary>无规则引用 → 空列表（GetReferencesAsync 不抛——空素材无引用清单）。</summary>
    [Fact]
    public async Task GetReferencesAsync_NoRules_EmptyList()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<IMaterialService>();
        var id = await service.SaveAsync(new MaterialEntity
        {
            Name = "无引用素材",
            Kind = 1,
            Content = "内容",
            IsEnabled = true,
        }, TestContext.Current.CancellationToken);

        var refs = await service.GetReferencesAsync(id, TestContext.Current.CancellationToken);

        Assert.Empty(refs);
    }

    /// <summary>多条规则引用（不同 SceneType——欢迎语/关键词/Click，含禁用规则）→ 聚合清单
    /// （SceneType 升序 + 每行 MaterialId/ReplyRuleId/TriggerKey/IsEnabled 断言——禁用规则仍计入引用，删除保护语义防悬空）。</summary>
    [Fact]
    public async Task GetReferencesAsync_MultipleSceneTypes_Aggregated()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<IMaterialService>();
        var id = await service.SaveAsync(new MaterialEntity
        {
            Name = "被多规则引用素材",
            Kind = 1,
            Content = "内容",
            IsEnabled = true,
        }, TestContext.Current.CancellationToken);

        var subId = await SeedRuleAsync(user, Ch, WeChatReplySceneTypes.Subscribe, materialId: id, replyText: "欢迎");
        var kwId = await SeedRuleAsync(user, Ch, WeChatReplySceneTypes.Keyword, materialId: id, triggerKey: "优惠", matchMode: 1, isEnabled: false);
        var clickId = await SeedRuleAsync(user, Ch, WeChatReplySceneTypes.Click, materialId: id, triggerKey: "V1001_MENU");

        var refs = await service.GetReferencesAsync(id, TestContext.Current.CancellationToken);

        // SceneType 升序（GetReferencesAsync 排序契约）：Subscribe(1) → Keyword(2) → Click(3)
        Assert.Equal(3, refs.Count);
        Assert.Equal(
            new[] { WeChatReplySceneTypes.Subscribe, WeChatReplySceneTypes.Keyword, WeChatReplySceneTypes.Click },
            refs.Select(r => r.SceneType).ToArray());

        // 欢迎语规则（场景型——TriggerKey 空，启用）
        Assert.Equal(id, refs[0].MaterialId);
        Assert.Equal(subId, refs[0].ReplyRuleId);
        Assert.Null(refs[0].TriggerKey);
        Assert.True(refs[0].IsEnabled);

        // 关键词规则（禁用——仍计入引用，生效性由 IsEnabled 区分）
        Assert.Equal(kwId, refs[1].ReplyRuleId);
        Assert.Equal("优惠", refs[1].TriggerKey);
        Assert.False(refs[1].IsEnabled);

        // Click 映射规则（菜单 key 为 TriggerKey，启用）
        Assert.Equal(clickId, refs[2].ReplyRuleId);
        Assert.Equal("V1001_MENU", refs[2].TriggerKey);
        Assert.True(refs[2].IsEnabled);
    }

    /// <summary>与删除保护联动：GetReferencesAsync 清单含 ReplyRuleId → 据此 DeleteRuleAsync 解绑
    /// （清单行可跳转解绑语义——DMP-Lite 5.2）→ 全部解绑后 DeleteAsync 素材成功。</summary>
    [Fact]
    public async Task GetReferencesAsync_UnbindViaRuleIds_ThenDeleteMaterialSucceeds()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<IMaterialService>();
        var ruleService = user.Use<IReplyRuleService>();
        var id = await service.SaveAsync(new MaterialEntity
        {
            Name = "待解绑素材",
            Kind = 1,
            Content = "内容",
            IsEnabled = true,
        }, TestContext.Current.CancellationToken);
        await SeedRuleAsync(user, Ch, WeChatReplySceneTypes.Subscribe, materialId: id);
        await SeedRuleAsync(user, Ch, WeChatReplySceneTypes.Keyword, materialId: id, triggerKey: "优惠", matchMode: 1);

        // 解绑前删除被拒（删除保护——Oracle P1）
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(id, TestContext.Current.CancellationToken));
        Assert.Contains("禁止删除", ex.Message);

        // 反查清单 → 逐行规则 Id 解绑（清单可跳转语义）
        var refs = await service.GetReferencesAsync(id, TestContext.Current.CancellationToken);
        Assert.Equal(2, refs.Count);
        foreach (var r in refs)
        {
            Assert.Equal(id, r.MaterialId);
            var deleted = await ruleService.DeleteRuleAsync(r.ReplyRuleId, TestContext.Current.CancellationToken);
            Assert.True(deleted);
        }

        // 全部解绑后 → 删除成功
        var ok = await service.DeleteAsync(id, TestContext.Current.CancellationToken);
        Assert.True(ok);
        Assert.Empty(await service.ListAsync(null, TestContext.Current.CancellationToken));
    }

    /// <summary>种子回复规则（ReplyRuleEntityDataService 生产路径直写——MaterialId 引用该素材）。</summary>
    private static async Task<long> SeedRuleAsync(DomainUser<TestUserInfo> user, string channelId, int sceneType,
        long materialId, string? triggerKey = null, int matchMode = 0, bool isEnabled = true, string? replyText = null)
    {
        var ds = user.Use<ReplyRuleEntityDataService>();
        var created = await ds.EntityCreateAsync(new ReplyRuleEntity
        {
            ChannelId = channelId,
            SceneType = sceneType,
            TriggerKey = triggerKey,
            MatchMode = matchMode,
            MaterialId = materialId,
            ReplyText = replyText,
            IsEnabled = isEnabled,
            SortOrder = 1,
            CreateTime = DateTime.UtcNow,
            UpdateTime = DateTime.UtcNow,
        }, TestContext.Current.CancellationToken);
        return created.Id;
    }
}
