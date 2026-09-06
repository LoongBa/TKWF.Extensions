using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Account;
using TKWF.Ext.Account.DTOs;

namespace TKWF.Ext.Account;

/// <summary>数据服务：&#x5BC6;&#x7801;&#x91CD;&#x7F6E;&#x7801;&#x8BB0;&#x5F55;&#x2014;&#x2014;&#x7528;&#x6237;&#x540D;&#x3001;&#x91CD;&#x7F6E;&#x7801;&#x3001;&#x8FC7;&#x671F;&#x65F6;&#x95F4;&#x4E0E;&#x4F7F;&#x7528;&#x72B6;&#x6001;&#x3002;     &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#xFF1B;     &#x4FDD;&#x7559; BCL &lt;c&gt;[Table(&quot;PasswordResetCode&quot;)]&lt;/c&gt;&#xFF1B;&#x5217;&#x6620;&#x5C04;&#x7528; FreeSql &lt;c&gt;[Column]&lt;/c&gt;&#x3002;&lt;/para&gt;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 PasswordResetCodeEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class PasswordResetCodeEntityDataService(IDomainUser user, IEntityDAC<PasswordResetCodeEntity> dac)
        : DomainDataServiceBase<PasswordResetCodeEntity, PasswordResetCodeEntityDto>(user, dac, hasSoftDelete:false) 
{
     // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
     public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
     {
         // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
         return await DeleteAsync(id, ct);
     }

     // ── 数据访问红线整改：PasswordResetStore 委托路径的业务方法 ──

     /// <summary>按用户名 + 重置码查询单条重置码记录。</summary>
     public async Task<PasswordResetCodeEntity?> GetByUserNameAndCodeAsync(
         string userName, string resetCode, CancellationToken ct = default)
         => await EntityGetAsync(p => p.UserName == userName && p.ResetCode == resetCode, ct);

     /// <summary>保存重置码记录——EntityCreateAsync（回写自增 Id）。</summary>
     public async Task SaveAsync(PasswordResetCodeEntity entity, CancellationToken ct = default)
         => await EntityCreateAsync(entity, ct);

     /// <summary>标记重置码已使用——单字段更新（Id 保持不变；先查后按列批量更新 IsUsed）。</summary>
     public async Task MarkUsedAsync(long id, CancellationToken ct = default)
     {
         var existing = await EntityGetAsync(p => p.Id == id, ct);
         if (existing == null) return;
         existing.IsUsed = true;
         await EntityUpdateColumnsBatchAsync(new[] { existing }, p => new { p.IsUsed }, ct);
     }
}