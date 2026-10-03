using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Authentication;

/// <summary>微信用户信息（方案 §5.10——sns/userinfo 响应裁剪）。</summary>
/// <param name="OpenId">openid。</param>
/// <param name="Nickname">昵称（未授权 snsapi_base 时 null）。</param>
/// <param name="Avatar">头像 URL（未授权时 null）。</param>
/// <param name="UnionId">unionid（开放平台归并，未关联时 null）。</param>
public sealed record WeChatUserInfo(string OpenId, string? Nickname, string? Avatar, string? UnionId);

/// <summary>
/// 微信 API 客户端契约（对齐 DMP 语义重写，解耦商户模型）。
/// <para>方案 §5.10——凭证经 <see cref="IPlatformCredentialService"/> 解析（不再直注入裸 DB）；</para>
/// <para>access_token L1 缓存（ConcurrentDictionary + SemaphoreSlim 并发锁 + 提前 5 分钟过期刷新）；公众号网页授权/开放平台扫码双形态。</para>
/// </summary>
public interface IWeChatApiClient : IDomainService
{
    /// <summary>获取 access_token（L1 缓存 + 并发锁 + 提前 5 分钟过期刷新；凭证经 PlatformCredentialService 解析）。</summary>
    Task<string> GetAccessTokenAsync(string appId, string appType, CancellationToken ct = default);

    /// <summary>用微信授权 code 换 openid（sns/oauth2/access_token——AppSecret 服务端持有，access_token 不缓存）。</summary>
    Task<string> GetOpenIdAsync(string appId, string code, CancellationToken ct = default);

    /// <summary>拉取用户信息（sns/userinfo；未授权 snsapi_base 时仅 openid 可回退构造）。</summary>
    Task<WeChatUserInfo?> GetUserInfoAsync(string accessToken, string openid, CancellationToken ct = default);
}
