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

/// <summary>数据服务：&#x77ED;&#x4FE1;&#x9A8C;&#x8BC1;&#x7801;&#x8BB0;&#x5F55;&#x5B9E;&#x4F53;&#x2014;&#x2014;&#x9891;&#x63A7;&#x6570;&#x636E;&#x6E90;&#xFF08;&#x72EC;&#x7ACB;&#x4E8E; AuthLoginAttempt&#xFF09;&#x3002;      &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#xFF08;&#x4E0D;&#x6307;&#x5B9A; UserType&#x2014;&#x2014;ADR42 D4&#xFF09;&#x3002;&lt;/para&gt;      &lt;para&gt;&#x9A8C;&#x8BC1;&#x7801;&#x5355;&#x5411;&#x6563;&#x5217;&#xFF08;SHA256&#xFF09;&#x843D;&#x5E93;&#xFF0C;&#x4E0D;&#x5B58;&#x660E;&#x6587;&#xFF1B;&#x6821;&#x9A8C;&#x6210;&#x529F;&#x540E; IsVerified=true &#x5355;&#x6B21;&#x6D88;&#x8D39;&#x3002;&lt;/para&gt;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 SmsRecordEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
//
// 【只增不改语义（SecurityLog 先例，方案 §5.5）】
// 短信验证码记录为追加写数据源——本分部类刻意【不】提供任何 Update/Delete 业务方法（AdminDeleteAsync 已删除）；
// 写路径 = SmsVerificationService → CreateAsync（发送）/ MarkVerifiedAsync（校验消费，仅 IsVerified 列更新）。
 partial class SmsRecordEntityDataService(IDomainUser user, IEntityDAC<SmsRecordEntity> dac)
        : DomainDataServiceBase<SmsRecordEntity, SmsRecordEntityDto>(user, dac, hasSoftDelete:false) 
{
    /// <summary>追加记录一条短信验证码记录（发送频控数据源；CodeHash 为 SHA256 hex，不存明文）。</summary>
    /// <param name="entity">短信验证码记录实体。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<SmsRecordEntity> CreateAsync(SmsRecordEntity entity, CancellationToken ct = default)
        => await EntityCreateAsync(entity, ct);

    /// <summary>最新未消费验证码（IsVerified==false，CreateTime 降序取最新一条——<b>不过滤过期</b>，
    /// 过期与否由 SmsVerificationService.VerifyCodeAsync 判定（区分 NOT_FOUND / EXPIRED 语义））。</summary>
    /// <param name="phone">手机号。</param>
    /// <param name="scene">场景（<see cref="SmsScenes"/> 常量）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>最新未消费记录；无则 null。</returns>
    public async Task<SmsRecordEntity?> GetLatestUnverifiedAsync(string phone, string scene, CancellationToken ct = default)
    {
        var rows = await EntitySelectAsync(
            e => e.Phone == phone && e.Scene == scene && !e.IsVerified,
            0, 1,
            q => q.OrderByDescending(e => e.CreateTime),
            ct);
        return rows.Count > 0 ? rows[0] : null;
    }

    /// <summary>发送次数计数（重发间隔/小时/天频控——SQL COUNT 下推，对齐基类 CountAsync 路径）。</summary>
    /// <param name="phone">手机号。</param>
    /// <param name="scene">场景（<see cref="SmsScenes"/> 常量）。</param>
    /// <param name="fromUtc">窗口起始时间（UTC，CreateTime &gt;=）。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<int> CountSentAsync(string phone, string scene, DateTime fromUtc, CancellationToken ct = default)
        => (int)await CountAsync(e => e.Phone == phone && e.Scene == scene && e.CreateTime >= fromUtc, ct);

    /// <summary>按来源 IP 发送次数计数（IP 维度频控——SQL COUNT 下推）。</summary>
    /// <param name="ipAddress">来源 IP（IPv4/IPv6）。</param>
    /// <param name="fromUtc">窗口起始时间（UTC，CreateTime &gt;=）。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<int> CountSentByIpAsync(string ipAddress, DateTime fromUtc, CancellationToken ct = default)
        => (int)await CountAsync(e => e.IpAddress == ipAddress && e.CreateTime >= fromUtc, ct);

    /// <summary>校验尝试次数计数（校验频控——统计窗口内【未过期】的发送记录行，即活动验证码数；设计决策：不落独立尝试行，复用 SmsRecord 活动码行计数）。</summary>
    /// <param name="phone">手机号。</param>
    /// <param name="scene">场景（<see cref="SmsScenes"/> 常量）。</param>
    /// <param name="fromUtc">窗口起始时间（UTC，CreateTime &gt;=；调用方传 now-1h）。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<int> CountVerifyAttemptsAsync(string phone, string scene, DateTime fromUtc, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return (int)await CountAsync(
            e => e.Phone == phone && e.Scene == scene && e.CreateTime >= fromUtc && e.ExpireAt >= now,
            ct);
    }

    /// <summary>标记验证码已消费（IsVerified=true，单次消费——校验成功路径）。</summary>
    /// <param name="id">记录主键。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>标记成功 true；记录不存在 false。</returns>
    public async Task<bool> MarkVerifiedAsync(long id, CancellationToken ct = default)
    {
        var entity = await EntityGetAsync(e => e.Id == id, ct);
        if (entity is null) return false;
        entity.IsVerified = true;
        await EntityUpdateAsync(entity, ct);
        return true;
    }
}
