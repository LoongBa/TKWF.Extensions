namespace TKWF.Ext.AuthSurface;

/// <summary>
/// AuthSurface 附加信息（核验业务信息）AES-GCM 密钥注册键——对齐主框架
/// <see cref="TKW.Framework.Domain.KeyManagement.SymmetricKeyProviderKeys"/>（AuthCenter/Federation/MFA）范式。
/// <para>⚠️ C11 边界（框架 C11 取舍）：主框架常量类仅 3 键（AuthCenter/Federation/MFA）——第 4 个扩展需 keyed 时
/// 应在本类加 const（框架变更）。本扩展 v0.2.0 核验场景（PayloadEncrypted AES-GCM）先行自建常量实施
/// （值 "AuthSurface"，与转告 E4 语义一致）——已转达框架组申请将本常量并入主框架
/// <c>SymmetricKeyProviderKeys</c>（CPM 升级后切换主框架常量，防 typo 静默解析错 key 单一事实源）。</para>
/// </summary>
public static class AuthSurfaceKeyProviderKeys
{
    /// <summary>AuthSurface 扩展注册键（值 "AuthSurface"——与主框架 E4 转告严格一致）。</summary>
    public const string AuthSurface = "AuthSurface";
}
