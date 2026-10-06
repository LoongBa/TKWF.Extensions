using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.KeyManagement;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 第三方平台凭证服务——公众号/小程序/网站应用/移动应用凭证管理（方案 §5.10）。
/// <para>摒弃 DMP 裸 FreeSql（POCO + 直触 ORM）——SG1 化 + DataService 委托；</para>
/// <para>E4 密钥管理抽象（V0.7.0）：AES-GCM 加解密在【本服务边界】——经注入的
/// <see cref="ISymmetricKeyProvider"/>（keyed 注册键 <see cref="SymmetricKeyProviderKeys.AuthCenter"/>，
/// <c>FileSymmetricKeyProvider</c> 生产 fail-fast / 开发随机兜底）；DataService 回归纯持久化
/// （AppSecretEncrypted 密文列原样存取）；本服务只见明文/负责加密（密文格式不触 DataService）。</para>
/// <para>数据访问红线合规：不注入 IFreeSql/IEntityDAC——全部方法委托 DataService 边界方法。</para>
/// <para>V4.10.53（领域自治根治，ADR90）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文
/// （IDomainUser 永不注册 DI）；DataService 经 <c>User.Use&lt;具体类&gt;()</c> NoAop 懒加载；
/// 注册改 <c>AddConstructibleService&lt;IPlatformCredentialService, PlatformCredentialService&gt;</c>。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。</para>
/// </summary>
[DiContractIgnore]
internal sealed class PlatformCredentialService : DomainServiceBase, IPlatformCredentialService
{
    private PlatformCredentialEntityDataService? _dataService;
    private readonly ISymmetricKeyProvider _keys;

    private PlatformCredentialEntityDataService DataService => _dataService ??= User.Use<PlatformCredentialEntityDataService>();

    // E4 密钥管理抽象（V0.7.0）：keyed ISymmetricKeyProvider 注入（FileSymmetricKeyProvider 构造即加载密钥——
    // 生产缺密钥 fail-fast 语义与旧 PlatformCredentialKeyStore.Initialize 启动期等价；keyed 单例惰性解析，
    // 首次构造本服务时触发）
    public PlatformCredentialService(
        IDomainUser user,
        [FromKeyedServices(SymmetricKeyProviderKeys.AuthCenter)] ISymmetricKeyProvider keys,
        IOptions<AuthCenterOptions> options,
        ILogger<PlatformCredentialService> logger)
        : base(user)
    {
        _keys = keys;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PlatformCredentialEntity>> GetEnabledByPlatformAsync(string platform, CancellationToken ct = default)
        => DataService.GetEnabledByPlatformAsync(platform, ct);

    /// <inheritdoc />
    public Task<PlatformCredentialEntity?> GetAsync(string platform, string appType, CancellationToken ct = default)
        => DataService.GetByAppAsync(platform, appType, ct);

    /// <inheritdoc />
    public async Task<PlatformCredentialSecret?> GetSecretAsync(string platform, string appType, CancellationToken ct = default)
    {
        var entity = await DataService.GetByAppAsync(platform, appType, ct);
        if (entity == null || !entity.IsEnabled || string.IsNullOrEmpty(entity.AppSecretEncrypted))
            return null; // 禁用凭证不发放明文密钥（对标旧 GetSecretByAppAsync 启停管控语义）
        return new PlatformCredentialSecret(entity.AppId, _keys.Decrypt(entity.AppSecretEncrypted));
    }

    /// <inheritdoc />
    public async Task<PlatformCredentialSecret?> GetSecretByAppIdAsync(string platform, string appId, CancellationToken ct = default)
    {
        var entity = await DataService.GetByAppIdAsync(platform, appId, ct);
        if (entity == null || string.IsNullOrEmpty(entity.AppSecretEncrypted))
            return null;
        return new PlatformCredentialSecret(entity.AppId, _keys.Decrypt(entity.AppSecretEncrypted));
    }

    /// <inheritdoc />
    public async Task CreateAsync(PlatformCredentialEntity credential, string plainSecret, CancellationToken ct = default)
    {
        if (credential == null) throw new ArgumentNullException(nameof(credential));
        if (plainSecret == null) throw new ArgumentNullException(nameof(plainSecret));
        credential.AppSecretEncrypted = _keys.Encrypt(plainSecret);
        await DataService.CreateAsync(credential, ct); // CreateTime/UpdateTime UTC 由 DataService 置
    }

    /// <inheritdoc />
    public async Task UpdateAsync(PlatformCredentialEntity credential, string? newPlainSecret, CancellationToken ct = default)
    {
        if (credential == null) throw new ArgumentNullException(nameof(credential));
        if (newPlainSecret != null)
            credential.AppSecretEncrypted = _keys.Encrypt(newPlainSecret);
        await DataService.UpdateAsync(credential, ct); // UpdateTime UTC 由 DataService 置
    }

    /// <inheritdoc />
    public Task SetEnabledAsync(long id, bool enabled, CancellationToken ct = default)
        => DataService.SetEnabledAsync(id, enabled, ct);
}
