namespace TKWF.Ext.FeatureManagement;

/// <summary>Provider 层常量（对齐 Settings/Permissions provider 命名约定；跳过 ABP Edition——TKWF 无版本概念）。</summary>
public static class FeatureProviders
{
    /// <summary>全局层（ProviderKey 忽略/null）。</summary>
    public const string Global = "Global";

    /// <summary>租户层（ProviderKey = TenantId）。</summary>
    public const string Tenant = "Tenant";

    /// <summary>角色层（ProviderKey = 角色名；多角色遍历序命中）。</summary>
    public const string Role = "Role";

    /// <summary>用户层（ProviderKey = UserId；最优先）。</summary>
    public const string User = "User";
}
