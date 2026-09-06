using TKW.Framework.Domain;

namespace TKWF.Ext.Dashboard
{
    /// <summary>
    /// 仪表盘扩展配置——经 <see cref="OptionsAttribute"/> 声明配置节，SG1 生成绑定（消费方启动期自动
    /// <c>services.Configure&lt;DashboardOptions&gt;(configuration.GetSection("TKWF:Dashboard"))</c>）。
    /// <para>配置节：<c>TKWF:Dashboard</c>。Metrics 规格根目录经 <see cref="IConfiguration"/> 直读 <c>TKWF:Metrics:SpecRoot</c>
    /// （Oracle C1：零冗余零新抽象——Dashboard 不引 TKWF.Ext.Metrics 扩展项目）。</para>
    /// </summary>
    [Options("TKWF:Dashboard")]
    public class DashboardOptions
    {
        /// <summary>Dashboard 定义文件根目录（默认 "docs/dashboard-specs"，相对消费方仓库根）。</summary>
        public string SpecRoot { get; set; } = "docs/dashboard-specs";
    }
}
