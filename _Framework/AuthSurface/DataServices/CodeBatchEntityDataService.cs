using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuthSurface.DTOs;

namespace TKWF.Ext.AuthSurface;

/// <summary>
/// 数据服务：兑换码批次实体（表 <c>TKWF_CodeBatch</c>，v0.3.0 批次管理端）——批次行生命周期（CAS 抢占/落定/回滚/删除/吊销联动）的持久化原子方法。
/// <para>提示：标准 CRUD 逻辑和构造函数已由 CodeBatchEntityDataService.g.cs 承载，本分部仅编写业务原子方法。</para>
/// </summary>
partial class CodeBatchEntityDataService(IDomainUser user, IEntityDAC<CodeBatchEntity> dac)
        : DomainDataServiceBase<CodeBatchEntity, CodeBatchEntityDto>(user, dac, hasSoftDelete: false)
{
    /// <summary>按 Id 查询批次头（不存在返回 null）——new：基类 DomainReadOnlyDataServiceBase 已有同签名成员，本分部显式覆盖为 EntityGetAsync 语义。</summary>
    public new async Task<CodeBatchEntity?> GetByIdAsync(long id, CancellationToken ct = default)
        => await EntityGetAsync(e => e.Id == id, ct);

    /// <summary>
    /// CAS 抢占生成（DRAFT→GENERATING）——单语句 WHERE 谓词（ADR89 防 read-modify-write 竞态）：
    /// <c>Id==id AND Status==0(DRAFT) AND GeneratedCount==0</c> → 翻 GENERATING。
    /// <para>影响 0 行 = 并发生成败者（批次已非 DRAFT/已生成过）→ 门面抛 BATCH_NOT_EDITABLE；与码生成同事务——进程崩溃 DB 回滚天然回 DRAFT。</para>
    /// </summary>
    public async Task<bool> CasClaimGeneratingAsync(long id, CancellationToken ct = default)
        => await EntityUpdateWhereAsync(
            where: e => e.Id == id && e.Status == 0 && e.GeneratedCount == 0,
            setColumns: e => new CodeBatchEntity { Status = 1, UpdateTime = System.DateTime.UtcNow },
            ct: ct) > 0;

    /// <summary>生成完成落定（GENERATING→GENERATED）——全量成功调用：Status=2 + GeneratedCount=Quantity + ExportedAtUtc=now。</summary>
    public async Task<bool> CompleteGeneratingAsync(long id, int generatedCount, CancellationToken ct = default)
        => await EntityUpdateWhereAsync(
            where: e => e.Id == id && e.Status == 1,
            setColumns: e => new CodeBatchEntity
            {
                Status = 2,
                GeneratedCount = generatedCount,
                ExportedAtUtc = System.DateTime.UtcNow,
                UpdateTime = System.DateTime.UtcNow,
            },
            ct: ct) > 0;

    /// <summary>生成失败/中断回滚（GENERATING→DRAFT）——仅 GENERATING 态可回退，败者=批次已被其他路径消费（抛/忽略由门面裁决）。</summary>
    public async Task<bool> ResetToDraftAsync(long id, CancellationToken ct = default)
        => await EntityUpdateWhereAsync(
            where: e => e.Id == id && e.Status == 1,
            setColumns: e => new CodeBatchEntity { Status = 0, UpdateTime = System.DateTime.UtcNow },
            ct: ct) > 0;

    /// <summary>
    /// CAS 删除（仅 DRAFT 物理删——批次状态机：DRAFT 无码，删除安全）——返回是否删成功（非 DRAFT/不存在 → false）。
    /// <para>生成基座 <c>EntityDeleteBatchAsync(ids)</c> 仅支持 id 集（无谓词形态）→ 先按 <c>Status==0</c> 校验再删；
    /// 删前校验非原子（注释备案）：批次仅 DRAFT 可删、DRAFT 无码，无并发生成/吊销路径与删除竞争（门面层 DeleteBatchAsync 仅 DRAFT 分支），竞态窗口无业务影响。</para>
    /// </summary>
    public async Task<bool> CasRemoveAsync(long id, CancellationToken ct = default)
    {
        var batch = await EntityGetAsync(e => e.Id == id, ct);
        if (batch is null || batch.Status != 0) return false;
        return await EntityDeleteBatchAsync(new[] { id }, ct) > 0;
    }
}