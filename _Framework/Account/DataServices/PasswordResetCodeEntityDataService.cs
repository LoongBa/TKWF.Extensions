using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Account;
using TKWF.Ext.Account.DTOs;

namespace TKWF.Ext.Account;

/// <summary>数据服务：密码重置码记录——用户名、重置码、过期时间与使用状态。     <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>；     保留 BCL <c>[Table("PasswordResetCode")]</c>；列映射用 FreeSql <c>[Column]</c>。</para></summary>
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