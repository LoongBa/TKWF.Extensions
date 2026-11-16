namespace TKWF.Ext.AuthSurface;

/// <summary>
/// 批次管理业务错误码（v0.3.0 批次管理端——镜像 RedemptionErrorCodes 范式）——
/// 经 BCL <c>AuthenticationException</c> 抛出（<c>new AuthenticationException(BatchErrorCodes.Xxx)</c>，对齐兑换/票据域惯例）。
/// </summary>
public static class BatchErrorCodes
{
    /// <summary>批次不存在（GetById 为 null）。</summary>
    public const string BatchNotFound = "BATCH_NOT_FOUND";

    /// <summary>批次不可编辑（非 DRAFT 拒删/非 GENERATED 拒吊销/生成 CAS 抢占败者——状态机当前态不允许该动作）。</summary>
    public const string BatchNotEditable = "BATCH_NOT_EDITABLE";

    /// <summary>批次计划数量超限（超出 Options.MaxBatchQuantity）。</summary>
    public const string BatchQuantityExceeded = "BATCH_QUANTITY_EXCEEDED";
}