using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Account;
using TKWF.Ext.Account.DTOs;

namespace TKWF.Ext.Account;

/// <summary>数据服务：账户锁定记录——用户名、失败计数与锁定截止时间。     <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>；     保留 BCL <c>[Table("AccountLockout")]</c>；列映射用 FreeSql <c>[Column]</c>。</para></summary>
// 提示：标准 CRUD 逻辑和构造函数已由 AccountLockoutEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class AccountLockoutEntityDataService(IDomainUser user, IEntityDAC<AccountLockoutEntity> dac)
        : DomainDataServiceBase<AccountLockoutEntity, AccountLockoutEntityDto>(user, dac, hasSoftDelete:false) 
{
     // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
     public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
     {
         // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
         return await DeleteAsync(id, ct);
     }

     // ── 数据访问红线整改：AccountLockoutStore 委托路径的业务方法 ──

     /// <summary>按用户名查询单条锁定记录。</summary>
     public async Task<AccountLockoutEntity?> GetByUserNameAsync(string userName, CancellationToken ct = default)
         => await EntityGetAsync(a => a.UserName == userName, ct);

     /// <summary>Upsert——按 UserName 查存在：存在则全字段更新（保留自增 Id 与 CreateTime），
     /// 不存在则插入（回写自增 Id）。</summary>
     public async Task UpsertAsync(AccountLockoutEntity entity, CancellationToken ct = default)
     {
         var existing = await EntityGetAsync(a => a.UserName == entity.UserName, ct);
         if (existing != null)
         {
             existing.FailedCount = entity.FailedCount;
             existing.LockoutEnd = entity.LockoutEnd;
             existing.LastFailedTime = entity.LastFailedTime;
             existing.UpdateTime = entity.UpdateTime;
             await EntityUpdateAsync(existing, ct);
         }
         else
         {
             await EntityCreateAsync(entity, ct);
         }
     }

     /// <summary>按用户名物理删除（hasSoftDelete:false；EntitySoftDeleteAsync 对未启用软删实体抛
     /// <c>InvalidOperationException</c>，故用 <c>EntityDeleteBatchAsync</c> 走物理删路径）。</summary>
     public async Task DeleteByUserNameAsync(string userName, CancellationToken ct = default)
     {
         var existing = await EntityGetAsync(a => a.UserName == userName, ct);
         if (existing != null)
             await EntityDeleteBatchAsync(new[] { existing.Id }, ct);
     }
}