# TKWF.Ext.OrganizationUnit

> TKWF 扩展：**组织单元管理**——树形部门/团队/分组定义、维护与用户归属管理（对标 ABP OrganizationUnits）。
> 零第三方依赖（FreeSql/SG1 为框架既有），数据访问全部委托 SG1 DataService（红线合规）。

## 定位

| 项 | 说明 |
|----|------|
| 包名 | `TKWF.Ext.OrganizationUnit` |
| 版本 | v0.1.0（独立起点） |
| 依赖 | `TKWF.Domain` + SG1（框架既有） |
| 数据 | 表 `OrganizationUnit` + `OrganizationUnitUser`（框架 `SyncTables` 统一建表） |

## 架构分层

```
OrganizationUnitEntity / OrganizationUnitUserEntity     # SG1 声明式实体（partial + [DomainGenerateCode]）
├── OrganizationUnitEntityDataService                   # SG1 DataService 骨架（GetByCode/GetAll/GetByIds）
├── OrganizationUnitUserEntityDataService               # SG1 DataService 骨架（关联 CRUD/查询）
├── IOrganizationUnitStore / OrganizationUnitStore      # internal 存储抽象（委托 DataService，异常自然传播）
└── IOrganizationUnitManager / OrganizationUnitManager  # 公开门面（业务规则 + 事务包裹 + 引用守卫）
```

- **数据访问红线**：Store/Manager 不注入 `IFreeSql`/`IEntityDAC`——全部经 DataService 委托；事务由 Manager 层统一管理（Store 不触碰 `ITransactionManager`）。
- **事务包裹（C1）**：`CreateAsync`/`MoveAsync`/`DeleteAsync` 多步写路径经 `ITransactionManager` `BeginAsync → CommitAsync / 失败 RollbackAsync`（using scope 范式，对齐 Approval CONDITION-1）。Move 子树重算与 Delete（前置检查 + 清 junction + 物理删）要么全提交要么全回滚，无半更新。
- **异常语义**：Store 异常自然传播（不静默）；Manager 将唯一约束冲突转业务异常、将业务规则违反抛 `InvalidOperationException`。

## 核心能力

### 树形 OU（`IOrganizationUnitManager`）

| API | 说明 |
|-----|------|
| `CreateAsync(code, name, parentId?, sortOrder?)` | 创建根/子 OU；Level/Path 自动计算（根=0、`/Code/`；子=父+1、`父Path+Code/`）；SortOrder 默认同级末尾 max+1 |
| `UpdateAsync(id, name?, sortOrder?, isEnabled?)` | 更新名称/排序/启停；**Code 不可改**（路径不变量） |
| `DeleteAsync(id)` | **删除保护**：有子节点或有关联用户 → 拒绝（`InvalidOperationException`）；空 OU 事务内清 junction + 物理删除 |
| `MoveAsync(id, newParentId?)` | 移动（null=设为根）；**循环防护**（移入自身/后代拒绝）；BFS 重算整棵子树 Level/Path；移动节点追加新父末尾 |
| `GetTreeAsync()` | 全树组装（内存，SortOrder 升序）；孤儿节点（父不存在）抛异常 |
| `GetSubTreeAsync(id)` | 子树（**含自身**，Path 前缀匹配） |
| `GetAncestorsAsync(id)` | 祖先链（根→直接父，不含自身；面包屑） |

### 用户关联

| API | 说明 |
|-----|------|
| `AssignUserAsync(ouId, userId)` | 分配用户；重复分配 → 业务异常"该用户已在此组织单元"（唯一约束转换） |
| `UnassignUserAsync(ouId, userId)` | 解除（幂等，不存在静默成功） |
| `GetUserIdsInOrganizationUnitAsync(ouId, includeDescendants)` | 双向查询①：OU（含/不含子孙）下用户 Id 列表（两步：子树 → junction，去重） |
| `GetOrganizationUnitIdsForUserAsync(userId)` | 双向查询②：用户所属 OU Id 列表 |

## 约束与语义

- **Code 白名单（C3）**：`[A-Za-z0-9_.-]`（拒绝 `%`/`_` 之外的 LIKE 通配符与空白——从源头保证 Path 前缀匹配无字符歧义）；拒绝纯点组合 `.`/`..`。Name 可含任意字符（不进 Path）。
- **Path 长度守卫（C3）**：Create/Move 路径重算后断言 ≤1024（列 MaxLength），超限抛 `InvalidOperationException`（非 DB OverflowError）。
- **删除语义（C2）**：物理删除（`hasSoftDelete:false` + 实体不声明 `IsDeleted`）——已删 Code 可复用；已删 OU 的引用守卫：建子/分配用户/子树查询 → 拒绝。
- **并发边界（P2）**：并发 Create/Move 交叠可能产生基于过期 parent 快照的 Level/Path 偏差——低风险低频场景，依赖库级唯一约束（Code/junction）+ 最后写入者生效，不保证读-改-写原子（组织架构百级、写频次低）。
- **`includeDescendants` 语义（P3）**：`true` 含 OU 自身 + 全部子孙。

## 启用方式（v4.9.85+）

扩展 DLL 被引用后 SG1 发现，但**发现 ≠ 启用**——消费方须在自身领域初始化器上白名单声明：

```csharp
using TKWF.Ext.OrganizationUnit;

[TKWFEnabledExtension(typeof(OrganizationUnitExtensionInitializer<>))]
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }
```

三钩子（`ConfigureServices`/`ConfigureFilters`/`InitializeAsync`）自动接线。DI 一律 `TryAddScoped`——消费方自定义 `IOrganizationUnitManager`/`IOrganizationUnitStore` 实现优先。

## 数据模型

```sql
OrganizationUnit(id BIGINT PK, code VARCHAR(128) UNIQUE, parent_id BIGINT NULL, name VARCHAR(128),
                 sort_order INT, level INT, path VARCHAR(1024), is_enabled BOOL,
                 create_time TIMESTAMPTZ, update_time TIMESTAMPTZ)
OrganizationUnitUser(id BIGINT PK, organization_unit_id BIGINT, user_id VARCHAR(128),
                     create_time TIMESTAMPTZ, update_time TIMESTAMPTZ,
                     UNIQUE(organization_unit_id, user_id))
```

- `parent_id` 自引用（null=根）；`level`/`path` 为物化路径冗余（写入维护，读取零写）。
- `UserId` 为 **string**（对齐 `IUserInfo.UserIdString`，跨 Identity 兼容）。
- 唯一约束：`UX_OrganizationUnit_Code`（Code）+ `UX_OrganizationUnitUser_User_OU`（防重复关联）。
- 生产建表：框架 `SyncTables` 统一托管（V4.9.92 ADR49），**无 VEntity/无 DBA 手工 DDL 前置**。

## 后续演进（v0.2.0+ 候选）

VEntity `vw_UserOrganizationUnitView`（大数据量 JOIN 分页）；OU 软删除 + 回收站；多租户隔离；岗位/职级语义。
