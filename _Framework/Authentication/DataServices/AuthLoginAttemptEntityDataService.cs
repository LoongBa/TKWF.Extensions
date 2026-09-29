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

/// <summary>数据服务：&#x767B;&#x5F55;&#x5C1D;&#x8BD5;&#x5B9E;&#x4F53;&#x2014;&#x2014;&#x767B;&#x5F55;&#x4FDD;&#x62A4;&#xFF08;&#x9650;&#x6D41;/&#x5BA1;&#x8BA1;&#x6570;&#x636E;&#x6E90;&#xFF0C;&#x53EA;&#x589E;&#x8BED;&#x4E49;&#xFF09;&#x3002;      &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#xFF08;&#x4E0D;&#x6307;&#x5B9A; UserType&#x2014;&#x2014;ADR42 D4&#xFF09;&#x3002;&lt;/para&gt;      &lt;para&gt;&#x8BA4;&#x8BC1;&#x4E2D;&#x5FC3;&#x5B9E;&#x4F8B;&#x81EA;&#x8EAB;&#x5E93;&#x7684;&#x767B;&#x5F55;&#x5C1D;&#x8BD5;&#x8BA1;&#x6570;&#xFF08;&#x542B; authType &#x7EF4;&#x5EA6;&#xFF09;&#xFF0C;&#x4E0E; SecurityLog &#x4E8B;&#x4EF6;&#x65E5;&#x5FD7; / Account &#x9501;&#x5B9A; / RateLimiting &#x4E2D;&#x95F4;&#x4EF6;&#x4E92;&#x8865;&#x4E0D;&#x91CD;&#x53E0;&#x3002;&lt;/para&gt;</summary>
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
