using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuthCenter;
using TKWF.Ext.AuthCenter.DTOs;

namespace TKWF.Ext.AuthCenter;

/// <summary>数据服务：&#x8DE8;&#x7CFB;&#x7EDF;&#x6620;&#x5C04;&#x5B9E;&#x4F53;&#x2014;&#x2014;&#x5E73;&#x53F0;&#x5185;&#x90E8; id &#x2194; &#x4E1A;&#x52A1; app &#x2B; &#x4E1A;&#x52A1;&#x672C;&#x5730; id &#x2B; UnionId&#xFF08;&#x7EDF;&#x4E00; DMP &#x53CC;&#x673A;&#x5236;&#xFF1A;&#x6620;&#x5C04;&#x8868;/&#x5916;&#x952E;&#xFF09;&#x3002;      &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#xFF08;&#x4E0D;&#x6307;&#x5B9A; UserType&#x2014;&#x2014;ADR42 D4&#xFF09;&#x3002;&lt;/para&gt;      &lt;para&gt;DMP V4.0 &#x7BA1;&#x7406;&#x5458;&#x8D70;&#x6620;&#x5C04;&#x8868;&#x3001;&#x4F1A;&#x5458;&#x8D70;&#x5916;&#x952E;&#x2014;&#x2014;Ext &#x4EE5;&#x672C;&#x8868;&#x7EDF;&#x4E00;&#x627F;&#x8F7D;&#xFF1B;DMP &#x8FC1;&#x79FB;&#x65F6;&#x4F1A;&#x5458;&#x6620;&#x5C04;&#x8FC1;&#x5165;&#x672C;&#x8868;&#xFF08;&#x9700;&#x6C42; &#xA7;6 &#x7EA6;&#x675F; 1&#xFF09;&#x3002;&lt;/para&gt;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 PlatformAccountMapEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class PlatformAccountMapEntityDataService(IDomainUser user, IEntityDAC<PlatformAccountMapEntity> dac)
        : DomainDataServiceBase<PlatformAccountMapEntity, PlatformAccountMapEntityDto>(user, dac, hasSoftDelete:false) 
{
     // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
     public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
     {
         // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
         return await DeleteAsync(id, ct);
     }
}