using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.TrustCenter;
using TKWF.Ext.TrustCenter.DTOs;

namespace TKWF.Ext.TrustCenter;

/// <summary>数据服务：SSO &#x5E94;&#x7528;&#x6CE8;&#x518C;&#x5B9E;&#x4F53;&#xFF08;&#x4E0B;&#x6E38; SP &#x63A5;&#x5165;&#x70B9;&#xFF09;&#x2014;&#x2014;app_id &#x2B; origin &#x767D;&#x540D;&#x5355; &#x2B; scope &#x2B; client credential &#x2B; per-channel HMAC &#x5BC6;&#x94A5;&#x3002;      &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#xFF08;&#x4E0D;&#x6307;&#x5B9A; UserType&#x2014;&#x2014;ADR42 D4&#xFF0C;&#x4F9B;&#x4EFB;&#x610F;&#x88C5;&#x914D;&#x5B9E;&#x4F8B;&#xFF09;&#x3002;&lt;/para&gt;      &lt;para&gt;&#x8BBE;&#x8BA1;&#x6587;&#x6863; &#xA7;5.4/&#xA7;6.3&#xFF1A;ClientSecret &#x4E0E; HmacSecret &#x5747; AES-GCM &#x52A0;&#x5BC6;&#xFF08;DataService &#x8FB9;&#x754C;&#x52A0;&#x89E3;&#x5BC6;&#x2014;&#x2014;      &#x5BF9;&#x9F50; PlatformCredentialEntity &#x5148;&#x4F8B;&#xFF09;&#xFF1B;&lt;c&gt;HmacSecretEncrypted&lt;/c&gt; &#x4E3A; per-channel &#x2B; per-app HMAC &#x5BC6;&#x94A5;      &#xFF08;&lt;c&gt;/sso/issue&lt;/c&gt; &#x9A8C;&#x7B7E;&#x5BC6;&#x94A5;&#x6765;&#x6E90;&#xFF0C;Oracle P1-4&#x2014;&#x2014;&#x4E0D;&#x590D;&#x7528; PlatformCredentialEntity&#xFF09;&#x3002;&lt;/para&gt;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 SsoClientEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class SsoClientEntityDataService(IDomainUser user, IEntityDAC<SsoClientEntity> dac)
        : DomainDataServiceBase<SsoClientEntity, SsoClientEntityDto>(user, dac, hasSoftDelete:false) 
{
     // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
     public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
     {
         // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
         return await DeleteAsync(id, ct);
     }
}