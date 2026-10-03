using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Authentication;
using TKWF.Ext.Authentication.DTOs;

namespace TKWF.Ext.Authentication;

/// <summary>数据服务：登录尝试实体——登录保护（限流/审计数据源，只增语义）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4）。</para>      <para>认证中心实例自身库的登录尝试计数（含 authType 维度），与 SecurityLog 事件日志 / Account 锁定 / RateLimiting 中间件互补不重叠。</para></summary>
// 提示：标准 CRUD 逻辑和构造函数已由 AuthLoginAttemptEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
//
// 【只增不改语义（SecurityLog 先例，方案 §5.6）】
// 登录尝试为追加写日志——本分部类刻意【不】提供任何 Update/Delete 业务方法（AdminDeleteAsync 已删除）；
// 唯一写路径 = AuthLoginAttemptService.RecordAttemptAsync → CreateAsync → EntityCreateAsync（追加）。
 partial class AuthLoginAttemptEntityDataService(IDomainUser user, IEntityDAC<AuthLoginAttemptEntity> dac)
        : DomainDataServiceBase<AuthLoginAttemptEntity, AuthLoginAttemptEntityDto>(user, dac, hasSoftDelete:false) 
{
    /// <summary>追加记录一次登录尝试（成功/失败均记录；AttemptTime 默认值由调用方置 UTC 当前时间）。</summary>
    /// <param name="entity">登录尝试实体。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<AuthLoginAttemptEntity> CreateAsync(AuthLoginAttemptEntity entity, CancellationToken ct = default)
        => await EntityCreateAsync(entity, ct);

    /// <summary>窗口内尝试次数计数（限流判定数据源——滑动窗口，SQL COUNT 下推，对齐基类 CountAsync 路径）。</summary>
    /// <param name="userIdentity">登录标识（手机号/openid/用户名）。</param>
    /// <param name="authType">认证方式（<see cref="AuthTypes"/> 常量）。</param>
    /// <param name="fromUtc">窗口起始时间（UTC，AttemptTime &gt;=）。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<int> CountInWindowAsync(string userIdentity, string authType, DateTime fromUtc, CancellationToken ct = default)
        => (int)await CountAsync(e => e.UserIdentity == userIdentity && e.AuthType == authType && e.AttemptTime >= fromUtc, ct);

    /// <summary>最近尝试记录（AttemptTime 降序，限流窗口/审计展示下界内）。</summary>
    /// <param name="userIdentity">登录标识（手机号/openid/用户名）。</param>
    /// <param name="authType">认证方式（<see cref="AuthTypes"/> 常量）。</param>
    /// <param name="count">返回条数（&gt; 0）。</param>
    /// <param name="fromUtc">起始时间（UTC，AttemptTime &gt;=）。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<List<AuthLoginAttemptEntity>> GetRecentAsync(string userIdentity, string authType, int count, DateTime fromUtc, CancellationToken ct = default)
    {
        var query = QueryForUser()
            .Where(e => e.UserIdentity == userIdentity && e.AuthType == authType && e.AttemptTime >= fromUtc)
            .OrderByDescending(e => e.AttemptTime)
            .Take(count);
        return await Dac.ToListAsync(query, ct);
    }
}