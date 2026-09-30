using System;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.MFA;

/// <summary>
/// MFA 短信发送渠道抽象（消费方实现——TryAdd 语义，无默认）。
/// <para>零扩展间依赖裁定（ADR-MFA-独立扩展与零依赖边界）：MFA 不引 Authentication/不拆其 Abstractions——
/// 短信渠道经本抽象注入（消费方包装其 Emailing/自有短信），对齐 Account v0.1.0「验证码渠道归消费方」裁定。</para>
/// <para>⚠️ 生产未装配 → <see cref="MfaMockForbiddenException"/>（503 语义 fail-fast，对齐 <c>SmsMockForbiddenException</c>）。</para>
/// </summary>
public interface IMfaSmsSender
{
    /// <summary>发送短信（实现方负责具体渠道；失败抛异常由服务层补偿）。</summary>
    Task SendAsync(MfaSmsMessage message, CancellationToken ct = default);
}

/// <summary>短信消息。</summary>
public sealed record MfaSmsMessage(string Phone, string Content);

/// <summary>MFA 短信渠道未装配异常（503 语义——生产环境必须实现 <see cref="IMfaSmsSender"/>）。</summary>
public sealed class MfaMockForbiddenException : InvalidOperationException
{
    public MfaMockForbiddenException(string message) : base(message) { }
}
