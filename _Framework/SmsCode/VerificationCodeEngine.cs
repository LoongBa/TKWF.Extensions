using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.SmsCode
{
    /// <summary>
    /// 短信验证码场景常量（v1 闭集：login——DMP 所需）。
    /// <para>扩场景 = 加条目不加引擎（场景注册表驱动，b+ 共识）；未知场景 fail-closed（UnknownScene）。</para>
    /// </summary>
    public static class SmsScenes
    {
        /// <summary>登录。</summary>
        public const string Login = "login";
    }

    /// <summary>
    /// 验证码引擎原语（internal，非 public API）——码生成 + SHA256 散列 + 恒定时间比对 + 单次消费 + attempt-cap。
    /// <para><b>b+ 共识</b>：按"未来统一验证核心的胚胎"写（场景注册表驱动、零 DMP 逻辑、Subject 不透明键控——X' 冻结项 1）；
    /// 不提取框架（3 消费者未全到）、不引用 AuthCenter（避免耦合）；MFA/AuthCenter 迁移由 X' 双轨触发器驱动。</para>
    /// <para><b>X' 裁定实证漂移修复方向</b>：SHA256 小写 hex + <see cref="CryptographicOperations.FixedTimeEquals"/> 恒定时间比对
    /// （统一 MFA 先例——AuthCenter 用 <c>string.Equals</c>+大写 hex 为漂移实证，新引擎直接采用正确形态）。</para>
    /// </summary>
    internal static class VerificationCodeEngine
    {
        /// <summary>生成 N 位数字验证码（加密安全随机，禁 System.Random）。</summary>
        /// <param name="length">位数（默认 6；≤ 9——超出 int 上界抛 <see cref="ArgumentOutOfRangeException"/>，防 Pow 溢出）。</param>
        public static string GenerateCode(int length = 6)
        {
            // 审核 P2 修复：Math.Pow(10, length) 在 length≥10 时超出 int.MaxValue（2,147,483,647）→ 显式拒绝
            if (length is < 1 or > 9)
                throw new ArgumentOutOfRangeException(nameof(length), "验证码位数须在 1-9 之间（10⁹ 超出 int 上界）");

            var code = RandomNumberGenerator.GetInt32((int)Math.Pow(10, length)).ToString("D" + length);

            // 边界防御：GetInt32 上限可能恰好落在 10^length 内——若生成位数不足（不走运的边界值），重试
            return code.Length == length ? code : GenerateCode(length);
        }

        /// <summary>SHA256 hex（小写）——验证码落库单向散列（统一 MFA 先例形态，X' 裁定）。</summary>
        public static string HashCode(string value)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>恒定时间比对（防时序侧信道）——码明文散列后与落库散列比对。</summary>
        /// <param name="code">用户输入明文码。</param>
        /// <param name="storedHash">落库 SHA256 hex（小写）。</param>
        public static bool VerifyHash(string code, string storedHash)
            => CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(HashCode(code)),
                Encoding.UTF8.GetBytes(storedHash));
    }
}