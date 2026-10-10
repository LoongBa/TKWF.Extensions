using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.KeyManagement;

namespace TKWF.Ext.TrustCenter;

/// <summary>
/// SSO 应用注册服务（下游 SP 接入点）——<see cref="ISsoClientService"/> 实现。
/// <para>设计文档 §5.4/§6.3：app_id + origin 白名单（防开放重定向，§8.4——不接受自由 redirect_uri）+ scope +
/// client credential（AES-GCM 密文落库）+ per-channel HMAC 密钥（<c>/sso/issue</c> 验签密钥来源，Oracle P1-4——
/// 不复用 PlatformCredentialEntity）。</para>
/// <para>数据访问红线合规：不注入 IFreeSql/IEntityDAC——全部经 <see cref="SsoClientEntityDataService"/>
/// 内部转发访问器（Entity*，同程序集）委托查询/写入；AES-GCM 加解密经注入的
/// <see cref="ISymmetricKeyProvider"/>（键 <see cref="SymmetricKeyProviderKeys.TrustCenter"/>——
/// <c>TrustCenterOptions.SecretEncryptionKeyPath</c> 前 32 字节密钥，FileSymmetricKeyProvider），
/// 密文落库明文不落库。</para>
/// <para>V4.10.53（领域自治根治，ADR90）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文
/// （IDomainUser 永不注册 DI）；DataService 经 <c>User.Use&lt;具体类&gt;()</c> NoAop 懒加载；
/// 注册改 <c>AddConstructibleService&lt;ISsoClientService, SsoClientService&gt;</c>（Initializer 负责）。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。</para>
/// <para><b>TrustCenter 剥离（2026-10-09）</b>：自 Federation 迁入——命名空间改 TKWF.Ext.TrustCenter；
/// keyed 密钥键 <see cref="SymmetricKeyProviderKeys.Federation"/> → <see cref="SymmetricKeyProviderKeys.TrustCenter"/>
/// （v4.10.70 主框架常量并入，C11 第 5 扩展边界兑现）。</para>
/// </summary>
[DiContractIgnore]
internal sealed class SsoClientService : DomainServiceBase, ISsoClientService
{
    private SsoClientEntityDataService? _dataService;
    private readonly ISymmetricKeyProvider _keys;
    private readonly IOptions<TrustCenterOptions> _options;
    private readonly ILogger<SsoClientService> _logger;

    private SsoClientEntityDataService DataService => _dataService ??= User.Use<SsoClientEntityDataService>();

    public SsoClientService(
        IDomainUser user,
        [FromKeyedServices(SymmetricKeyProviderKeys.TrustCenter)] ISymmetricKeyProvider keys,
        IOptions<TrustCenterOptions> options,
        ILogger<SsoClientService> logger)
        : base(user)
    {
        _keys = keys;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 注册应用——生成 <c>app-{Guid}</c> AppId + client secret（CSPRNG 32B Base64Url）+ HMAC secret
    /// （CSPRNG 32B Base64Url）→ 均 AES-GCM 加密落库（ClientSecretEncrypted/HmacSecretEncrypted）；
    /// origin/scopes JSON 序列化（JsonSerializer）；scopes 为空默认 <c>["profile:basic"]</c>。
    /// </summary>
    public async Task<SsoClientInfo> RegisterAsync(IReadOnlyList<string> originWhitelist, IReadOnlyList<string> scopes, CancellationToken ct = default)
    {
        var origins = originWhitelist?.ToList() ?? [];
        var scopeList = scopes is { Count: > 0 } ? scopes.ToList() : ["profile:basic"];

        var appId = "app-" + Guid.NewGuid().ToString("N");
        var clientSecret = NewSecret();
        var hmacSecret = NewSecret();
        var now = DateTime.UtcNow;

        var entity = new SsoClientEntity
        {
            AppId = appId,
            OriginWhitelist = JsonSerializer.Serialize(origins),
            Scopes = JsonSerializer.Serialize(scopeList),
            ClientSecretEncrypted = _keys.Encrypt(clientSecret),
            HmacSecretEncrypted = _keys.Encrypt(hmacSecret),
            IsEnabled = true,
            CreateTime = now,
            UpdateTime = now,
        };
        await DataService.EntityCreateAsync(entity, ct);

        return new SsoClientInfo(appId, origins, scopeList, entity.IsEnabled);
    }

    /// <summary>
    /// 取注册信息（校验 origin 白名单用）——反序列化 origin/scopes → <see cref="SsoClientInfo"/>；
    /// 404/禁用语义经返回 null + 调用方判定；禁用的返回 <c>IsEnabled=false</c> 不隐藏。
    /// </summary>
    public async Task<SsoClientInfo?> GetInfoAsync(string appId, CancellationToken ct = default)
    {
        var entity = await GetEntityAsync(appId, ct);
        if (entity == null) return null;
        return new SsoClientInfo(
            entity.AppId,
            DeserializeList(entity.OriginWhitelist),
            DeserializeList(entity.Scopes),
            entity.IsEnabled);
    }

    /// <summary>
    /// 校验客户端凭据（server-to-server profile API 鉴权）——AES-GCM 解密 ClientSecretEncrypted →
    /// <c>CryptographicOperations.FixedTimeEquals</c> 恒定时间比对（防时序攻击）；应用不存在/禁用或密文损坏 → false。
    /// </summary>
    public async Task<bool> ValidateClientCredentialAsync(string appId, string clientSecret, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(clientSecret)) return false;
        var entity = await GetEntityAsync(appId, ct);
        if (entity == null) return false;

        try
        {
            var decrypted = _keys.Decrypt(entity.ClientSecretEncrypted);
            return FixedTimeEquals(decrypted, clientSecret);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            _logger.LogWarning("SSO client credential 解密失败（appId={AppId}）——密文损坏：{Ex}", appId, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 取 HMAC 密钥（明文——<c>/sso/issue</c> 验签用；AES-GCM 解密于 DataService 边界语义等价——
    /// 解密经注入的 <see cref="ISymmetricKeyProvider"/> 于本服务边界）。应用不存在返回 null。
    /// </summary>
    public async Task<string?> GetHmacSecretAsync(string appId, CancellationToken ct = default)
    {
        var entity = await GetEntityAsync(appId, ct);
        if (entity == null) return null;
        return _keys.Decrypt(entity.HmacSecretEncrypted);
    }

    /// <summary>
    /// 校验重定向 origin（防开放重定向——设计文档 §8.4：target_app_id → 注册精确 origin，scheme+host，
    /// <see cref="StringComparison.Ordinal"/> 精确匹配）。应用不存在返回 false。
    /// </summary>
    public async Task<bool> IsOriginAllowedAsync(string appId, string origin, CancellationToken ct = default)
    {
        var entity = await GetEntityAsync(appId, ct);
        if (entity == null) return false;
        var whitelist = DeserializeList(entity.OriginWhitelist);
        return whitelist.Any(item => string.Equals(item, origin, StringComparison.Ordinal));
    }

    // ── 私有实现（对齐 TokenService 静态辅助模式） ──

    private async Task<SsoClientEntity?> GetEntityAsync(string appId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(appId)) return null;
        return await DataService.EntityGetAsync(m => m.AppId == appId, ct);
    }

    /// <summary>origin/scopes JSON 反序列化（非法 JSON 容忍 → 空列表兜底）。</summary>
    private static List<string> DeserializeList(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>CSPRNG 32 字节 → Base64Url（client secret / HMAC secret——密文加密源明文）。</summary>
    private static string NewSecret()
        => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    /// <summary>Base64Url 编码（RFC 7515——trim padding，+/ 替换 -_）。</summary>
    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>恒定时间字符串比对（防时序攻击——client credential 比对）。</summary>
    private static bool FixedTimeEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
