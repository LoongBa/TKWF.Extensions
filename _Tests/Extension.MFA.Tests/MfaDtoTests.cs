using System;
using System.Reflection;
using TKWF.Ext.MFA.DTOs;

namespace TKWF.Ext.MFA.Tests;

/// <summary>
/// DTO 敏感字段裁剪测试——实体敏感列（TOTP secret 密文/EnrollToken 哈希/码哈希）必须经
/// <c>[DtoFieldIgnore]</c> + <c>[JsonIgnore]</c> 从 DTO 裁剪（tkwf-entity 规则 4，对齐
/// <c>PlatformCredentialEntity.AppSecretEncrypted</c> 先例）——防敏感字段经 CRUD/查询 API 外泄。
/// <para>断言方式：反射检查 DTO 类型属性（编译期裁剪后属性根本不存在——比运行时 null 断言更强，
/// 直接验证 .g.cs 生成物无敏感列）。</para>
/// </summary>
public class MfaDtoTests
{
    [Fact]
    public void MfaSecretEntityDto_SecretEncrypted_NotExposed()
    {
        Assert.Null(typeof(MfaSecretEntityDto).GetProperty("SecretEncrypted"));
        Assert.Null(typeof(MfaSecretEntityDto).GetProperty("EnrollTokenHash"));
    }

    [Fact]
    public void MfaChallengeEntityDto_CodeHash_NotExposed()
    {
        Assert.Null(typeof(MfaChallengeEntityDto).GetProperty("CodeHash"));
    }

    [Fact]
    public void MfaRecoveryCodeEntityDto_CodeHash_NotExposed()
    {
        Assert.Null(typeof(MfaRecoveryCodeEntityDto).GetProperty("CodeHash"));
    }

    [Fact]
    public void MfaSecretEntityDto_Phone_Exposed_ForBindingDisplay()
    {
        // 绑手机保留 DTO——消费方绑定管理 UI 展示需显示绑定手机号（对齐 AuthAccountEntityDto.Phone 先例）；
        // 手机号非密码学敏感（不构成验证器克隆/码爆破面）
        Assert.NotNull(typeof(MfaSecretEntityDto).GetProperty("Phone"));
    }

    [Fact]
    public void MfaSecretEntityDto_NonSensitiveColumns_Exposed()
    {
        // 非敏感列正常暴露（绑定管理展示：方法/激活态/过期时间）
        Assert.NotNull(typeof(MfaSecretEntityDto).GetProperty("Id"));
        Assert.NotNull(typeof(MfaSecretEntityDto).GetProperty("UserId"));
        Assert.NotNull(typeof(MfaSecretEntityDto).GetProperty("Method"));
        Assert.NotNull(typeof(MfaSecretEntityDto).GetProperty("IsConfirmed"));
        Assert.NotNull(typeof(MfaSecretEntityDto).GetProperty("EnrollExpireAt"));
    }
}
