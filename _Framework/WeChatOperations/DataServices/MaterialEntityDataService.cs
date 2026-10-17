using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.WeChatOperations;
using TKWF.Ext.WeChatOperations.DTOs;

namespace TKWF.Ext.WeChatOperations;

/// <summary>数据服务：&#x7D20;&#x6750;&#x5B9E;&#x4F53;&#xFF08;&#x8868; &lt;c&gt;TKWF_WeChatMaterial&lt;/c&gt;&#xFF09;&#x2014;&#x2014;&#x7D20;&#x6750;&#x5E93;&#xFF08;&#x5168;&#x5C40;&#x5171;&#x4EAB;&#x2014;&#x2014;&#x65E0; ChannelId&#xFF0C;&#x89C4;&#x5219;&#x6309; &lt;see cref=&quot;MaterialEntity.Id&quot;/&gt; &#x5F15;&#x7528;&#xFF0C;&#x4E00;&#x4E2A;&#x7D20;&#x6750;&#x591A;&#x89C4;&#x5219;&#x590D;&#x7528;&#xFF09;&#x3002;      &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#x3002;&lt;/para&gt;      &lt;para&gt;Kind&#xFF1A;1=Text&#xFF08;&#x7EAF;&#x6587;&#x672C;&#xFF0C;&lt;see cref=&quot;Content&quot;/&gt;&#xFF09;/ 2=News&#xFF08;&#x56FE;&#x6587;&#xFF0C;Title/Description/Url/PicUrl&#xFF09;&#x3002;&lt;/para&gt;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 MaterialEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class MaterialEntityDataService(IDomainUser user, IEntityDAC<MaterialEntity> dac)
        : DomainDataServiceBase<MaterialEntity, MaterialEntityDto>(user, dac, hasSoftDelete:false) 
{
     // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
     public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
     {
         // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
         return await DeleteAsync(id, ct);
     }
}