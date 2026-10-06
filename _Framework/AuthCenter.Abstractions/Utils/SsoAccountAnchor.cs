using System;
using System.Security.Cryptography;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 联盟锚点值生成器（N2 联盟锚点映射策略 Oracle 评审 P1-4 定案——锚点 = TKWF 自生成的<b>平台无关稳定值</b>）。
/// <para>语义：<c>FederationAnchorOpenId</c> 列存本生成器产物（<c>anc_</c> 前缀 + CSPRNG 16 字节 base64url，
/// 26 字符）——<b>非 uid 本身、非某平台 unionid、非 null</b>——保持 <c>GetByFederationAnchorAsync</c> 独立反查语义
/// （不退化 ≡ GetByUId）；所有平台 <c>(channel_id, external_uid)</c> 经 <c>PlatformAccountMap</c> 归一指向同一 uid，
/// anchor 列 = 加速直认快照（冗余，非唯一依赖）。</para>
/// <para>生成时机（N2 定案）：Federation 编排层在<b>首个渠道绑定</b>时生成并写
/// <see cref="ISsoAccountLinkService.SetFederationAnchorAsync"/>；既有微信 unionid 落地数据按 channel 映射保留。
/// 纯 BCL 零依赖（Abstractions 契约包零框架引用约束保持）；生成值无需解码——不透明字符串（P12 语义）。</para>
/// </summary>
public static class SsoAccountAnchor
{
    private const string Prefix = "anc_";

    /// <summary>
    /// 生成平台无关稳定锚点值（<c>anc_</c> 前缀 + CSPRNG 16 字节 base64url 无填充——26 字符）。
    /// <para>CSPRNG（<see cref="RandomNumberGenerator.GetBytes(int)"/>）——不可预测防枚举（锚点具账号关联敏感性）；
    /// base64url（RFC 4648——<c>+</c>/<c>/</c> → <c>-</c>/<c>_</c>，去尾 <c>=</c>）——无 URL 转义歧义。</para>
    /// </summary>
    public static string Create()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Prefix + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}