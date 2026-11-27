namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 脱敏策略（<b>store 层统一应用</b>——所有写路径的唯一收敛点）。
    /// <para>默认实现留主包；消费方可 <c>TryAdd</c> 替换。写时脱敏使全部读路径（查询/统一门面）天然一致。</para>
    /// </summary>
    public interface IAuditMaskingPolicy
    {
        /// <summary>
        /// 对对象做脱敏（按字段名/JSON 路径/正则命中，命中值替换为掩码）。返回脱敏后的可序列化对象/字典；
        /// <c>null</c> 原样返回。
        /// </summary>
        object? Mask(object? value);

        /// <summary>脱敏自由文本（用于 Summary/Detail 等；<c>null</c>/空原样返回）。</summary>
        string? MaskText(string? text);
    }
}
