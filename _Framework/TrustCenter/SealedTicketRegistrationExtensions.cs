using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Ticket;

namespace TKWF.Ext.TrustCenter;

/// <summary>
/// 密封票据后端（DB CAS）泛型注册扩展——<c>ISealedTicketService&lt;T&gt;</c> 逐 T 闭合注册（D03 1.3/§5.2 修正）。
/// <para><b>背景（2026-10-30 架构裁定）</b>：TrustCenter 是纯信任内核（泛型中间人，零业务知识——不感知具体 T）；
/// <c>FederatedIdentity</c>（框架 Core 类型）由 TrustCenter 初始器直接注册；<c>FederatedTicketPayload</c>
/// （Federation.Abstractions 域类型）不在 TrustCenter 依赖面内——TrustCenter 不得反向引用 FA（TrustCenter ⟂
/// Federation 分层）。因此 payload 及未来任意 T 的注册经本扩展由<b>装配方</b>闭合（对齐平台库
/// <c>AddWeChatFederationChannels()</c> 装配先例——消费方一行，T 由调用方提供）。</para>
/// <para>用例（Federation 免登/签票域——消费方装配 TrustCenter 后补一行）：</para>
/// <code>
/// builder.Services.AddSealedTicketFor&lt;FederatedTicketPayload&gt;();   // D03 联邦免登券载荷
/// </code>
/// </summary>
public static class SealedTicketRegistrationExtensions
{
    /// <summary>
    /// 注册 <see cref="SealedTicketService{T}"/> DB CAS 后端（表 <c>TKWF_SealedTicket</c>——多 T 共用一张表、行独立）。
    /// <paramref name="ttl"/> 等行为配置归 <c>TKWF:TrustCenter</c> 节（SealedTicketExpirationSeconds/RetentionDays）。
    /// 幂等（内部 AddConstructibleService——TryAdd 语义，重复注册无害）。
    /// </summary>
    public static IServiceCollection AddSealedTicketFor<T>(this IServiceCollection services)
        where T : class
    {
        services.AddConstructibleService<ISealedTicketService<T>, SealedTicketService<T>>();
        return services;
    }
}