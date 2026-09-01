using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.Core.AuthController;

namespace TKWF.Ext.Account
{
    /// <summary>
    /// 密码重置流程默认实现——<see cref="IPasswordResetFlow"/>（主框架 V4.9.45 扩展点）。
    /// <para>发起重置生成随机码存库（<see cref="IPasswordResetStore"/>，FreeSql），
    /// 完成重置时校验码（存在/未用/未过期）并委托 <see cref="IAccountPasswordManager"/> 落地新密码。</para>
    /// <para>新密码采用 SecurePassword 契约：客户端已计算 PBKDF2（newClientHash + salt），服务端仅存储。</para>
    /// <para><see cref="IAccountPasswordManager"/> 经 <see cref="IServiceProvider"/> 延迟解析（消费方未注册时
    /// GetService 返回 null——重置不可用但不阻断 DI 激活）。</para>
    /// </summary>
    internal sealed class DefaultPasswordResetFlow : IPasswordResetFlow
    {
        private const string AllowedChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // 去混淆字符集

        private readonly IPasswordResetStore _store;
        private readonly IServiceProvider _serviceProvider;
        private readonly AccountOptions _options;
        private readonly ILogger<DefaultPasswordResetFlow> _logger;

        public DefaultPasswordResetFlow(
            IPasswordResetStore store,
            IServiceProvider serviceProvider,
            IOptions<AccountOptions> options,
            ILogger<DefaultPasswordResetFlow> logger)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _options = options?.Value ?? new AccountOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 发起重置请求（生成随机码存库，有效期 Options.ResetCodeValidityMinutes）。
        /// <para>用户不存在也返回 true（防用户枚举）。密码管理器未注册返回 false（重置不可用）。</para>
        /// </summary>
        public async Task<bool> InitiateResetAsync(string userName, CancellationToken ct = default)
        {
            var passwordManager = _serviceProvider.GetService<IAccountPasswordManager>();
            if (passwordManager == null)
            {
                _logger.LogWarning("密码重置不可用: IAccountPasswordManager 未注册");
                return false;
            }
            if (string.IsNullOrWhiteSpace(userName)) return false;

            try
            {
                // 防用户枚举：用户不存在也返回 true
                var exists = await passwordManager.UserExistsAsync(userName, ct);
                if (!exists) return true;

                var code = GenerateResetCode();
                await _store.SaveAsync(new PasswordResetCodeEntity
                {
                    UserName = userName,
                    ResetCode = code,
                    ExpireTime = DateTime.Now.AddMinutes(_options.ResetCodeValidityMinutes)
                }, ct);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "密码重置发起失败: UserName={UserName}", userName);
                return false;
            }
        }

        /// <summary>
        /// 验证重置码并设置新密码（SecurePassword 契约：客户端已计算 PBKDF2）。
        /// <para>码无效/已用/过期 → 失败；密码落地成功后标记已用（幂等消费）。</para>
        /// </summary>
        public async Task<ResetResult> CompleteResetAsync(
            string userName,
            string resetCode,
            string newClientHash,
            string salt,
            CancellationToken ct = default)
        {
            var passwordManager = _serviceProvider.GetService<IAccountPasswordManager>();
            if (passwordManager == null)
            {
                _logger.LogWarning("密码重置不可用: IAccountPasswordManager 未注册");
                return new ResetResult(false, "密码重置未启用");
            }
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(resetCode)) return new ResetResult(false, "参数无效");

            try
            {
                var record = await _store.GetAsync(userName, resetCode, ct);
                if (record == null || record.IsUsed || record.ExpireTime < DateTime.Now)
                    return new ResetResult(false, "无效或已过期的重置码");

                var ok = await passwordManager.SetPasswordAsync(userName, newClientHash, salt, ct);
                if (!ok) return new ResetResult(false, "密码更新失败");

                await _store.MarkUsedAsync(record.Id, ct); // 幂等消费
                return new ResetResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "密码重置完成失败: UserName={UserName}", userName);
                return new ResetResult(false, "密码重置失败");
            }
        }

        /// <summary>生成 8 位随机重置码（加密随机源）。</summary>
        private static string GenerateResetCode()
        {
            Span<byte> bytes = stackalloc byte[4];
            Span<char> chars = stackalloc char[8];
            for (var i = 0; i < chars.Length; i++)
            {
                RandomNumberGenerator.Fill(bytes);
                chars[i] = AllowedChars[(int)(BitConverter.ToUInt32(bytes) % (uint)AllowedChars.Length)];
            }
            return new string(chars);
        }
    }
}