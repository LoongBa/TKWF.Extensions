using System.Text.Json.Serialization;

namespace TKWF.Federation.WeChat;

/// <summary>
/// 微信自定义菜单查询结果（<c>cgi-bin/get_current_selfmenu_info</c> 返回——结构化 poco，供展示/回填编辑器）。
/// <para>菜单 JSON 处理策略（方案 docs/WeChatOperations/微信公众号运营扩展-开发方案.md §3.2.1）：
/// <b>提交 = raw JSON 透传</b>（<see cref="WeChatApiClient.CreateMenuAsync"/> 收 string——编辑器自由形态），
/// <b>返回 = 结构化 poco</b>（本类型族——展示/回填编辑器）。两类结构命名区分（提交 string / 返回 poco），消除混淆。</para>
/// <para><see cref="IsMenuOpen"/>：菜单是否开启（微信官方 1=开启 / 0=未开启）；<see cref="SelfMenuInfo"/>：
/// 菜单结构（未配置菜单 / <c>is_menu_open</c>=0 时微信侧可能缺失 → null）。</para>
/// </summary>
public sealed record WeChatSelfMenuInfoResult(
    [property: JsonPropertyName("is_menu_open")] int IsMenuOpen,
    [property: JsonPropertyName("selfmenu_info")] WeChatSelfMenuInfo? SelfMenuInfo);

/// <summary>菜单结构（<c>selfmenu_info.button</c>——根按钮列表）。</summary>
public sealed record WeChatSelfMenuInfo(
    [property: JsonPropertyName("button")] WeChatMenuButton[] Button);

/// <summary>
/// 菜单按钮（微信官方 button 字段全集——click/view/miniprogram/media_id/article_id/view_limited 等类型参数各异，
/// 仅类型化公共字段 + <see cref="SubButton"/> 递归子菜单；字段命名对齐微信官方 JSON（snake_case））。
/// </summary>
public sealed record WeChatMenuButton(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("key")] string? Key,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("media_id")] string? MediaId,
    [property: JsonPropertyName("article_id")] string? ArticleId,
    [property: JsonPropertyName("app_id")] string? AppId,
    [property: JsonPropertyName("page_path")] string? PagePath,
    [property: JsonPropertyName("sub_button")] WeChatMenuButton[]? SubButton);
