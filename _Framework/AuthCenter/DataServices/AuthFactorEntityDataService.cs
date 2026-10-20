using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuthCenter;
using TKWF.Ext.AuthCenter.DTOs;

namespace TKWF.Ext.AuthCenter;

/// <summary>数据服务：&#x8BA4;&#x8BC1;&#x56E0;&#x5B50;&#x5B9E;&#x4F53;&#x2014;&#x2014;AuthAccount &#x51ED;&#x636E;&#x767D;&#x540D;&#x5355;&#x7684;&#x56E0;&#x5B50;&#x5316;&#x6F14;&#x8FDB;&#xFF08;WAVE 1&#xFF1A;&#x65B0;&#x589E; SG1 &#x6570;&#x636E;&#x5E95;&#x5EA7;&#xFF0C;&#x4E25;&#x683C;&#x589E;&#x91CF;&#x4E0D;&#x6539;&#x65E2;&#x6709;&#xFF09;&#x3002;      &lt;para&gt;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#xFF08;&#x4E0D;&#x6307;&#x5B9A; UserType&#x2014;&#x2014;ADR42 D4&#xFF0C;&#x4F9B;&#x4EFB;&#x610F;&#x88C5;&#x914D;&#x5B9E;&#x4F8B;&#xFF09;&#x3002;&lt;/para&gt;      &lt;para&gt;FactorType &#x8BED;&#x4E49;&#xFF1A;1=Password / 2=Sms / 3=Email / 4=Federated / 5=Totp(&#x9884;&#x7559;) / 6=Passkey(&#x9884;&#x7559;)&#x3002;&lt;/para&gt;      &lt;para&gt;Identifier &#x89C4;&#x8303;&#x5316;&#xFF1A;Password=UId&#xFF1B;Sms=E.164&#xFF1B;Email=&#x5C0F;&#x5199; trim&#xFF1B;Federated=&lt;c&gt;{channelId}|{externalUserId}&lt;/c&gt;&#x3002;&lt;/para&gt;      &lt;para&gt;SecretHash&#xFF1A;PBKDF2/AES-GCM blob&#xFF08;&#x539F; AuthAccount.PasswordHash &#x7684;&#x56E0;&#x5B50;&#x5316;&#x843D;&#x70B9;&#xFF09;&#x3002;&lt;/para&gt;      &lt;para&gt;State&#xFF1A;0=Active / 1=Unverified&#xFF08;&#x89E3;&#x7ED1;=&#x7269;&#x7406;&#x5220;&#x9664;&#x2014;&#x2014;&#x65E0; Revoked &#x6001;&#xFF09;&#x3002;&lt;/para&gt;      &lt;para&gt;Metadata&#xFF1A;&#x6269;&#x5C55; JSON&#xFF0C;&lt;b&gt;&#x4E0D;&#x53C2;&#x4E0E;&#x67E5;&#x8BE2;&lt;/b&gt;&#xFF08;&#x4EC5;&#x5E8F;&#x5217;&#x5316;&#x5B58;&#x8BFB;&#xFF0C;&#x4E0D;&#x505A;&#x4EFB;&#x4F55;&#x7B5B;&#x9009;/&#x6392;&#x5E8F;&#x6761;&#x4EF6;&#xFF09;&#x3002;&lt;/para&gt;      &#x8868;&#x540D; &lt;c&gt;TKWF_AuthFactor&lt;/c&gt;&#xFF08;ADR100 &#x8868;&#x540D;&#x524D;&#x7F00;&#x2014;&#x2014;AuthCenter &#x8868;&#x524D;&#x7F00;&#x6279;&#x6B21;&#xFF09;&#x3002;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 AuthFactorEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class AuthFactorEntityDataService(IDomainUser user, IEntityDAC<AuthFactorEntity> dac)
        : DomainDataServiceBase<AuthFactorEntity, AuthFactorEntityDto>(user, dac, hasSoftDelete:false) 
{
     // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
     public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
     {
         // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
         return await DeleteAsync(id, ct);
     }
}