using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.WeChatOperations
{
    /// <summary>
    /// 运营配置·票据域（表 <c>TKWF_OpsConfig_Ticket</c>）——D03 1.10 运营配置面（按域多表 O4-4：各域独立版本/并发写/schema 演进）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>
    /// <para>密封票据行为策略（D03 1.3 签票载荷切换 + 本次运营配置化）：
    /// <see cref="Mode"/>（Sealed | Loose——仅 Sealed 本期可用，O3 定稿）/
    /// <see cref="TtlSeconds"/> 票据有效期（默认 300）。</para>
    /// <para>乐观并发（R7）：<see cref="Version"/> 为并发令牌——PUT 带 expected version，冲突 OPS_CONFIG_VERSION_CONFLICT（HTTP 400——框架 DomainException 映射；409 需框架扩展 ErrorCode→Status 映射）；<see cref="UpdatedAt"/>/<see cref="UpdatedBy"/> 写路径统一填充。</para>
    /// </summary>
    [Table("TKWF_OpsConfig_Ticket")]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class TicketOpsConfigEntity
    {
        /// <summary>主键（单行配置表恒为 1——业务主键非自增，按 Id=1 单行读写）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>票据模式（Sealed | Loose——仅 Sealed 本期可用；空 = 未配置 → 消费方默认 Sealed）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(50)]
        public string? Mode { get; set; }

        /// <summary>票据有效期秒（空 = 未配置 → 消费方默认 300）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        public int? TtlSeconds { get; set; }

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