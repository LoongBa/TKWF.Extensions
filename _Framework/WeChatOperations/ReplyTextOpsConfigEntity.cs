using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.WeChatOperations
{
    /// <summary>
    /// 运营配置·回复文案域（表 <c>TKWF_OpsConfig_ReplyText</c>）——D03 1.10 运营配置面（按域多表 O4-4：各域独立版本/并发写/schema 演进）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>
    /// <para><see cref="DisabledChannelReplyTemplate"/>：S3 通道停用提示文案（D03 §4.3 S3）；
    /// <see cref="DefaultReplyText"/>：S5 无 handler 命中兜底文案（WeChatOperations 回复 handler 模板兜底优先）。</para>
    /// <para>乐观并发（R7）：<see cref="Version"/> 为并发令牌——PUT 带 expected version，冲突 OPS_CONFIG_VERSION_CONFLICT（HTTP 400——框架 DomainException 映射；409 需框架扩展 ErrorCode→Status 映射）；<see cref="UpdatedAt"/>/<see cref="UpdatedBy"/> 写路径统一填充。</para>
    /// </summary>
    [Table("TKWF_OpsConfig_ReplyText")]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class ReplyTextOpsConfigEntity
    {
        /// <summary>主键（单行配置表恒为 1——业务主键非自增，按 Id=1 单行读写）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>S3 通道停用提示文案（空 = 未配置 → 消费方 Options 降级默认「服务暂停中，请稍后再试」）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(500)]
        public string? DisabledChannelReplyTemplate { get; set; }

        /// <summary>S5 兜底回复文案（空 = 未配置 → 消费方 Options 降级默认「感谢您的关注，请稍后再试」）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(500)]
        public string? DefaultReplyText { get; set; }

        /// <summary>乐观并发令牌（R7——PUT 带 expected version，冲突 OPS_CONFIG_VERSION_CONFLICT（HTTP 400——框架 DomainException 映射；409 需框架扩展 ErrorCode→Status 映射））。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        public long Version { get; set; }

        /// <summary>最近更新时间（UTC，写路径统一填充）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        public DateTime UpdatedAt { get; set; }

        /// <summary>最近更新人（UId，写路径统一填充；系统初始化可为空）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        [MaxLength(100)]
        public string? UpdatedBy { get; set; }
    }
}