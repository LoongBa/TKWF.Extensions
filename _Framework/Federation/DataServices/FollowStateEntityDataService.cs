using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;
using TKWF.Ext.Federation.DTOs;

namespace TKWF.Ext.Federation;

/// <summary>数据服务：&#x5173;&#x6CE8;&#x5173;&#x7CFB;&#x72B6;&#x6001;&#x805A;&#x5408;&#x6839;&#xFF08;I2 &#x6E20;&#x9053;&#x5173;&#x6CE8;&#x5173;&#x7CFB;&#x57DF;&#x2014;&#x2014;&#x300C;&#x5F53;&#x524D;&#x662F;&#x5426;&#x5173;&#x6CE8;&#x300D;&#x552F;&#x4E00;&#x6743;&#x5A01;&#xFF1B;&#x5355;&#x7528;&#x6237;&#x5355;&#x6E20;&#x9053;&#x4E00;&#x884C;&#xFF09;&#x3002;      &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#xFF08;&#x4E0D;&#x6307;&#x5B9A; UserType&#x2014;&#x2014;ADR42 D4&#xFF1B;&#x5BF9;&#x9F50;      &lt;see cref=&quot;SsoChannelRegistryEntity&quot;/&gt; &#x4F53;&#x4F8B;&#xFF09;&#x3002;&lt;/para&gt;      &lt;para&gt;&#x6392;&#x5E8F;&#x5B88;&#x536B;&#xFF08;&#x65B9;&#x6848; &#xA7;4.3 oracle6 C4&#x2014;&#x2014;&lt;b&gt;&#x4E0D;&#x53EF;&#x7528;&#x5143;&#x7EC4; &amp;gt; &#x8FD0;&#x7B97;&#x7B26;&lt;/b&gt;&#xFF0C;&#x4E14;&#x8BE5;&#x5F62;&#x5F0F;&#x65E0;&#x6CD5;&#x7FFB;&#x8BD1; SQL&#xFF0C;&#x5FC5;&#x987B;&#x4E24;&#x6BB5;&#x6BD4;&#x8F83;&#xFF09;&#xFF1A;      &lt;code&gt;      newer = evt.OccurredAt &amp;gt; state.LastEventAt           || (evt.OccurredAt == state.LastEventAt &amp;amp;&amp;amp; evt.ReceivedAt &amp;gt; state.LastReceivedAt);      &lt;/code&gt;      &#x4EC5;&#x5F53; &lt;c&gt;newer&lt;/c&gt; &#x65F6;&#x8986;&#x76D6;&#x72B6;&#x6001;&#x5B57;&#x6BB5;&#xFF1B;&#x4E8B;&#x4EF6;&#x884C;&#x65E0;&#x8BBA;&#x662F;&#x5426; newer &#x90FD;&#x843D;&#x5E93;&#xFF08;&#x4E8B;&#x5B9E;&#x4E0D;&#x53EF;&#x4E22;&#xFF09;&#x3002;&#x4E24;&#x5904;&#x5B9E;&#x73B0;&#x5FC5;&#x987B;&#x540C;&#x578B;&#xFF1A;      &#x2460; &lt;c&gt;FollowStateService.RecordAsync&lt;/c&gt; &#x7684; LINQ &lt;c&gt;Where&lt;/c&gt;&#xFF08;&#x6761;&#x4EF6;&#x66F4;&#x65B0; &lt;c&gt;EntityUpdateWhereAsync&lt;/c&gt;&#xFF09;&#xFF1B;      &#x2461; &#x66F4;&#x65B0;&#x524D;&#x7684;&#x5185;&#x5B58;&#x5224;&#x5B9A;&#xFF08;&#x8FD4;&#x56DE; 0 &#x884C;&#x65F6;&#x5224;&#x5B9A;&#x300C;&#x88AB;&#x6392;&#x5E8F;&#x5B88;&#x536B;&#x6321;&#x4E0B;&#x300D;&#x800C;&#x975E;&#x300C;&#x884C;&#x4E0D;&#x5B58;&#x5728;&#x300D;&#xFF09;&#x3002;&lt;/para&gt;      &lt;para&gt;&#x805A;&#x5408;&#x6839;&#x8BED;&#x4E49;&#xFF08;&#xA7;2.2/R8&#xFF09;&#xFF1A;&#x72B6;&#x6001;&#x662F;&lt;b&gt;&#x9886;&#x57DF;&#x4E8B;&#x5B9E;&lt;/b&gt;&#xFF08;&#x975E;&#x6D3E;&#x751F;&#x6295;&#x5F71;&#xFF09;&#x2014;&#x2014;&#x4E8B;&#x4EF6;&#x6D41;&#x91CD;&#x653E;&#x662F;&#x91CD;&#x5EFA;&#x624B;&#x6BB5;&#x975E;&#x5B9A;&#x4E49;&#xFF0C;Rebuild &#x53EA;&#x7528;&#x4E8E;      &#x6570;&#x636E;&#x4FEE;&#x590D;&#xFF1B;&#x4E0E; Iteration 1 D9&#x300C;&#x7981;&#x6D3E;&#x751F;&#x6295;&#x5F71;&#x5217;&#x300D;&#x5224;&#x636E;&#x533A;&#x5206;&#xFF08;&#x89C1; ADR&#xFF09;&#x3002;&lt;/para&gt;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 FollowStateEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class FollowStateEntityDataService(IDomainUser user, IEntityDAC<FollowStateEntity> dac)
        : DomainDataServiceBase<FollowStateEntity, FollowStateEntityDto>(user, dac, hasSoftDelete:false) 
{
     // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
     public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
     {
         // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
         return await DeleteAsync(id, ct);
     }
}