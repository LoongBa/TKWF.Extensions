using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.WeChatOperations
{
    /// <summary>
    /// 运营配置·弱身份策略域（表 <c>TKWF_OpsConfig_WeakIdentity</c>）——D03 1.10 运营配置面（按域多表 O4-4：各域独立版本/并发写/schema 演进）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>
    /// <para>弱身份清理策略（能力归 AuthCenter，策略归运营配置面——D03 O2 定稿）：
    /// <see cref="Enabled"/> 总开关 / <see cref="InactiveDays"/> 无交互判定天数 /
    /// <see cref="Action"/>（SoftDelete | PhysicalDelete）/
    /// <see cref="RecoveryWindowDays"/> 恢复窗口（软删后期限内可恢复）/
    /// <see cref="ExcludeBound"/> 排除已绑定账号。</para>
    /// <para>乐观并发（R7）：<see cref="Version"/> 为并发令牌——PUT 带 expected version，冲突 409；<see cref="UpdatedAt"/>/<see cref="UpdatedBy"/> 写路径统一填充。</para>
    /// </summary>
    [Table("TKWF_OpsConfig_WeakIdentity")]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class WeakIdentityOpsConfigEntity
    {
        /// <summary>主键（单行配置表恒为 1——业务主键非自增，按 Id=1 单行读写）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>弱身份清理总开关（空 = 未配置 → 消费方默认禁用——fail-closed）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        public bool? Enabled { get; set; }

        /// <summary>无交互判定天数（空 = 未配置 → 消费方默认 180）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        public int? InactiveDays { get; set; }

        /// <summary>处置动作（SoftDelete | PhysicalDelete，空 = 未配置 → 消费方默认 SoftDelete）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(50)]
        public string? Action { get; set; }

        /// <summary>恢复窗口天数（软删后期限内可恢复，空 = 未配置 → 消费方默认 30）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        public int? RecoveryWindowDays { get; set; }

        /// <summary>排除已绑定账号（空 = 未配置 → 消费方默认 true 排除）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        public bool? ExcludeBound { get; set; }

        /// <summary>乐观并发令牌（R7——PUT 带 expected version，冲突 409）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public long Version { get; set; }

        /// <summary>最近更新时间（UTC，写路径统一填充）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public DateTime UpdatedAt { get; set; }

        /// <summary>最近更新人（UId，写路径统一填充；系统初始化可为空）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        [MaxLength(100)]
        public string? UpdatedBy { get; set; }
    }
}