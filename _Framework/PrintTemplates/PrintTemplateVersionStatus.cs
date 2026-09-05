namespace TKWF.Ext.PrintTemplates;

/// <summary>
/// 模板版本状态枚举——Draft（草稿）/ Active（当前激活）/ Archived（已归档，可追溯渲染）
/// </summary>
public enum PrintTemplateVersionStatus
{
    /// <summary>草稿（每模板仅一个 Draft，不占 Active 槽位）</summary>
    Draft = 0,

    /// <summary>当前激活版本（每模板唯一 Active，发布时自动递增 minor）</summary>
    Active = 1,

    /// <summary>已归档（可追溯渲染，不可再发布）</summary>
    Archived = 2
}
