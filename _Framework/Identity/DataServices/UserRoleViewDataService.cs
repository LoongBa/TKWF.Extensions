using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Identity;
using TKWF.Ext.Identity.DTOs;

namespace TKWF.Ext.Identity;

/// <summary>
/// 用户-角色视图只读 DataService（VEntity V0.2.0）——跨表 JOIN 单查询下推 DB。
/// <para>VEntity 由 xCodeGen 跳过 DataService 模板（Engine.cs L42-46），此处手写继承
/// <see cref="DomainReadOnlyDataServiceBase{TEntity, TDto}"/>（2 参数版，与扩展现有 DataService 一致）。
/// 注入 <see cref="IEntityReadOnlyDAC{TEntity}"/>（只读契约）——绝不用 IEntityDAC（FreeSqlEntityDAC 静态守卫）。
/// 不标 <c>[GenerateController(FromDataService = true)]</c>：GetRolesAsync 是 Store 内部能力，
/// REST 经 IUserManager 门面暴露；GraphQL 经 ExposeGraphqlQuery 由 SG1b 自动生成 resolver。</para>
/// </summary>
partial class UserRoleViewDataService(IDomainUser user, IEntityReadOnlyDAC<UserRoleView> dac)
    : DomainReadOnlyDataServiceBase<UserRoleView, UserRoleViewDto>(user, dac, hasSoftDelete: false)
{
    /// <summary>单查询跨表：按 UserId 返回用户角色（JOIN IdentityUserRole → IdentityRole 下推 DB，替代两步查询）。</summary>
    public async Task<List<UserRoleView>> GetRolesByUserIdAsync(long userId, CancellationToken ct = default)
        => await SelectAsync(v => v, predicate: v => v.UserId == userId, limit: 1000, ct: ct);
}
