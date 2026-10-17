using System;

namespace TKWF.Ext.WeChatOperations;

/// <summary>
/// 微信公众号运营扩展 keyed 注册键单一事实源（对齐 AuthSurface V0.2.0 先例——主框架
/// <see cref="TKW.Framework.Domain.KeyManagement.SymmetricKeyProviderKeys"/> 尚无 WeChatOperations 键，
/// 本扩展**自建常量**并以 C11 扩展边界提报并入主框架（AuthSurface 第 4 扩展 / TrustCenter 第 5 扩展先例：
/// 转达框架组 → <c>SymmetricKeyProviderKeys.WeChatOperations</c> = "WeChatOperations" → CPM 升级后切换收口）。
/// <para>⚠️ key 字符串必须用本常量（P2 单一事实源，防 typo 静默解析错 key——tkwf-extension §4.10 #17）。</para>
/// </summary>
public static class WeChatOperationsKeyProviderKeys
{
    /// <summary>WeChatOperations 扩展注册键（值 "WeChatOperations"——对齐 C11 转达命名规范，待主框架并入后切 SymmetricKeyProviderKeys）。</summary>
    public const string WeChatOperations = "WeChatOperations";
}