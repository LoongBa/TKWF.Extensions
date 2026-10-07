using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuthCenter;
using TKWF.Ext.AuthCenter.DTOs;

namespace TKWF.Ext.AuthCenter;

/// <summary>数据服务：用户档案实体——认证中心基础档案 1:1（V0.9.0 凭据/档案表级分离 A.1：Nickname/Avatar/Birthday/Gender/Email）。
/// <para>红线（数据访问）：档案读写经 SG1 生成 <see cref="UserProfileEntityDataService"/>——零 ORM 直注入。</para>
/// <para>V0.9.0（T2/T3）：档案读写入口——AuthAccountUserProfileSource / AuthAccountQueryService.ToDto 等消费方经此读写 1:1 档案。</para></summary>
// 提示：标准 CRUD 逻辑和构造函数已由 UserProfileEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class UserProfileEntityDataService(IDomainUser user, IEntityDAC<UserProfileEntity> dac)
        : DomainDataServiceBase<UserProfileEntity, UserProfileEntityDto>(user, dac, hasSoftDelete:false) 
{
    /// <summary>按平台内部 id 查档案（1:1——AuthAccount.UId 唯一；null=未建档）。</summary>
    public async Task<UserProfileEntity?> GetByUIdAsync(string uid, CancellationToken ct = default)
        => await EntityGetAsync(p => p.UId == uid, ct);

    /// <summary>档案 1:1 upsert——按 UId 存在则更新（Nickname/Avatar/Birthday/Gender/Email + UpdateTime），不存在则建。</summary>
    public async Task<UserProfileEntity> CreateOrUpdateAsync(UserProfileEntity profile, CancellationToken ct = default)
    {
        var existing = await GetByUIdAsync(profile.UId, ct);
        if (existing is null)
        {
            profile.CreateTime = DateTime.UtcNow;
            profile.UpdateTime = DateTime.UtcNow;
            await EntityCreateAsync(profile, ct);
            return profile;
        }

        existing.Nickname = profile.Nickname;
        existing.Avatar = profile.Avatar;
        existing.Birthday = profile.Birthday;
        existing.Gender = profile.Gender;
        existing.Email = profile.Email;
        existing.UpdateTime = DateTime.UtcNow;
        await EntityUpdateAsync(existing, ct);
        return existing;
    }

/// <summary>按 Email 查档案（找回链路 Email 通道——UId 从档案导入；联系方式角色查询）。</summary>
    public async Task<UserProfileEntity?> GetByEmailAsync(string email, CancellationToken ct = default)
        => await EntityGetAsync(p => p.Email == email, ct);

     // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
     public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
     {
         // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
         return await DeleteAsync(id, ct);
     }
}