using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.WeChatOperations;
using TKWF.Ext.WeChatOperations.DTOs;

namespace TKWF.Ext.WeChatOperations;

/// <summary>数据服务：&#x8FD0;&#x8425;&#x914D;&#x7F6E;&#xB7;&#x5F31;&#x8EAB;&#x4EFD;&#x7B56;&#x7565;&#x57DF;&#xFF08;&#x8868; &lt;c&gt;TKWF_OpsConfig_WeakIdentity&lt;/c&gt;&#xFF09;&#x2014;&#x2014;D03 1.10 &#x8FD0;&#x8425;&#x914D;&#x7F6E;&#x9762;&#xFF08;&#x6309;&#x57DF;&#x591A;&#x8868; O4-4&#xFF1A;&#x5404;&#x57DF;&#x72EC;&#x7ACB;&#x7248;&#x672C;/&#x5E76;&#x53D1;&#x5199;/schema &#x6F14;&#x8FDB;&#xFF09;&#x3002;      &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#x3002;&lt;/para&gt;      &lt;para&gt;&#x5F31;&#x8EAB;&#x4EFD;&#x6E05;&#x7406;&#x7B56;&#x7565;&#xFF08;&#x80FD;&#x529B;&#x5F52; AuthCenter&#xFF0C;&#x7B56;&#x7565;&#x5F52;&#x8FD0;&#x8425;&#x914D;&#x7F6E;&#x9762;&#x2014;&#x2014;D03 O2 &#x5B9A;&#x7A3F;&#xFF09;&#xFF1A;      &lt;see cref=&quot;Enabled&quot;/&gt; &#x603B;&#x5F00;&#x5173; / &lt;see cref=&quot;InactiveDays&quot;/&gt; &#x65E0;&#x4EA4;&#x4E92;&#x5224;&#x5B9A;&#x5929;&#x6570; /      &lt;see cref=&quot;Action&quot;/&gt;&#xFF08;SoftDelete | PhysicalDelete&#xFF09;/      &lt;see cref=&quot;RecoveryWindowDays&quot;/&gt; &#x6062;&#x590D;&#x7A97;&#x53E3;&#xFF08;&#x8F6F;&#x5220;&#x540E;&#x671F;&#x9650;&#x5185;&#x53EF;&#x6062;&#x590D;&#xFF09;/      &lt;see cref=&quot;ExcludeBound&quot;/&gt; &#x6392;&#x9664;&#x5DF2;&#x7ED1;&#x5B9A;&#x8D26;&#x53F7;&#x3002;&lt;/para&gt;      &lt;para&gt;&#x4E50;&#x89C2;&#x5E76;&#x53D1;&#xFF08;R7&#xFF09;&#xFF1A;&lt;see cref=&quot;Version&quot;/&gt; &#x4E3A;&#x5E76;&#x53D1;&#x4EE4;&#x724C;&#x2014;&#x2014;PUT &#x5E26; expected version&#xFF0C;&#x51B2;&#x7A81; 409&#xFF1B;&lt;see cref=&quot;UpdatedAt&quot;/&gt;/&lt;see cref=&quot;UpdatedBy&quot;/&gt; &#x5199;&#x8DEF;&#x5F84;&#x7EDF;&#x4E00;&#x586B;&#x5145;&#x3002;&lt;/para&gt;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 WeakIdentityOpsConfigEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class WeakIdentityOpsConfigEntityDataService(IDomainUser user, IEntityDAC<WeakIdentityOpsConfigEntity> dac)
        : DomainDataServiceBase<WeakIdentityOpsConfigEntity, WeakIdentityOpsConfigEntityDto>(user, dac, hasSoftDelete:false) 
{
     // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
     public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
     {
         // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
         return await DeleteAsync(id, ct);
     }
}