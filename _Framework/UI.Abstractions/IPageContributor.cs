#nullable enable
using System.Collections.Generic;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.UI;

/// <summary>
/// 页面贡献者契约（表现层页面装配——ADR106 决策 1 / D23 §二：声明接口归表现层 Abstractions 包，即本包）。
/// <para><b>实现即意图声明</b>（A+ 模式，对齐 <c>IMenuContributor</c>）：实现本接口即被 SG1b 按接口短名
/// <c>IPageContributor</c> 自动收集，无需任何标记特性；编译器强制实现 <see cref="GetPages"/>，消除漏标静默漏收窗口。</para>
/// <para><b>实现形态硬约束</b>：实现类必须 <c>public</c> 且具备 <c>public</c> 无参构造函数
/// （SG 在消费端编译期 <c>new</c> 实例化，零反射）——违反即 <c>TKWF_SG1b_PAGE002</c>（Error）。</para>
/// <para><b>纯元数据函数</b>：<see cref="GetPages"/> 必须在重复调用时返回等值内容，不得读取环境/时钟/
/// 随机数/外部 IO，不得持有运行时状态；实现体请使用数组或集合字面量（如
/// <c>public IReadOnlyList&lt;PageDescriptor&gt; GetPages() =&gt; [ new(…) ]</c>）。
/// <b>返回类型必须写 <see cref="IReadOnlyList{T}"/> of <see cref="PageDescriptor"/></b>——<c>PageDescriptor[]</c>
/// 不实现接口成员（C# 接口实现不做协变返回）。
/// <b>禁止</b> <c>yield return</c> 与 LINQ 延迟求值——未来 SG 将在编译期解释本方法体以产出结构化
/// <c>PageDescriptor[]</c>（D23 §四 两阶段目标态）。</para>
/// <para><b>归属铁律</b>：实现类只能位于 ① 消费方领域项目（主场景——SG1b 只收集当前编译自身程序集）
/// 或 ② 扩展主包（白名单扩展程序集——其 <c>GeneratedPageCatalog</c> 被聚合）。
/// <b>严禁</b>放在 <c>{Ext}.UI.{TechStack}</c> RCL 内——RCL 不在领域项目编译内、也不在白名单聚合内，
/// 页面会静默不注册。RCL 只承载组件/静态资产/契约表。</para>
/// <para>页面字段语义（九字段冻结表）、路由与权限键约定、诊断现状（PAGE002 生效 / PAGE003·004 休眠）、
/// R2 增长上限与端点前置，见仓库 <c>docs/AE-Kit/skills/tkwf-extension-ui/references/page-contract.md</c>
/// （三个 UI skill 的单一事实源）。</para>
/// </summary>
public interface IPageContributor
{
    /// <summary>
    /// 返回本模块贡献的页面描述（纯元数据——不加载数据、不做授权判断；页面可见性由
    /// <c>IPageGuard</c>、业务授权由领域 <c>User.Use&lt;TService&gt;()</c> 的 AuthorityFilter 分别负责）。
    /// </summary>
    IReadOnlyList<PageDescriptor> GetPages();
}
