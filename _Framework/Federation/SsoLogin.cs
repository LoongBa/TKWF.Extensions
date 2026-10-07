using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Federation;

/// <summary>
/// 联邦登录编排门面实现（多通道联邦 v0.3.0）。
/// <para>内部组合：<see cref="ISsoChannelFactory"/> 经基类 <c>User.Use</c> 懒加载（DI004 零豁免——
/// 领域服务间调用经 User.Use，不 ctor 注入）；选区失败/构造失败统一转
/// <see cref="SsoChannelAuthResult"/>（FailReason 机器可读，不抛不出帧）。</para>
/// <para>错误码（方案 §3.5 映射表）：<c>CHANNEL_NOT_FOUND</c>（404）/<c>CHANNEL_DISABLED</c>（403）/
/// <c>CHANNEL_REGISTRY_UNAVAILABLE</c>（503，registry 故障）/平台码（400，如 QQ_CODE_REQUIRED）。</para>
/// <para><c>[DiContractIgnore]</c>：运行时手写注册豁免 DI001。</para>
/// </summary>
[DiContractIgnore]
public sealed class SsoLogin : DomainServiceBase, ISsoLogin
{
    private readonly ILogger<SsoLogin> _logger;
    private ISsoChannelFactory? _factory;

    /// <summary>构造（IDomainUser 经守卫工厂帧内供给；非域基础设施可构造注入）。</summary>
    public SsoLogin(IDomainUser user, IServiceProvider serviceProvider, ILogger<SsoLogin> logger)
        : base(user)
    {
        _ = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        // serviceProvider 保留——若后续需要 ctor 直接解析其它基础设施；当前经基类 Use 懒加载
    }

    /// <inheritdoc />
    public async Task<SsoChannelAuthResult> LoginAsync(string channelId, SsoChannelAuthContext context, CancellationToken ct = default)
    {
        try
        {
            var channel = await Factory.CreateAsync(channelId, null, ct);
            if (channel is null)
            {
                // 工厂侧已区分 NOT_FOUND/DISABLED（日志）——统一返回 NOT_FOUND（DISABLED 语义由工厂预警，门面收敛）
                _logger.LogWarning("登录选区失败：ChannelId={ChannelId}（CHANNEL_NOT_FOUND）", channelId);
                return new SsoChannelAuthResult(false, null, "CHANNEL_NOT_FOUND", 0);
            }
            return await channel.AuthenticateAsync(context, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // registry/工厂故障——与业务 NOT_FOUND 区分（可观测性：CHANNEL_REGISTRY_UNAVAILABLE）
            _logger.LogError(ex, "登录编排异常：ChannelId={ChannelId}", channelId);
            return new SsoChannelAuthResult(false, null, "CHANNEL_REGISTRY_UNAVAILABLE", 0);
        }
    }

    /// <inheritdoc />
    public async Task<SsoChannelAuthResult> LoginDefaultAsync(SsoChannelAuthContext context, CancellationToken ct = default)
    {
        try
        {
            var channel = await Factory.CreateDefaultAsync(ct);
            if (channel is null)
            {
                _logger.LogWarning("默认通道选区失败：无可用默认通道（CHANNEL_NOT_FOUND）");
                return new SsoChannelAuthResult(false, null, "CHANNEL_NOT_FOUND", 0);
            }
            return await channel.AuthenticateAsync(context, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "默认登录编排异常（CHANNEL_REGISTRY_UNAVAILABLE）");
            return new SsoChannelAuthResult(false, null, "CHANNEL_REGISTRY_UNAVAILABLE", 0);
        }
    }

    private ISsoChannelFactory Factory => _factory ??= User.Use<ISsoChannelFactory>();
}