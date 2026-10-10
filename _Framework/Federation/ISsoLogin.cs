using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.TrustCenter;

namespace TKWF.Ext.Federation;

/// <summary>
/// 联邦登录编排门面（多通道联邦 v0.3.0——装配层零编排终态）。
/// <para>职责：按 channelId 选区构造通道 → <c>AuthenticateAsync</c> 认证 → 返回
/// <see cref="SsoChannelAuthResult"/>（ExternalUserId/FailReason/AuthLevel 全透传）。装配层端点
/// （<c>/sso/oauth/{channelId}/callback</c>）只调本门面，不串多门面（Oracle P0-1 表现层零编排对齐）。</para>
/// <para>路由衔接（方案 §3.5）：<c>/sso/oauth/callback</c>（无前缀 → <c>LoginDefaultAsync</c> 降级默认通道）/
/// <c>/sso/oauth/{channelId}/callback</c>（带前缀 → <c>LoginAsync(channelId,...)</c> 精确选区）——
/// 单实例免前缀存量零破坏，多实例按段选区。错误码（CHANNEL_NOT_FOUND/CHANNEL_DISABLED/平台码）经
/// FailReason 机器可读 + 装配层 HTTP 映射，不静默。</para>
/// <para>注册：<c>AddConstructibleService&lt;ISsoLogin, SsoLogin&gt;()</c>——门面标准形态，消费方
/// <c>User.Use&lt;ISsoLogin&gt;()</c> 帧内解析（登录编排内部经基类 <c>User.Use</c> 懒加载工厂，DI004 零豁免）。</para>
/// <para><b>state 校验边界（N3 P1-3 对齐）</b>：本门面不感知 state——授权构造/state 生成/回调校验归装配层
/// （持有 HTTP 上下文）；门面只做 code→身份归一。</para>
/// </summary>
public interface ISsoLogin : IDomainService
{
    /// <summary>按 channelId 登录（指定通道——多实例精确选区）。</summary>
    Task<SsoChannelAuthResult> LoginAsync(string channelId, SsoChannelAuthContext context, CancellationToken ct = default);

    /// <summary>默认通道登录（无前缀降级——单实例兼容）。</summary>
    Task<SsoChannelAuthResult> LoginDefaultAsync(SsoChannelAuthContext context, CancellationToken ct = default);
}