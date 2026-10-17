using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.WeChatOperations;
using TKWF.Ext.WeChatOperations.DTOs;

namespace TKWF.Ext.WeChatOperations;

/// <summary>数据服务：&#x4E0B;&#x6E38;&#x8F6C;&#x53D1;&#x7AEF;&#x70B9;&#x5B9E;&#x4F53;&#xFF08;&#x8868; &lt;c&gt;TKWF_WeChatDownstreamEndpoint&lt;/c&gt;&#xFF09;&#x2014;&#x2014;&#x7F51;&#x5173;&#x6D88;&#x606F;&#x8F6C;&#x53D1;&#x4E0B;&#x6E38;&#x7AEF;&#x70B9;&#xFF08;HTTP &#x51FA;&#x7AD9;&#x76EE;&#x6807;&#xFF0C;V0.2.0 &#x589E;&#x7F51;&#x5173;&#x6A21;&#x5757;&#xFF09;&#x3002;      &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#x3002;&lt;/para&gt;      &lt;para&gt;&lt;see cref=&quot;BaseUrl&quot;/&gt; &#x2B; &lt;see cref=&quot;Path&quot;/&gt;&#xFF08;&#x53EF;&#x7A7A;&#xFF09;&#x62FC;&#x63A5;&#x51FA;&#x7AD9;&#x76EE;&#x6807;&#xFF1B;&lt;see cref=&quot;HttpMethod&quot;/&gt; &#x9ED8;&#x8BA4; &quot;POST&quot;&#xFF08;&#x5FAE;&#x4FE1;&#x88AB;&#x52A8;&#x56DE;&#x590D;/&#x8F6C;&#x53D1;&#x8BED;&#x4E49;&#xFF09;&#x3002;&lt;/para&gt;      &lt;para&gt;&lt;see cref=&quot;AuthType&quot;/&gt;&#xFF08;int&#xFF09;&#xFF1A;0=None &#x65E0;&#x9274;&#x6743; / 1=Bearer &#x5934;&#x643A;&#x5E26; / 2=Header &#x81EA;&#x5B9A;&#x4E49;&#x5934;&#x643A;&#x5E26;&#x3002;      &lt;see cref=&quot;AuthTokenCipher&quot;/&gt;&#xFF1A;AES-GCM &#x5355;&#x6BB5;&#x5BC6;&#x6587;&#xFF08;base64(nonce&#x2016;cipher&#x2016;tag)&#xFF0C;&#x5BF9;&#x9F50; &lt;see cref=&quot;WeChatChannelConfigEntity&quot;/&gt; &#x51ED;&#x8BC1;&#x5BC6;&#x6587;&#x5217;&#xFF09;&#x2014;&#x2014;      &lt;b&gt;&#x660E;&#x6587;&#x6C38;&#x4E0D;&#x843D;&#x5E93;&lt;/b&gt;&#xFF1B;&#x89E3;&#x5BC6;&#x4EC5;&#x51FA;&#x7AD9;&#x77AC;&#x65F6;&#x3002;&lt;/para&gt;      &lt;para&gt;&#x552F;&#x4E00;&#x7D22;&#x5F15;&#xFF1A;&lt;see cref=&quot;ChannelId&quot;/&gt; &#x2B; &lt;see cref=&quot;Name&quot;/&gt;&#xFF08;channel &#x5185;&#x7AEF;&#x70B9;&#x540D;&#x552F;&#x4E00;&#x2014;&#x2014;&#x6309;&#x540D;&#x9009;&#x533A;/&#x53BB;&#x91CD;&#xFF09;&#x3002;&lt;/para&gt;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 DownstreamEndpointEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class DownstreamEndpointEntityDataService(IDomainUser user, IEntityDAC<DownstreamEndpointEntity> dac)
        : DomainDataServiceBase<DownstreamEndpointEntity, DownstreamEndpointEntityDto>(user, dac, hasSoftDelete:false) 
{
    /// <summary>按 Id 取启用端点并<b>校验同 channel</b>（规则引用选区——跨 channel 引用拒绝；方案 §十·一 ④）。</summary>
    /// <param name="id">端点主键。</param>
    /// <param name="channelId">公众号隔离 id（须与端点归属一致——跨 channel 不可见）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>启用且同 channel 的端点；未命中/禁用/跨 channel → null。</returns>
    public async Task<DownstreamEndpointEntity?> GetEnabledEndpointByIdAsync(long id, string channelId, CancellationToken ct = default)
        => await EntityGetAsync(e => e.Id == id && e.ChannelId == channelId && e.IsEnabled, ct);

    /// <summary>按 channel 分页查询端点（管理端——Id 升序稳定序）。</summary>
    /// <param name="channelId">公众号隔离 id。</param>
    /// <param name="page">页码（1 起）。</param>
    /// <param name="size">每页条数。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<List<DownstreamEndpointEntity>> QueryEndpointsByChannelAsync(string channelId, int page, int size, CancellationToken ct = default)
        => await EntitySelectAsync(
            e => e.ChannelId == channelId,
            (page < 1 ? 1 : page - 1) * size, size,
            q => q.OrderBy(e => e.Id),
            ct);

    /// <summary>按 channel 端点总数（管理端分页计数）。</summary>
    /// <param name="channelId">公众号隔离 id。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<long> CountEndpointsByChannelAsync(string channelId, CancellationToken ct = default)
        => await CountAsync(e => e.ChannelId == channelId, ct);
}
