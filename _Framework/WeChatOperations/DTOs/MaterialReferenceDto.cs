namespace TKWF.Ext.WeChatOperations.DTOs;

/// <summary>
/// 素材引用位置（跨回复规则聚合——欢迎语/关键词/Click 映射/全消息类型/Default；
/// DMP-Lite 5.2 素材引用反查"引用 N 处"+清单可跳转解绑）。
/// <para>对齐既有 DTO 风格（<c>init</c> 投影 record——门面经 <c>EntitySelectAsync</c> 聚合投影）。</para>
/// </summary>
public sealed record MaterialReferenceDto
{
    /// <summary>被引用素材 Id（<see cref="MaterialEntity"/> 全局共享——一个素材多规则复用）。</summary>
    public long MaterialId { get; init; }

    /// <summary>引用规则 Id（<see cref="ReplyRuleEntity"/>——清单行可跳转解绑）。</summary>
    public long ReplyRuleId { get; init; }

    /// <summary>触发场景（int——1 Subscribe / 2 Keyword / 3 Click / 4 Image / 5 Voice / 6 Video / 7 ShortVideo / 8 Location / 9 Link / 10 Scan / 11 Default）。</summary>
    public int SceneType { get; init; }

    /// <summary>触发键（关键词或菜单 key——场景型规则为空；Click 场景 = 菜单按钮 key）。</summary>
    public string? TriggerKey { get; init; }

    /// <summary>规则启用（默认 true——禁用规则仍计入引用（删除保护语义防悬空）；生效性由 IsEnabled 区分）。</summary>
    public bool IsEnabled { get; init; }
}
