using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.WeChatOperations;
using TKWF.Ext.WeChatOperations.DTOs;

namespace TKWF.Ext.WeChatOperations;

/// <summary>数据服务：&#x83DC;&#x5355;&#x64CD;&#x4F5C;&#x5BA1;&#x8BA1;&#x5B9E;&#x4F53;&#xFF08;&#x8868; &lt;c&gt;TKWF_WeChatMenuPublishLog&lt;/c&gt;&#xFF09;&#x2014;&#x2014;&#x64CD;&#x4F5C;&#x5BA1;&#x8BA1;&#xFF08;who/when/action&#xFF0C;&lt;b&gt;&#x4E0D;&#x542B; JSON &#x5185;&#x5BB9;&lt;/b&gt;&#x2014;&#x2014;JSON &#x5F52; MenuVersion&#xFF09;&#x3002;      &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#x3002;&lt;/para&gt;      &lt;para&gt;&#x804C;&#x8D23;&#x5206;&#x79BB;&#xFF08;Oracle P1&#xFF09;&#xFF1A;&#x53D1;&#x5E03;&#x52A8;&#x4F5C; MenuVersion&#xFF08;&#x8BB0;&#x5185;&#x5BB9;&#xFF09;&#x2B; MenuPublishLog&#xFF08;&#x8BB0;&#x5BA1;&#x8BA1;&#xFF09;&#x540C;&#x65F6;&#x8BB0;&#x5F55;&#xFF1B;      &#x975E;&#x53D1;&#x5E03;&#x64CD;&#x4F5C;&#xFF08;&#x8BFB;&#x53D6;/&#x5220;&#x9664;/&#x6D4B;&#x8BD5;&#xFF09;&#x53EA;&#x8BB0;&#x672C;&#x8868;&#x3002;&lt;/para&gt;      &lt;para&gt;Action&#xFF08;int&#xFF09;&#xFF1A;1=Read&#xFF08;&#x62C9;&#x53D6;&#x5F53;&#x524D;&#x83DC;&#x5355;&#xFF09;/ 2=Create&#xFF08;&#x53D1;&#x5E03;&#xFF09;/ 3=Delete&#xFF08;&#x5220;&#x9664;&#xFF09;/ 4=Test&#xFF08;&#x8BD5;&#x53D1;&#x6D4B;&#x8BD5;&#xFF09;&#x3002;&lt;/para&gt;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 MenuPublishLogEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class MenuPublishLogEntityDataService(IDomainUser user, IEntityDAC<MenuPublishLogEntity> dac)
        : DomainDataServiceBase<MenuPublishLogEntity, MenuPublishLogEntityDto>(user, dac, hasSoftDelete:false) 
{
     // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
     public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
     {
         // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
         return await DeleteAsync(id, ct);
     }
}