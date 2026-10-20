using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuthCenter;
using TKWF.Ext.AuthCenter.DTOs;

namespace TKWF.Ext.AuthCenter;

/// <summary>数据服务：&#x8BA4;&#x8BC1;&#x56E0;&#x5B50;&#x4E8B;&#x4EF6;&#x5B9E;&#x4F53;&#x2014;&#x2014;append-only &#x5BA1;&#x8BA1;&#x4E8B;&#x5B9E;&#x8868;&#xFF08;&lt;b&gt;&#x53EA;&#x589E;&#x4E0D;&#x5220;&#x6539;&lt;/b&gt;&#xFF1A;&#x5199;&#x5165;&#x5373;&#x6C38;&#x4E45;&#xFF0C;&#x65E0; Update/Delete &#x4E1A;&#x52A1;&#x8BED;&#x4E49;&#xFF09;&#x3002;      &lt;para&gt;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#xFF08;&#x4E0D;&#x6307;&#x5B9A; UserType&#x2014;&#x2014;ADR42 D4&#xFF0C;&#x4F9B;&#x4EFB;&#x610F;&#x88C5;&#x914D;&#x5B9E;&#x4F8B;&#xFF09;&#x3002;&lt;/para&gt;      &lt;para&gt;EventType&#xFF1A;1=Bind / 2=Unbind / 3=Verify / 4=Reset&#xFF1B;Source&#xFF1A;1=Manual / 2=Business / 3=Policy / 4=Provisioning&#x3002;&lt;/para&gt;      &lt;para&gt;Identifier &#x4FDD;&#x7559;&#x6807;&#x8BC6;&#x5FEB;&#x7167;&#x2014;&#x2014;&#x56E0;&#x5B50;&#x89E3;&#x7ED1;&#xFF08;&#x7269;&#x7406;&#x5220;&#x9664;&#xFF09;&#x540E;&#x4ECD;&#x53EF;&#x8FFD;&#x6EAF;&#x8BE5;&#x6807;&#x8BC6;&#x7684;&#x5386;&#x53F2;&#x4E8B;&#x4EF6;&#x3002;&lt;/para&gt;      &#x8868;&#x540D; &lt;c&gt;TKWF_AuthFactorEvent&lt;/c&gt;&#xFF08;ADR100 &#x8868;&#x540D;&#x524D;&#x7F00;&#x2014;&#x2014;AuthCenter &#x8868;&#x524D;&#x7F00;&#x6279;&#x6B21;&#xFF09;&#x3002;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 AuthFactorEventEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class AuthFactorEventEntityDataService(IDomainUser user, IEntityDAC<AuthFactorEventEntity> dac)
        : DomainDataServiceBase<AuthFactorEventEntity, AuthFactorEventEntityDto>(user, dac, hasSoftDelete:false) 
{
     // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
     public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
     {
         // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
         return await DeleteAsync(id, ct);
     }
}