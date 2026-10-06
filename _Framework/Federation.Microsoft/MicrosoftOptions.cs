using System.Collections.Generic;

namespace TKWF.Federation.Microsoft;

/// <summary>
/// Microsoft Entra ID 通道配置（M2 方案：pairwise sub + tenant 配置化）。
/// <para>**一 ChannelId = 一 tenant 实例**（Oracle P1-7——消费方按 ChannelId 区分多 Microsoft 通道；
/// tenant 是访问控制维度不影响 pairwise sub 派生，channel_id 不含 tenant 正确——N2 §3.3 定案语义）。</para>
/// </summary>
public sealed class MicrosoftChannelConfig
{
    /// <summary>channel 实例 id（pairwise 复合编码——channel_id 由 <c>BuildChannelId</c> 自动拼接 `microsoft_oidc:{client_id}`，Oracle P1-2）。</summary>
    public string ChannelId { get; set; } = "";

    /// <summary>Microsoft Entra App client_id（pairwise sub 派生维度——不同 client_id 不同 sub）。</summary>
    public string ClientId { get; set; } = "";

    /// <summary>App client_secret（凭证自持——Oracle P2-4；private_key_jwt 证书认证归后续迭代 P2-1）。</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>tenant 受众：common（任意 AAD+MSA）/consumers（仅 MSA）/organizations（仅 AAD 工作学校）/租户 ID（精确）。</summary>
    public string Tenant { get; set; } = "common";

    /// <summary>TokenIssuer 白名单（默认 login.microsoftonline.com 通配——common tenant iss 含实际租户 GUID，M2-P1-1）。</summary>
    public List<string> TokenIssuers { get; set; } = [];
}

/// <summary>
/// Microsoft 平台网关库配置选项（<c>TKWF:Federation:Microsoft</c> 节——Channels 列表，每 ChannelId 一 tenant 实例）。
/// </summary>
public sealed class MicrosoftOptions
{
    /// <summary>channel 配置列表（一 ChannelId = 一 tenant 实例——Channels[0] 默认选区）。</summary>
    public List<MicrosoftChannelConfig> Channels { get; set; } = [];
}