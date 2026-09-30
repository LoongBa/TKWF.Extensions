using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Authentication;

/// <summary>平台凭证明文密钥（方案 §5.10——Service 层只见明文；AppSecret AES-GCM 加密在 DataService 边界）。</summary>
/// <param name="AppId">应用 AppId。</param>
/// <param name="AppSecret">解密后的明文 AppSecret（DB 无明文）。</param>
public sealed record PlatformCredentialSecret(string AppId, string AppSecret);

/// <summary>
/// 第三方平台凭证服务契约——公众号/小程序/网站应用/移动应用凭证管理。
/// <para>方案 §5.10——摒弃 DMP 裸 FreeSql；AES-GCM 加解密在 <c>PlatformCredentialEntityDataService</c> 边界
/// （<c>AuthCenterOptions.SecretEncryptionKeyPath</c> 派生密钥）；本接口只见明文。</para>
/// </summary>
public interface IPlatformCredentialService
{
    /// <summary>按平台查询全部启用凭证（实体返回——AppSecretEncrypted 密文列已 DtoFieldIgnore，不外泄）。</summary>
    Task<IReadOnlyList<PlatformCredentialEntity>> GetEnabledByPlatformAsync(string platform, CancellationToken ct = default);

    /// <summary>按平台 + 应用类型查询凭证（实体返回）。</summary>
    Task<PlatformCredentialEntity?> GetAsync(string platform, string appType, CancellationToken ct = default);

    /// <summary>按平台 + 应用类型获取明文密钥（读路径解密——WeChatApiClient 等消费）。</summary>
    Task<PlatformCredentialSecret?> GetSecretAsync(string platform, string appType, CancellationToken ct = default);

    /// <summary>按平台 + 应用 AppId 获取明文密钥（Oracle M3——微信授权 code 绑定发起 AppId，须按 AppId 精确定位凭证，防多应用误配）。</summary>
    Task<PlatformCredentialSecret?> GetSecretByAppIdAsync(string platform, string appId, CancellationToken ct = default);

    /// <summary>创建凭证（明文 secret 在 DataService 边界加密落库）。</summary>
    Task CreateAsync(PlatformCredentialEntity credential, string plainSecret, CancellationToken ct = default);

    /// <summary>更新凭证（newPlainSecret null = 不更新密钥；非 null = 重新加密）。</summary>
    Task UpdateAsync(PlatformCredentialEntity credential, string? newPlainSecret, CancellationToken ct = default);

    /// <summary>启用/禁用。</summary>
    Task SetEnabledAsync(long id, bool enabled, CancellationToken ct = default);
}
