using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuthCenter;
using TKWF.Ext.AuthCenter.DTOs;

namespace TKWF.Ext.AuthCenter;

/// <summary>数据服务：&#x8D26;&#x53F7;&#x51BB;&#x7ED3;&#x5B9E;&#x4F53;&#x2014;&#x2014;&#x591A;&#x539F;&#x56E0;&#x51BB;&#x7ED3;&#x884C;&#xFF08;V0.11.0 &#x8D77; AuthAccount.IsFrozen/FreezeEnd &#x4E24;&#x5217;&#x7684;&#x56E0;&#x5B50;&#x5316;&#x6F14;&#x8FDB;&#x5E95;&#x5EA7;&#xFF09;&#x3002;      &lt;para&gt;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#xFF08;&#x4E0D;&#x6307;&#x5B9A; UserType&#x2014;&#x2014;ADR42 D4&#xFF0C;&#x4F9B;&#x4EFB;&#x610F;&#x88C5;&#x914D;&#x5B9E;&#x4F8B;&#xFF09;&#x3002;&lt;/para&gt;      &lt;para&gt;&lt;b&gt;&#x6D3B;&#x8DC3;&#x5224;&#x636E;&#xFF1A;&lt;c&gt;ReleasedAt == null &amp;amp;&amp;amp; (FreezeEnd == null || FreezeEnd &amp;gt; DateTime.UtcNow)&lt;/c&gt;&lt;/b&gt;&#x2014;&#x2014;      &#x89E3;&#x51BB;&#x7F6E; ReleasedAt &#x975E;&#x7A7A;&#xFF08;&#x8F6F;&#x91CA;&#x653E;&#x4FDD;&#x7559;&#x5BA1;&#x8BA1;&#x884C;&#xFF09;&#xFF0C;&#x5230;&#x671F; FreezeEnd &#x5230;&#x65F6;&#x4E0D;&#x81EA;&#x52A8;&#x6E05;&#x884C;&#xFF08;&#x514D;&#x5199;&#x8DEF;&#x5F84;&#xFF0C;&#x5224;&#x636E;&#x5929;&#x7136;&#x5931;&#x6548;&#xFF09;&#x3002;&lt;/para&gt;      &lt;para&gt;Scope&#xFF1A;0=Account&#xFF08;&#x5168;&#x7AD9;&#x767B;&#x5F55;&#xFF09;/ 1=Login / 2=Feature&#xFF1B;&lt;b&gt;V0.11.0 &#x6052;&#x4E3A; 0&lt;/b&gt;&#xFF0C;&#x5176;&#x4F59;&#x4E3A;&#x9884;&#x7559;&#x4F4D;&#x3002;&lt;/para&gt;      &#x8868;&#x540D; &lt;c&gt;TKWF_AuthAccountFreeze&lt;/c&gt;&#xFF08;ADR100 &#x8868;&#x540D;&#x524D;&#x7F00;&#x2014;&#x2014;AuthCenter &#x8868;&#x524D;&#x7F00;&#x6279;&#x6B21;&#xFF09;&#x3002;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 AuthAccountFreezeEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class AuthAccountFreezeEntityDataService(IDomainUser user, IEntityDAC<AuthAccountFreezeEntity> dac)
        : DomainDataServiceBase<AuthAccountFreezeEntity, AuthAccountFreezeEntityDto>(user, dac, hasSoftDelete:false) 
{
     // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
     public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
     {
         // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
         return await DeleteAsync(id, ct);
     }
}