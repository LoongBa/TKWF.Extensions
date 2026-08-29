# TKWF.Tools.Tags 标签提取引擎技术规范

**状态**: 核心基础设施 (Core Infrastructure) | **版本**: V4.3 | **框架**: .NET 10

**核心约束**: 零分配 (Zero-Allocation), Native AOT 兼容, 异步友好, 逻辑解耦

---

## 一、 需求分析 (Demand Analysis)

在领域驱动设计 (DDD) 的应用层中，常需对非结构化文本进行特征提取（打标）。

- **性能瓶颈**：传统 `foreach` + `Contains` 匹配在规则过千时性能呈线性下降，且产生大量临时字符串。

- **语义缺失**：无法区分词法边界（如“苹果”与“青苹果”）。

- **冲突处理**：缺乏优先级（Priority）和互斥（Exclusion）逻辑，导致标签结果冗余或矛盾。

- **配置耦合**：规则硬编码在业务逻辑中，无法通过配置中心实现无缝热加载。

## 二、 设计原理 (Design Principles)

本引擎采用 **“数据与处理解耦的流水线架构 (Pipeline Architecture)”**。

### 1. 结构分层

- **输入级 (`ITokenizer`)**：分词坐标生成。采用流式回调，仅传递 `TokenText`（基于 `int` 偏移量的坐标），彻底避免字符串截取分配。

- **处理级 (`ITagMatcher`)**：策略驱动匹配。根据规则的 `MatchMode` 路由任务。

- **清洗级 (`ITagPipelinePostProcessor`)**：结果集后处理。负责根据 `Priority` 在 `ExclusionGroup`（互斥组）内进行择优过滤。

### 2. 性能基石

- **零分配坐标系**：
  
  ```csharp
  public readonly struct TokenText(int startIndex, int length) // 仅占 8 字节
  ```

- **无缝内存视图**：全量匹配逻辑基于 `ReadOnlySpan<char>` 执行，不产生 `Substring` 调用。

- **强类型配置**：深度集成 `DomainOptions.TagRules`，利用 .NET 10 的特性实现极速路由。

---

## 三、 使用说明 (Usage Guide)

### 1. 宿主集成 (Hosting)

利用 `DomainAppBuilder` 扩展方法，引擎会自动关联 `DomainOptions` 中的 `TagRules` 配置，并支持高度灵活的流式组装。

```csharp
// 基础用法：使用默认分词器和内置的 5 种匹配器
builder.UseTagService(); 

// 高级用法 A：使用自定义分词器 (如接入中文 NLP)
builder.UseTagService<JiebaTokenizer>();

// 高级用法 B：使用自定义分词器实例，并链式添加额外的专用匹配器
builder.UseTagService(myTokenizerInstance)
       .AddTagMatcher<AcAutomataMatcher>()  // 添加 AC 自动机匹配器
       .AddTagMatcher<SemanticMatcher>();   // 添加 语义向量匹配器
```

### 2. 业务调用 (Business Logic)

注入门面服务 `TagService`，获取结构化的命中结果。

```csharp
public class AnalysisService(TagService tagService)
{
    public void Process(string input)
    {
        // 极简 API，内部自动调度 Pipeline
        var hits = tagService.GetTags(input); 

        foreach (var hit in hits)
        {
            // hit 包含：维度、标签名、命中位置、原词、优先级等
            _logger.LogInformation("命中标签: {Tag} (位置: {Start})", hit.TagName, hit.StartIndex);
        }
    }
}
```

## 四、 核心组件清单 (Component List)

| **组件**                          | **职责**           | **默认实现**                                                                                       |
| ------------------------------- | ---------------- | ---------------------------------------------------------------------------------------------- |
| **`TagService`**                | 业务单例门面，持有并管理规则集。 | 内置                                                                                             |
| **`ITokenizer`**                | 执行文本拆分，生成坐标流。    | `DefaultTokenizer` (标点/空格切分)                                                                   |
| **`ITagMatcher`**               | 具体的匹配算法实现。       | `TokenExactMatcher`, `RegexMatcher`, `ContainsMatcher`, `StartsWithMatcher`, `EndsWithMatcher` |
| **`ITagPipelinePostProcessor`** | 处理互斥逻辑、裁剪和默认兜底。  | `ExclusionGroupProcessor` (互斥裁剪)<br>`DefaultTagProcessor` (空白维度默认标签生成)                         |

## 五、 扩展与维护规范 (Extension & Maintenance)

### 1. 扩展新算法 (添加 Matcher)

如果后续有更复杂的算法需求（如高并发下的 AC 自动机实现），只需在此架构下增加一个新的 `Matcher` 实现即可：

1. 实现 `ITagMatcher` 接口。

2. 在构建时调用专用扩展方法注册：`builder.AddTagMatcher<MyCustomMatcher>()`。

### 2. 接入第三方 NLP (替换 Tokenizer)

若需接入 **Jieba** 或 **HanLP** 等重型中文分词库：

1. 建立独立的扩展包项目，实现 `ITokenizer`。

2. 在宿主程序入口，直接调用泛型方法替换底层引擎：`builder.UseTagService<JiebaTokenizer>()`。

## 六、 架构演进路线 (Architecture Roadmap)

### 1. 配置文件层级化 (Hierarchical Configuration)

- **当前策略**：本版本**暂时维持扁平结构 (Flat)**。每一条 `TagRule` 均为自包含实体，便于关系型数据库（如 SQL Server / PostgreSQL）单表存储、LINQ 高效过滤以及并行计算 (PLINQ)。

- **演进规划**：未来若维度（Dimension）级元数据（如维度的别名、显示顺序、多语言标签等）大幅膨胀，配置中心将演进为按维度收纳的层级化 JSON 拓扑。层级化能将 `DefaultTagName` 提升为维度级的固有属性，从根本上消除扁平结构下“同一维度意外配置了多个不同默认标签”的配置冲突。

### 2. 业内常用匹配模式与未来扩展

为了让引擎能够无缝支撑未来更复杂的文本挖掘、内容审查以及智能打标场景，流水线已预留扩展位（`TagMatchMode`），规划引入以下工业级匹配策略：

1. **词典批量匹配 (`DictMatch`)**
   
   - **原理**：引入 **Aho-Corasick (AC自动机)** 算法。
   
   - **场景**：针对行业专用庞大词库（成千上万条专有名词、敏感词、高频商品词）。
   
   - **价值**：实现单次文本扫描（$O(n)$ 时间复杂度）全量捞取，彻底规避高并发下频繁循环数万条 `Contains` 规则带来的性能灾难。

2. **邻近度匹配 (`ProximityMatch`)**
   
   - **原理**：结合分词坐标 `TokenText`，计算两个核心词在空间上的相对距离（词距）。
   
   - **场景**：限定上下文语义。例如：“当关键词 `微软` 与 `开源` 共同出现，且它们之间的距离不超过 5 个词时”触发命中。

3. **逻辑组合匹配 (`LogicalCombination`)**
   
   - **原理**：构建简易的表达式解析树，支持复合条件筛选。
   
   - **场景**：Pattern 允许配置类似 `(A OR B) AND (NOT C)` 的布尔逻辑，使单条规则具备复合过滤能力。

4. **模糊纠错匹配 (`FuzzyMatch`)**
   
   - **原理**：利用**编辑距离 (Levenshtein Distance)** 算法或文本相似度度量。
   
   - **场景**：自动兼容用户输入不规范（如错别字、拼写漏字母、英文字母大小写及单复数变体等），在相似度达标时（如 $>0.85$）强制视为命中。

5. **语义向量匹配 (`SemanticMatch`)**
   
   - **原理**：智能标签时代的标配。将标签 Pattern 提前转化为低维稠密向量（Embedding）存入内存向量表或向量库。
   
   - **场景**：突破“字面完全不匹配”的瓶颈。例如：输入文本 *“今天绿油油一片，跌惨了”*，在字面上不包含金融字样，但通过向量余弦相似度计算，能极其精准地打上 `股市/金融` 标签。

## 七、 AI Agent 协作契约 (AI Agent Prompting Guide)

> [!CAUTION]
> 
> **绝对指令：在进行任何代码生成或重构时，严禁触碰以下红线。**

1. **禁分配 (No Allocations)**：禁止在 `Matcher` 的核心循环中使用 `string.Substring`、`string.Split` 或 `Linq.Select`。

2. **禁反射 (No Reflection)**：严禁使用 `Reflection.Emit`。所有组件必须保证 Native AOT 编译通过。

3. **坐标纯粹性**：`TokenText` 必须保持为 `readonly struct`，且**不得**缓存任何字符串引用（`string` 或 `ReadOnlyMemory`）。

4. **接口封闭**：所有的命中结果输出必须为 `IReadOnlyList<TagHit>`，禁止下游业务逻辑修改原始命中数据。

### 文档信息

- **归档日期**: 2026-05-26

- **维护团队**: play / TKW Framework Team

- **审批状态**: 定稿 (V4.3)
