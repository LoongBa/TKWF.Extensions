using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.WeChatOperations;
using TKWF.Ext.WeChatOperations.DTOs;

namespace TKWF.Ext.WeChatOperations;

/// <summary>数据服务：&#x83DC;&#x5355;&#x7248;&#x672C;&#x5B9E;&#x4F53;&#xFF08;&#x8868; &lt;c&gt;TKWF_WeChatMenuVersion&lt;/c&gt;&#xFF09;&#x2014;&#x2014;&#x83DC;&#x5355;&#x53D1;&#x5E03;&#x5386;&#x53F2;&#xFF08;&#x5185;&#x5BB9;&#x7248;&#x672C;&#x2014;&#x2014;before/after JSON &#x2B; &#x53D1;&#x5E03;&#x4EBA;/&#x7ED3;&#x679C;&#xFF0C;&#x56DE;&#x6EDA;&#x6570;&#x636E;&#x6E90;&#xFF09;&#x3002;      &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#x3002;&lt;/para&gt;      &lt;para&gt;&#x804C;&#x8D23;&#x5206;&#x79BB;&#xFF08;Oracle P1&#xFF09;&#xFF1A;&lt;b&gt;&#x5185;&#x5BB9;&#x7248;&#x672C;&lt;/b&gt;&#xFF08;before/after &#x5B8C;&#x6574; JSON &#x2B; &#x53D1;&#x5E03;&#x5BA1;&#x8BA1;&#x4EBA;/&#x7ED3;&#x679C;&#xFF09;&#x5F52;&#x672C;&#x8868;&#xFF1B;      &#x64CD;&#x4F5C;&#x5BA1;&#x8BA1;&#xFF08;who/when/action&#xFF0C;&#x4E0D;&#x542B; JSON&#xFF09;&#x5F52; &lt;c&gt;MenuPublishLogEntity&lt;/c&gt;&#x2014;&#x2014;&#x53D1;&#x5E03;&#x52A8;&#x4F5C;&#x4E24;&#x8005;&#x540C;&#x65F6;&#x8BB0;&#x5F55;&#xFF0C;&#x975E;&#x53D1;&#x5E03;&#x64CD;&#x4F5C;&#x53EA;&#x8BB0; PublishLog&#x3002;&lt;/para&gt;      &lt;para&gt;Result&#xFF1A;1=Success / 2=Fail&#x2014;&#x2014;&#x56DE;&#x6EDA;&#x65F6;&#x52A0;&#x8F7D;&#x65E7;&#x7248;&#x672C; AfterJson &#x91CD;&#x65B0;&#x53D1;&#x5E03;&#x3002;&lt;/para&gt;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 MenuVersionEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class MenuVersionEntityDataService(IDomainUser user, IEntityDAC<MenuVersionEntity> dac)
        : DomainDataServiceBase<MenuVersionEntity, MenuVersionEntityDto>(user, dac, hasSoftDelete:false) 
{
     // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
     public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
     {
         // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
         return await DeleteAsync(id, ct);
     }
}