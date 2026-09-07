using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TKW.Framework.Core.Hosting;
using TKWF.Ext.Account;

namespace TKWF.Ext.Identity;

/// <summary>
/// Identity 密码落地适配器（V0.3.0）——Account 密码重置流程的 <see cref="IAccountPasswordManager"/> 开箱实现，
/// 消费方启用 Identity + Account 后**零手写**获得密码重置落地（重置码验证后新密码写入 Identity 用户表）。
/// <para>组装方案（Oracle 调研实证，2026-09-07）：SecurePassword 语义的 newClientHash(hex) + salt(hex)
/// 组装为 PasswordHasher 格式 <c>"{iterations}.{base64salt}.{base64hash}"</c>——登录走标准 Password 模式
/// （<c>VerifyCredentialsAsync</c> 明文验证，<c>PasswordHasher.VerifyPassword</c> 解析格式内嵌迭代重算比对）。
/// 迭代次数从 <c>IOptions&lt;DomainOptions&gt;.Value.Auth.Pbkdf2Iterations</c> 注入（与框架 RequestChallenge
/// 下发客户端单一来源，用户裁定可配置——不可硬编码否则登录必失败）。</para>
/// <para>keysize 前提（已实证）：客户端 PBKDF2 输出 = 32 bytes（ts-client crypto.ts L42-46 <c>deriveBits(..., 256)</c>），
/// 与服务端 PasswordHasher.KeySize=32 一致；适配器校验 hash 长度，不符返回 false。</para>
/// </summary>
public sealed class IdentityPasswordManager : IAccountPasswordManager
{
    private readonly IUserManager _userManager;
    private readonly int _iterations;

    public IdentityPasswordManager(IUserManager userManager, IOptions<DomainOptions> options)
    {
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _iterations = options?.Value.Auth.Pbkdf2Iterations ?? 600000;
    }

    /// <summary>用户是否存在（防用户枚举）。</summary>
    public async Task<bool> UserExistsAsync(string userName, CancellationToken ct = default)
        => await _userManager.FindByNameAsync(userName, ct) is not null;

    /// <summary>设置用户密码——SecurePassword 语义 clientHash+salt 组装为 PasswordHasher 格式直存。</summary>
    public async Task<bool> SetPasswordAsync(string userName, string newClientHash, string salt, CancellationToken ct = default)
    {
        var user = await _userManager.FindByNameAsync(userName, ct);
        if (user is null) return false;
        try
        {
            var saltBytes = Convert.FromHexString(salt);
            var hashBytes = Convert.FromHexString(newClientHash);
            if (hashBytes.Length != 32) return false;      // 组装前提：客户端 PBKDF2 keysize = 32 bytes（PasswordHasher.KeySize）
            user.PasswordHash = $"{_iterations}.{Convert.ToBase64String(saltBytes)}.{Convert.ToBase64String(hashBytes)}";
            user.UpdateTime = DateTimeOffset.Now;
            await _userManager.UpdateUserAsync(user, ct);
            return true;
        }
        catch (FormatException) { return false; }              // hex 解析失败（非 hex 字符/奇数长度）
        catch (ArgumentNullException) { return false; }        // null/空输入（P2-4）
    }
}
