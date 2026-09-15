# ADR-FileManagement-用户级配额所有权语义

> **版本**：V0.3.0（目标）
> **状态**：📋 提议（随 v0.3.0 开发方案，Oracle 评审 bg_670b41d1 定案）
> **关联**：`v0.3.0-FileManagement-用户级配额-开发方案.md`（Oracle PASS WITH CONDITIONS 11 条件）、跟踪 §五 #5、`ADR-扩展数据访问红线.md`（DataService 聚合红线）
> **关键字**：FileManagement、用户级配额、OwnerId、所有权语义、CanUpdate、数值型 UserId

---

## 目的与目标

裁定 FileManagement 用户级配额（v0.3.0）的**所有权归属语义**：`ManagedFileEntity.OwnerId` 何时写入、何时不可变、版本化/回滚分支如何处理、配额如何按所有者计费与释放。目标状态（3 句内）：`OwnerId` 仅在**新建文件**分支写入且**一经写入不可变**（`CanUpdate=false`）；版本化新版本与回滚均**不动 OwnerId**（谁首传谁拥有，新版本字节计入原 owner 配额）；用户配额**仅支持数值型 UserId**（非数值静默降级到全局配额兜底 + Warning），删除主行即释放配额。

---

## 问题

### 问题现象

- FileManagement v0.2.0 配额仅目录级（`MaxFilesPerFolder`/`MaxFolderSizeBytes`）+ 全局级（`MaxTotalSizeBytes`），无用户维度——多用户场景无法按用户限流。
- 方案拟增 `OwnerId` 列实现用户配额，但**所有权语义存在歧义**：
  - 用户 B 对用户 A 的文件上传**同名不同内容新版本**——新版本字节算谁的配额？（选项 ①：所有权转移给 B；选项 ②：保留给 A）
  - `RollbackFileAsync` 回滚后 `OwnerId` 是否变化？
  - `CanUpdate` 取值取决于上述语义（可转移 → true；保留 → false）。
- 若无明确裁定，实施时易选错语义（默认"版本上传者夺走所有权"看似自然，实则引入权限/权益模型复杂化）。

### 触发场景

- 用户 B 上传用户 A 文件的**新版本**（同名不同内容）——配额归属必须确定。
- 用户 A 删除文件/回滚版本——配额释放/保留语义必须确定。
- GUID/字符串 UserId 消费方启用用户配额——降级行为必须确定（否则静默不生效无提示）。

### 现有方案不足

- **无先例可循**：Settings/FeatureManagement 的 `IDomainUser` 注入仅用于分层读取/特性值解析，无"归属写入 + 配额计费"语义——本 ADR 是首个用户归属维度决策。
- **"所有权转移"的隐式陷阱**：若版本上传即转移所有权——原 owner 配额释放但**仍可读/删该文件**（权限模型不变），权益与配额错位；新 owner 未上传却"付费"（新版本字节计入其配额），语义混乱。
- **非数值 UserId 静默失败**：`long.TryParse` 失败若仅返回 null，GUID 用户配额配置不生效且无提示——须明示约束 + 告警。

---

## 使用场景

1. **多用户网盘**（常态）：用户 A 上传文件 → `OwnerId=A`；A 达配额上限后上传拒绝；用户 B 上传 A 文件的同名新版本 → **版本字节计入 A 配额**（B 不付费，文件仍归 A）；A 删除文件 → 配额释放。
2. **匿名/系统上传**：`IsAuthenticated=false` 或 `IsSystemActor=true` → `OwnerId=null`，用户配额跳过，全局配额兜底。
3. **非数值 UserId 消费方**：用户配额配置不生效（静默降级到全局），首次解析失败 Warning 提示——**使用指南明示此约束**，消费方知情选择。
4. **不适用**：`OwnerId` 不回填存量数据（历史文件不计入任何用户配额）；单用户应用无需用户配额（保持目录/全局配额）。

---

## 决策

### 1. 所有权保留（Oracle 条件 2 定案）

**`OwnerId` 仅在 `UploadFileAsync` 新建文件分支写入，一经写入不可变**：

- `[Column(Position = 12, IsNullable = true, CanUpdate = false)]`——`CanUpdate=false` 从数据库层禁止更新（对齐 `CreateTime`/版本表 `CreateTime` 先例）。
- **版本化 Upsert 新版本分支不动 OwnerId**——文件归首传者；新版本字节计入原 owner 配额（`SumSizeByOwnerAsync` 按主表 `Size` 单指针求和，不重复计版本行）。
- **RollbackFileAsync 不动 OwnerId**——回滚是指针切换（主表 `StoredPath/Sha256/Size/UpdateTime` 更新），非新上传，不改变归属。
- **语义一致性**：谁拥有文件谁付费——原 owner 的配额涵盖其文件全部版本的最新 Size；新上传者不因替旧文件传新版而付费（他并未"拥有"该文件）。

> **否决"所有权转移"**（Option ①）：新上传者成为 owner → 原 owner 配额释放但**权限/删除权不变**（权益错位）+ 新 owner"替人付费"语义混乱 + `CanUpdate=true` 引入所有权可变的额外复杂度。无消费方诉求支撑，否决。

### 2. 删除即释放配额

`DeleteFileAsync` 删除主表行（含 `OwnerId`）→ `SumSizeByOwnerAsync`/`CountByOwnerAsync` 按主表聚合自然不含已删行——**配额自动释放，无需额外处理**。版本行/Blob 删除是物理清理（既有 v0.2.0 语义），不影响配额聚合（版本行不参与聚合）。

### 3. 用户配额仅支持数值型 UserId（Oracle 条件 5）

- `long.TryParse(_domainUser.UserId, out var uid)` 失败 → `OwnerId=null` → 用户配额静默跳过（全局兜底）。
- **约束文档化**：使用指南明示"用户配额仅支持数值型 UserId；GUID/字符串 ID 消费方用户配额不生效（降级全局）"。
- **告警**：首次解析失败 `_logger.LogWarning` 一次（便于消费方排查"配额配置了但不生效"）。

### 4. 系统账号排除（Oracle 条件 6）

`_domainUser.IsSystemActor=true` → `OwnerId=null`（系统上传不计入任何用户配额——后台任务/系统初始化上传不占用户额度）。

### 5. 检查顺序契约（Oracle 条件 7）

`EnforceQuotaAsync` 顺序：**用户计数 → 用户容量 → 目录计数 → 目录容量 → 全局容量**（最具体 → 最兜底；用户维度早失败省一次目录聚合 IO）。在现有方法顶部插入用户块，其余 3 键不动。

---

## 备选方案

### 所有权转移（Option ①）
- 版本上传者成为 owner，原 owner 配额释放。
- **否决**：权限/删除权不随配额转移（权益错位）；新 owner"替人付费"；`CanUpdate=true` 复杂度；无消费方诉求。

### OwnerId 随版本行冗余（版本表也存 OwnerId）
- 每版本记录上传者，配额按"我上传的版本字节"计费。
- **否决**：配额聚合复杂度暴增（版本行求和 vs 主表单指针）；权益语义更模糊（文件本体归谁 vs 版本字节归谁）；超出用户配额最小范围。

### 存量数据回填（历史文件按 uploader_name 反推 OwnerId）
- **否决**：`UploaderName` 现为 null（从未写入），反推不可行；批处理脚本侵入消费方数据；"启用前文件不占额度"是可接受的权衡。

---

## 后果

### 正面
- 所有权语义单一明确（谁首传谁拥有），配额计费无歧义；`CanUpdate=false` 数据库层保护。
- 删除即释放（聚合按主表）零额外逻辑；版本化/回滚不动 OwnerId 零迁移成本。
- 非数值 UserId 降级显式化 + Warning，消费方知情。

### 负面
- **新版本字节计入原 owner 配额**——若原 owner 配额已满，用户 B 无法为 A 的文件传新版（被 A 配额卡住）——语义上正确（文件归 A），但**协作场景受限**（需 A 提升配额或转移所有权——超出本 ADR 范围，消费方权限扩展配合）。
- 非数值 UserId 消费方**用户配额不可用**（仅全局）——约束明确，接受。
- 存量文件不计入用户配额——启用前文件不占额度（可接受的权衡）。

### 缓解
- 协作受限场景：使用指南注明"新版本计入文件所有者配额；协作上传需所有者配额充足或消费方扩展所有权转移"。
- 非数值 ID：使用指南醒目约束 + Warning 日志。

---

## 变更记录

| 日期 | 状态 | 说明 |
|------|------|------|
| 2026-09-16 | 📋 提议 | 初稿——随 v0.3.0 开发方案 Oracle 评审（bg_670b41d1）定案：所有权保留 + CanUpdate=false + 删除即释放 + 非数值降级 + 系统账号排除 |
