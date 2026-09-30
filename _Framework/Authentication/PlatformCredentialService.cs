using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TKWF.Ext.Authentication;

/// <summary>
/// 第三方平台凭证服务——公众号/小程序/网站应用/移动应用凭证管理（方案 §5.10）。
/// <para>摒弃 DMP 裸 FreeSql（POCO + 直触 ORM）——SG1 化 + DataService 委托；</para>
/// <para>AES-GCM 加解密在 <see cref="PlatformCredentialEntityDataService"/> 边界
/// （<c>AuthCenterOptions.SecretEncryptionKeyPath</c> 派生密钥）；本服务只见明文（不触 AppSecretEncrypted 密文格式）。</para>
/// <para>数据访问红线合规：不注入 IFreeSql/IEntityDAC——全部方法委托 DataService 边界方法。</para>
/// <para>internal sealed（对齐 Identity UserStore 先例——DataService 为 internal，public 构造器会 CS0051）。</para>
/// </summary>
internal sealed class PlatformCredentialService : IPlatformCredentialService
{
    private readonly PlatformCredentialEntityDataService _dataService;
    private readonly IOptions<AuthCenterOptions> _options;
    private readonly ILogger<PlatformCredentialService> _logger;

    public PlatformCredentialService(
        PlatformCredentialEntityDataService dataService,
        IOptions<AuthCenterOptions> options,
        ILogger<PlatformCredentialService> logger)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 生产 fail-fast 门（对齐签名密钥策略）：SecretEncryptionKeyPath 未配置 → 拒绝启动。
        if (options.Value.IsProduction && string.IsNullOrWhiteSpace(options.Value.SecretEncryptionKeyPath))
        {
            _logger.LogError("生产环境未配置 AuthCenterOptions.SecretEncryptionKeyPath——平台凭证 AES-GCM 密钥无法派生，拒绝启动。");
            throw new InvalidOperationException("生产环境必须配置 AuthCenterOptions.SecretEncryptionKeyPath（平台凭证 AES-GCM 密钥派生文件路径）。");
        }

        // AES-GCM 密钥幂等初始化（DataService 边界经 PlatformCredentialKeyStore 取密钥——生产缺文件 fail-fast / 开发生成+Warning）
        PlatformCredentialKeyStore.Initialize(options.Value, logger);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PlatformCredentialEntity>> GetEnabledByPlatformAsync(string platform, CancellationToken ct = default)
        => _dataService.GetEnabledByPlatformAsync(platform, ct);

    /// <inheritdoc />
    public Task<PlatformCredentialEntity?> GetAsync(string platform, string appType, CancellationToken ct = default)
        => _dataService.GetByAppAsync(platform, appType, ct);

    /// <inheritdoc />
    public Task<PlatformCredentialSecret?> GetSecretAsync(string platform, string appType, CancellationToken ct = default)
        => _dataService.GetSecretByAppAsync(platform, appType, ct);

    /// <inheritdoc />
    public Task<PlatformCredentialSecret?> GetSecretByAppIdAsync(string platform, string appId, CancellationToken ct = default)
        => _dataService.GetSecretByAppIdAsync(platform, appId, ct);

    /// <inheritdoc />
    public Task CreateAsync(PlatformCredentialEntity credential, string plainSecret, CancellationToken ct = default)
        => _dataService.CreateEncryptedAsync(credential, plainSecret, ct);

    /// <inheritdoc />
    public Task UpdateAsync(PlatformCredentialEntity credential, string? newPlainSecret, CancellationToken ct = default)
        => _dataService.UpdateEncryptedAsync(credential, newPlainSecret, ct);

    /// <inheritdoc />
    public Task SetEnabledAsync(long id, bool enabled, CancellationToken ct = default)
        => _dataService.SetEnabledAsync(id, enabled, ct);
}
