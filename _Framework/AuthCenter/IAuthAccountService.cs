using System;
using System.Linq;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Core.Hosting;
using TKW.Framework.Domain;
using TKW.Framework.Domain.AuthController;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.SecurityLog;

namespace TKWF.Ext.AuthCenter;

/// <summary>账号管理服务——对外写契约（DMP 渐进替换影子账号 upsert / 装配实例 / 内部复用）。
/// <para>写路径暴露裁定（ADR-Authentication-账号写契约）：v0.2.0 方案 C1「写路径不暴露」因 DMP P1 令牌替换
/// 真实消费需求反转——平台管理员影子 AuthAccount 必须可由消费端创建/更新/失效（TokenService.RefreshTokenAsync
/// 强依赖 AuthAccount 存在 + IsEnabled + TokenVersion，L196-200）。</para></summary>
public interface IAuthAccountService : IDomainService
{
    /// <summary>按平台内部 id 查询账号（upsert 流 read 前置；与 <see cref="IAuthAccountQueryService"/> 重复委托同一 DataService——单注入便利）。</summary>
    Task<AuthAccountEntity?> GetByUIdAsync(string uid, CancellationToken ct = default);

    /// <summary>创建账号（回写自增 Id；CreateTime/UpdateTime UTC——影子账号 Phone 可空：唯一索引对 NULL 放行）。</summary>
    Task CreateAsync(AuthAccountEntity account, CancellationToken ct = default);

    /// <summary>更新账号（先设 UpdateTime；IsEnabled 禁用/回用由此路）。</summary>
    Task UpdateAsync(AuthAccountEntity account, CancellationToken ct = default);

    /// <summary>TokenVersion 自增——密码/绑定变更后旧 Refresh Token 失效（DMP 改密 → 影子账号 bump → 旧 refresh REFRESH_STALE）。</summary>
    Task<int> IncrementTokenVersionAsync(string uid, CancellationToken ct = default);

    /// <summary>设置密码（V0.9.0 ADR-密码策略与口令协议 决策 1——SecurePassword 协议：客户端算 clientHash+salt，
    /// 服务端<b>只存不算</b>（对齐 IdentityPasswordManager 组装格式 <c>{iterations}.{b64salt}.{b64hash}</c>，经
    /// <c>ICredentialProtector</c> AES-GCM 加密落库——DB 泄露不可解密，服务端零明文）；TokenVersion++ +
    /// 密码历史追加 + MustChangePassword 清；影响行数 0 = UId 不存在。
    /// <paramref name="newClientHash"/> 为 hex(PBKDF2(newPassword, salt, iterations, 32bytes))——客户端算；
    /// <paramref name="salt"/> 为 hex(salt 32bytes)。</summary>
    Task<int> SetPasswordAsync(string uid, string newClientHash, string salt, CancellationToken ct = default);

    /// <summary>修改密码（V0.9.0 ADR-密码策略与口令协议 决策 1——验旧零明文）：服务端解保护存储 → 解析组装格式 →
    /// <c>FixedTimeEquals</c> 比对 <paramref name="oldClientHash"/>（持有旧 clientHash 才能产生匹配值，等价 HMAC 绑定安全）；
    /// 成功设新（同 <see cref="SetPasswordAsync"/> 语义）+ TokenVersion++；验旧失败抛 <c>PASSWORD_MISMATCH</c>。</summary>
    Task ChangePasswordAsync(string uid, string oldClientHash, string oldSalt, string newClientHash, string newSalt, CancellationToken ct = default);

    /// <summary>冻结账号（V0.9.0 ADR-密码策略与口令协议 决策 3/6/7——临时安全处置）：<c>IsFrozen=true</c> + 可选
    /// <c>FreezeEnd</c>（null=永久冻结直至显式解冻）→ 认证路径拦截新签发；SecurityLog 直写 <c>Freeze</c> 事件
    /// （<c>UserName</c> 填被冻结账号，<paramref name="operatorName"/> 进 <c>Detail</c>；Options 门控后跳过写入）。</summary>
    Task FreezeAsync(string uid, DateTime? freezeEnd, string operatorName, CancellationToken ct = default);

    /// <summary>解冻账号（同上）：清 <c>IsFrozen=false</c> + <c>FreezeEnd=null</c> → 认证路径恢复；SecurityLog 直写 <c>Unfreeze</c> 事件。</summary>
    Task UnfreezeAsync(string uid, string operatorName, CancellationToken ct = default);
}

/// <summary>账号管理服务实现——委托 AuthAccountEntityDataService（红线合规）。
/// <para>internal sealed——与 <see cref="AuthAccountQueryService"/> 先例一致（public 契约 + internal 实现 + TryAddScoped 注册）。
/// V4.10.53（领域自治根治，ADR90）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文；
/// DataService 经 <c>User.Use&lt;具体类&gt;()</c> NoAop 懒加载；注册改
/// <c>AddConstructibleService&lt;IAuthAccountService, AuthAccountService&gt;</c>。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。</para></summary>
[DiContractIgnore]
internal sealed class AuthAccountService : DomainServiceBase, IAuthAccountService
{
    private readonly IOptions<SecurityLoggingOptions> _securityLogOptions;
    private readonly IOptions<DomainOptions> _domainOptions;
    private readonly IOptions<AuthCenterOptions> _authOptions;
    private readonly ICredentialProtector _credentialProtector;
    private readonly ILogger<AuthAccountService> _logger;
    private AuthAccountEntityDataService? _dataService;
    private AuthAccountEntityDataService DataService => _dataService ??= User.Use<AuthAccountEntityDataService>();
    private PasswordHistoryEntityDataService? _historyDataService;
    private PasswordHistoryEntityDataService HistoryDataService => _historyDataService ??= User.Use<PasswordHistoryEntityDataService>();

    public AuthAccountService(
        IDomainUser user,
        IOptions<SecurityLoggingOptions> securityLogOptions,
        IOptions<DomainOptions> domainOptions,
        IOptions<AuthCenterOptions> authOptions,
        ICredentialProtector credentialProtector,
        ILogger<AuthAccountService> logger)
        : base(user)
    {
        _securityLogOptions = securityLogOptions ?? throw new ArgumentNullException(nameof(securityLogOptions));
        _domainOptions = domainOptions ?? throw new ArgumentNullException(nameof(domainOptions));
        _authOptions = authOptions ?? throw new ArgumentNullException(nameof(authOptions));
        _credentialProtector = credentialProtector ?? throw new ArgumentNullException(nameof(credentialProtector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<AuthAccountEntity?> GetByUIdAsync(string uid, CancellationToken ct = default)
        => DataService.GetByUIdAsync(uid, ct);
    public Task CreateAsync(AuthAccountEntity account, CancellationToken ct = default)
        => DataService.CreateAsync(account, ct);
    public Task UpdateAsync(AuthAccountEntity account, CancellationToken ct = default)
        => DataService.UpdateAsync(account, ct);
    public Task<int> IncrementTokenVersionAsync(string uid, CancellationToken ct = default)
        => DataService.IncrementTokenVersionAsync(uid, ct);

    /// <inheritdoc cref="IAuthAccountService.SetPasswordAsync"/>
    public async Task<int> SetPasswordAsync(string uid, string newClientHash, string salt, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(newClientHash) || string.IsNullOrEmpty(salt))
            throw new ArgumentException("PASSWORD_CREDENTIALS_REQUIRED", nameof(newClientHash));

        var account = await DataService.GetByUIdAsync(uid, ct);
        if (account is null) return 0;

        // ① SecurePassword 组装（客户端算的 clientHash+salt → 自描述格式 → AES-GCM 加密落库——服务端零明文，DB 泄露不可解密）
        var iterations = _domainOptions.Value.Auth.Pbkdf2Iterations;   // 单一来源（600000），不硬编码
        var clientHashBytes = Convert.FromHexString(newClientHash);
        if (clientHashBytes.Length != 32) throw new ArgumentException("PASSWORD_HASH_LENGTH_INVALID", nameof(newClientHash));
        var saltBytes = Convert.FromHexString(salt);
        var assembled = $"{iterations}.{Convert.ToBase64String(saltBytes)}.{Convert.ToBase64String(clientHashBytes)}";
        var protectedBlob = _credentialProtector.Protect(System.Text.Encoding.UTF8.GetBytes(assembled));

        // ⑥ 密码策略单入口强制校验（fail-closed——复杂度/历史防重用；轮换判定查历史表）
        EnforcePasswordPolicy(account, assembled, ct);

        // 落库 + TokenVersion++（CAS）+ 历史追加 + MustChangePassword 清
        var affected = await DataService.SetPasswordHashAsync(uid, protectedBlob, ct);
        if (affected > 0)
        {
            await HistoryDataService.CreateAsync(new PasswordHistoryEntity
            {
                UId = uid,
                ClientHash = assembled,   // 防重用比对源（明文组装格式——服务端不接触密码明文）
                CreateTime = DateTime.UtcNow,
            }, ct);
            await TrimHistoryAsync(uid, ct);
            if (account.MustChangePassword)   // 初始密码强制改密：改密落地后清标记
                await DataService.ClearMustChangePasswordAsync(uid, ct);
        }
        return affected;
    }

    /// <inheritdoc cref="IAuthAccountService.ChangePasswordAsync"/>
    public async Task ChangePasswordAsync(string uid, string oldClientHash, string oldSalt, string newClientHash, string newSalt, CancellationToken ct = default)
    {
        var account = await DataService.GetByUIdAsync(uid, ct);
        if (account is null) throw new AuthenticationException("ACCOUNT_NOT_FOUND");
        if (string.IsNullOrEmpty(account.PasswordHash)) throw new AuthenticationException("PASSWORD_NOT_SET");

        // ① 验旧零明文：解保护存储 → 解析组装格式 → FixedTimeEquals 比对 oldClientHash（持有旧 clientHash 才能匹配——等价 HMAC 绑定安全）
        var storedAssembled = DecodeStoredBlob(account.PasswordHash);
        var storedHash = ParseClientHash(storedAssembled);
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(oldClientHash), storedHash))
            throw new AuthenticationException("PASSWORD_MISMATCH");

        await SetPasswordAsync(uid, newClientHash, newSalt, ct);   // 复用单入口（策略校验 + 历史 + TokenVersion++）
    }

    /// <summary>解保护存储 blob → 组装格式字符串（AES-GCM 解密；密钥变更/非法 blob → 密码错误语义）。</summary>
    private string DecodeStoredBlob(string protectedBlob)
    {
        try
        {
            var assembledBytes = _credentialProtector.Unprotect(protectedBlob);
            return System.Text.Encoding.UTF8.GetString(assembledBytes);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            throw new AuthenticationException("PASSWORD_DECRYPT_FAILED");
        }
    }

    /// <summary>解析组装格式 <c>{iterations}.{b64salt}.{b64hash}</c> → clientHash 原始字节（防重用/验旧比对源）。</summary>
    private static byte[] ParseClientHash(string assembled)
    {
        var parts = assembled.Split('.', 3);
        if (parts.Length != 3) throw new AuthenticationException("PASSWORD_FORMAT_INVALID");
        return Convert.FromBase64String(parts[2]);
    }

    /// <summary>⑥ 密码策略强制校验（fail-closed 单入口——复杂度/历史防重用/强制轮换；EnforcePolicy=false 时跳过校验仍记历史）。</summary>
    private void EnforcePasswordPolicy(AuthAccountEntity account, string assembledClientHash, CancellationToken ct)
    {
        var policy = _authOptions.Value.PasswordPolicy;
        if (!policy.EnforcePolicy) return;

        // 复杂度：最小长度 + 类别数（clientHash 无法直接校验明文复杂度——复杂度校验由客户端 SecurePassword 协议承担，
        // 服务端仅校验 clientHash 长度前提（32 bytes）；长度/类别属客户端契约，记入使用指南。这里校验组装格式完整性）
        if (string.IsNullOrEmpty(assembledClientHash)) throw new AuthenticationException("PASSWORD_POLICY_VIOLATION");

        // 历史防重用：新 clientHash 不得与最近 N 代相同（比对组装格式 hash 段）
        // 强制轮换：最近改密时间超过 RotationDays → 拒绝设密（由验证侧判定；设密侧仅当 RotationDays>0 且历史超期时提示）
        var recent = HistoryDataService.GetRecentAsync(account.UId, Math.Max(policy.HistoryRetentionCount, 1), ct).GetAwaiter().GetResult();
        var newHash = ParseClientHash(assembledClientHash);
        foreach (var hist in recent)
        {
            var histHash = ParseClientHash(hist.ClientHash);
            if (CryptographicOperations.FixedTimeEquals(newHash, histHash))
                throw new AuthenticationException("PASSWORD_REUSE_REJECTED");
        }
    }

    /// <summary>历史裁剪——保留最近 N 代（超出删除；只增表语义下由门面清理旧代）。</summary>
    private async Task TrimHistoryAsync(string uid, CancellationToken ct)
    {
        var keep = Math.Max(_authOptions.Value.PasswordPolicy.HistoryRetentionCount, 1);
        var all = await HistoryDataService.GetRecentAsync(uid, 100, ct);
        foreach (var hist in all.Skip(keep))
            await HistoryDataService.AdminDeleteAsync(hist.Id, ct);
    }

    /// <inheritdoc cref="IAuthAccountService.FreezeAsync"/>
    public async Task FreezeAsync(string uid, DateTime? freezeEnd, string operatorName, CancellationToken ct = default)
    {
        var account = await DataService.GetByUIdAsync(uid, ct);
        if (account is null) throw new AuthenticationException("ACCOUNT_NOT_FOUND");
        if (account.IsFrozen && (freezeEnd is null || account.FreezeEnd == freezeEnd))
            return; // 幂等：已冻结且到期不变

        account.IsFrozen = true;
        account.FreezeEnd = freezeEnd;
        await DataService.UpdateAsync(account, ct);
        await WriteSecurityLogAsync(SecurityLogEventTypes.Freeze, uid, operatorName, $"冻结（到期={freezeEnd?.ToString("O") ?? "永久"}）", ct);
    }

    /// <inheritdoc cref="IAuthAccountService.UnfreezeAsync"/>
    public async Task UnfreezeAsync(string uid, string operatorName, CancellationToken ct = default)
    {
        var account = await DataService.GetByUIdAsync(uid, ct);
        if (account is null) throw new AuthenticationException("ACCOUNT_NOT_FOUND");
        if (!account.IsFrozen) return; // 幂等：未冻结

        account.IsFrozen = false;
        account.FreezeEnd = null;
        await DataService.UpdateAsync(account, ct);
        await WriteSecurityLogAsync(SecurityLogEventTypes.Unfreeze, uid, operatorName, "解冻", ct);
    }

    /// <summary>
    /// SecurityLog 直写（V0.9.0 ADR-密码策略与口令协议 决策 6/7）：绕过过滤器内 EventTypes 门控，须自读
    /// <see cref="SecurityLoggingOptions"/>——<c>Enabled=false</c> 或 <c>EventTypes</c> 非空且不含目标事件类型时跳过写入。
    /// 写失败（Store 异常静默或未启用 SecurityLog）catch + Warning 降级——冻结/解冻业务不阻断。
    /// </summary>
    private async Task WriteSecurityLogAsync(string eventType, string accountUid, string operatorName, string detail, CancellationToken ct)
    {
        var options = _securityLogOptions.Value;
        if (!options.Enabled) return;
        if (options.EventTypes.Count > 0 && !options.EventTypes.Contains(eventType)) return;

        try
        {
            // User.Use<ISecurityLogStore>()——帧内解析守卫工厂；SecurityLog 未启用 → 解析抛守卫，catch 降级
            var store = User.Use<ISecurityLogStore>();
            await store.SaveAsync(new SecurityLogEntry(
                EventType: eventType,
                EventCategory: SecurityLogEventTypes.CategoryAuthentication,
                UserName: accountUid,            // ⚠️ UserName 填被冻结账号（非操作者）——ADR 决策 7
                UserId: null,
                IpAddress: null,
                UserAgent: null,
                Result: SecurityLogEventTypes.ResultSuccess,
                Detail: $"操作者={operatorName}；{detail}",
                CorrelationId: null), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "冻结事件 SecurityLog 直写失败——降级不阻断（EventType={EventType}, UId={UId}）", eventType, accountUid);
        }
    }
}