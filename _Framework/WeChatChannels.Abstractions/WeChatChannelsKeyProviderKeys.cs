using System;

namespace TKWF.Ext.WeChatChannels;

/// <summary>
/// WeChatChannels 扩展 keyed 注册键单一事实源（2026-11-05 拆分下沉——原 WeChatOperationsKeyProviderKeys
/// 迁入并改值 "WeChatChannels"，对称密钥键随通道面下沉）。
/// <para>⚠️ 收口路径（S3/C11）：本键<b>已转告主框架并入</b> <see cref="TKW.Framework.Domain.KeyManagement.SymmetricKeyProviderKeys.WeChatChannels"/>
/// （第 7 扩展边界，值 "WeChatChannels"）——框架并入 + CPM lockstep 后<b>删除本过渡常量</b>，改直用主框架常量
/// （对齐 AuthSurface/TrustCenter 收口先例：自建常量 → 主框架并入 → 切换删除）。
/// key 字符串必须用本常量（P2 单一事实源，防 typo 静默解析错 key——tkwf-extension §4.10 #17）。</para>
/// </summary>
public static class WeChatChannelsKeyProviderKeys
{
    /// <summary>WeChatChannels 扩展注册键（值 "WeChatChannels"——防 AuthCenter 等其它扩展 key 冲突 last-wins）。
    /// ⚠️ 过渡常量：主框架 SymmetricKeyProviderKeys.WeChatChannels 并入后删除本类，改直用主框架常量。</summary>
    public const string WeChatChannels = "WeChatChannels";
}