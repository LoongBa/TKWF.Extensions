using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;
using TKWF.Ext.Federation.DTOs;

namespace TKWF.Ext.Federation;

/// <summary>数据服务：&#x901A;&#x9053;&#x6CE8;&#x518C;&#x8868;&#x5B9E;&#x4F53;&#xFF08;&#x591A;&#x901A;&#x9053;&#x8054;&#x90A6; Phase 2&#x2014;&#x2014;DB &#x52A8;&#x6001;&#x6743;&#x5A01;&#x5C42;&#xFF0C;&#x65B9;&#x6848; docs/Federation/&#x591A;&#x901A;&#x9053;&#x8054;&#x90A6;-&#x5F00;&#x53D1;&#x65B9;&#x6848;.md &#xA7;3.3&#xFF09;&#x3002;      &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#xFF08;&#x4E0D;&#x6307;&#x5B9A; UserType&#x2014;&#x2014;ADR42 D4&#xFF0C;&#x4F9B;&#x4EFB;&#x610F;&#x88C5;&#x914D;&#x5B9E;&#x4F8B;&#xFF09;&#x3002;&lt;/para&gt;      &lt;para&gt;&#x51ED;&#x8BC1;&#x6A21;&#x578B;&#xFF08;&#x65B9;&#x6848; &#xA7;3.1 &#x524D;&#x77BB;&#x6838;&#x5FC3;&#xFF09;&#xFF1A;&#x516C;&#x5171;&#x5217;&#xFF08;&lt;see cref=&quot;ChannelId&quot;/&gt;/&lt;see cref=&quot;PlatformType&quot;/&gt;/&lt;see cref=&quot;AppId&quot;/&gt;&#x2B;      &lt;see cref=&quot;AppSecretEncrypted&quot;/&gt;&#xFF09;&#x2B; &#x6269;&#x5C55; JSON &#x5BC6;&#x6587;&#x5217;&#xFF08;&lt;see cref=&quot;ExtraJsonEncrypted&quot;/&gt;&#x2014;&#x2014;&#x5E73;&#x53F0;&#x7279;&#x5B9A;&#x8D1F;&#x8F7D; AES-GCM      &#x6574;&#x6BB5;&#x52A0;&#x5BC6;&#xFF1A;WeChat{Token,EncodingAESKey} / OIDC{TokenIssuers,DiscoveryUri} / &#x652F;&#x4ED8;&#x5B9D;&#xFF08;&#x672A;&#x6765;&#xFF09;{AppPrivateKey}&#x2014;&#x2014;      &#x672A;&#x6765;&#x5E73;&#x53F0;&#x96F6;&#x8868;&#x7ED3;&#x6784;&#x53D8;&#x66F4;&#xFF09;&#x3002;&#x516C;&#x5171;&#x5217; &lt;see cref=&quot;AppId&quot;/&gt; &#x4EC5;&#x627F;&#x8F7D; OAuth &#x63E1;&#x624B;&#x5E94;&#x7528;&#x6807;&#x8BC6;&#xFF08;Oracle M7 &#x6620;&#x5C04;&#x89C4;&#x5219;&#x7EA6;&#x675F;&#xFF09;&#xFF0C;      &#x6CD5;&#x4EBA;&#x7EA7;&#x6807;&#x8BC6;&#xFF08;CorpId/OrgId/AgentId &#x7C7B;&#xFF09;&#x4E00;&#x5F8B;&#x8FDB; ExtraJson&#x3002;&lt;/para&gt;      &lt;para&gt;&#x52A0;&#x5BC6;&#x8BED;&#x4E49;&#xFF08;&#x65B9;&#x6848; M6/M12&#xFF09;&#xFF1A;&lt;see cref=&quot;AppSecretEncrypted&quot;/&gt; &#x4E0E; &lt;see cref=&quot;ExtraJsonEncrypted&quot;/&gt; &#x5747;      AES-GCM &#x5BC6;&#x6587;&#x5217;&#xFF08;keyed &lt;c&gt;ISymmetricKeyProvider &quot;Federation&quot;&lt;/c&gt;&#xFF09;&#xFF0C;&#x670D;&#x52A1;&#x5C42;&#x52A0;&#x89E3;&#x5BC6;&#xFF08;&lt;c&gt;SsoChannelRegistryService&lt;/c&gt;&#xFF09;&#x2014;&#x2014;      &#x751F;&#x4EA7;&#x7528; DB &#x5C42;&#x5168;&#x7A0B;&#x5BC6;&#x6587;&#xFF0C;&#x660E;&#x6587;&#x4E0D;&#x843D;&#x5E93;&#xFF08;&#x9759;&#x6001; Options &#x660E;&#x6587;&#x4EC5;&#x9650;&#x5F00;&#x53D1;&#x6001;&#xFF0C;&#x65B9;&#x6848; M12&#xFF09;&#x3002;&lt;/para&gt;      &lt;para&gt;&#x5BB9;&#x91CF;&#x6838;&#x9A8C;&#xFF08;Oracle M6&#xFF09;&#xFF1A;&lt;see cref=&quot;ExtraJsonEncrypted&quot;/&gt;(4000) &#x8D77;&#x6B65;&#x2014;&#x2014;RSA-2048 PEM &#x79C1;&#x94A5;&#x7EA6; 1.7KB &#x2B;      OIDC DiscoveryUri&#x2B;TokenIssuers &#x7EC4;&#x5408; 1.5KB&#x2B; &#x52A0;&#x5BC6; base64 &#x81A8;&#x80C0; ~33%&#xFF08;P1 &#x5B9A;&#x6848;&#x9501;&#x5B9A;&#xFF0C;&#x907F;&#x514D; P2 &#x8FC1;&#x79FB;&#xFF09;&#xFF1B;      &lt;see cref=&quot;AppSecretEncrypted&quot;/&gt;(512) &#x627F;&#x8F7D;&#x5BF9;&#x79F0;&#x5BC6;&#x94A5;/AES-GCM &#x5BC6;&#x6587;&#x8DB3;&#x91CF;&#x3002;&lt;/para&gt;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 SsoChannelRegistryEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class SsoChannelRegistryEntityDataService(IDomainUser user, IEntityDAC<SsoChannelRegistryEntity> dac)
        : DomainDataServiceBase<SsoChannelRegistryEntity, SsoChannelRegistryEntityDto>(user, dac, hasSoftDelete:false) 
{
     // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
     public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
     {
         // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
         return await DeleteAsync(id, ct);
     }
}