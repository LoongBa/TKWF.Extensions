# TKWF.Ext.Calendar

> TKWF 扩展：**日历/排程**——日历定义、事件维护与重复规则展开（对标 Google Calendar 基础子集）。
> 重复规则算法零依赖收纳主框架 `TKW.Framework.Utility.Calendar.Recurrence`（对齐 Metrics/Tags 收纳先例），数据访问全部委托 SG1 DataService（红线合规）。

## 定位

| 项 | 说明 |
|----|------|
| 包名 | `TKWF.Ext.Calendar` |
| 版本 | v0.1.0（独立起点） |
| 依赖 | `TKWF.Domain` + `TKWF.Utility`（重复规则算法）+ SG1（框架既有） |
| 数据 | 表 `Calendar` + `CalendarEvent`（框架 `SyncTables` 统一建表） |

## 架构分层

```
CalendarEntity / CalendarEventEntity                     # SG1 声明式实体（partial + [DomainGenerateCode]）
├── CalendarEntityDataService / CalendarEventEntityDataService  # SG1 DataService 骨架（C1 分路范围查询）
├── ICalendarStore / CalendarStore                       # internal 存储抽象（委托 DataService + SQLite DateTime 规范化）
└── ICalendarManager / CalendarManager                   # 公开门面（事务包裹 + 重复展开 + occurrence 合并）

TKW.Framework.Utility.Calendar.Recurrence                # 主框架 Utility：RecurrenceRule / RecurrenceExpander（纯算法零依赖）
```

- **数据访问红线**：Store/Manager 不注入 `IFreeSql`/`IEntityDAC`——全部经 DataService 委托；事务由 Manager 层统一管理。
- **SQLite DateTime 规范化**：FreeSql SQLite 把 UTC DateTime 存为本地墙钟（+8），读出 `Unspecified`——Store 读取统一 `NormalizeUtc`（`Unspecified→Local→UTC`）还原真实 UTC，occurrence 计算/比较绝对正确。
- **事务包裹**：`CreateCalendar`/`CreateEvent`/`UpdateEvent`/`DeleteCalendar` 多步写经 `ITransactionManager` `BeginAsync → CommitAsync / 失败 RollbackAsync`。

## 核心能力

### 日历（`IOrganizationUnitManager` 等价物：`ICalendarManager`）

| API | 说明 |
|-----|------|
| `CreateCalendarAsync(code, name, description?, color?)` | 创建日历（Code 库级唯一） |
| `UpdateCalendarAsync(id, name?, description?, color?, isEnabled?)` | 更新（含启停） |
| `DeleteCalendarAsync(id)` | **删除保护**：有事件 → 拒绝（`InvalidOperationException`） |
| `GetCalendarAsync(id)` / `GetCalendarsAsync()` | 查询 |

### 事件 + 重复规则

| API | 说明 |
|-----|------|
| `CreateEventAsync(calendarId, title, startUtc, endUtc?, ..., allDay?, recurrenceRule?)` | 创建事件——引用守卫（日历存在）+ `EndUtc ≥ StartUtc` 校验 + **UTC Kind 校验** + 规则串 `Parse` fail-fast + `RecurrenceEndUtc` 同源推导 |
| `UpdateEventAsync(id, ...)` | 更新（改规则/时间 → `RecurrenceEndUtc` 重算；改日历引用守卫） |
| `DeleteEventAsync(id)` | 删除（occurrence 不落库——展开是查询时计算） |
| `GetOccurrencesAsync(calendarId?, fromUtc, toUtc, maxCount?)` | **occurrence 查询**：单次命中 + 重复展开 + 合并排序（`StartUtc` 升序 + `EventId` 次级键）+ 截断标志 |

### 重复规则语法（RRULE 子集）

```
FREQ=DAILY|WEEKLY|MONTHLY|YEARLY          # 必填
INTERVAL=n                                 # 每 n 个周期（默认 1）
COUNT=n | UNTIL=yyyyMMdd'T'HHmmss'Z'      # 互斥（同现 fail-fast）
BYDAY=MO,TU,WE,TH,FR,SA,SU                # 仅 WEEKLY（多值逗号分隔）
```

- **展开语义**：绝对索引（月末钳制 31→2/28 无级联漂移）；DTSTART 恒为第一 occurrence；半开区间 `[from, to)`；UTC 计算。
- **限制**：非 WEEKLY 的 BYDAY、序数形式（`1MO`）、未知子句（BYMONTH/BYSETPOS/WKST 等）→ `Parse` 抛 `FormatException`（fail-fast）。
- **边界**：`COUNT`/`UNTIL` 均无 → 无限规则（查询侧 `maxCount` 上限 + 展开 `MaxOccurrenceCount` 防 DoS）。

## 约束与语义

- **UTC 契约**：所有时间参数必须 `DateTimeKind.Utc`（`EnsureUtcKind` fail-fast——传 Local/Unspecified 抛 `ArgumentException`）。时区/夏令时不做（v0.1.0 划界），展示层自行转换本地时间。
- **AllDay 语义**：起点归一为 UTC 日期午夜 + `EndUtc = 次日 00:00`；occurrence `EndUtc = StartUtc + 1 天`。
- **IsEnabled 过滤**：`GetOccurrencesAsync` 默认仅返回启用日历的事件；`GetCalendarsAsync` 返回全部。
- **occurrence 时长**：`EventOccurrence.EndUtc = occurrence 起点 + 事件时长偏移`（`EndUtc - StartUtc`；AllDay +1 天）。
- **全局上限**：occurrence 合并总收集 `MaxTotalOccurrences = 1_000_000` 硬上限（防 DoS）+ per-event `maxCount`。

## 启用方式（v4.9.85+）

```csharp
using TKWF.Ext.Calendar;

[TKWFEnabledExtension(typeof(CalendarExtensionInitializer<>))]
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }
```

三钩子自动接线。DI 一律 `TryAddScoped`——消费方自定义 `ICalendarManager`/`ICalendarStore` 实现优先。

## 数据模型

```sql
Calendar(id BIGINT PK, code VARCHAR(128) UNIQUE, name VARCHAR(128), description VARCHAR(512) NULL,
         color VARCHAR(16) NULL, is_enabled BOOL, create_time TIMESTAMP, update_time TIMESTAMP)
CalendarEvent(id BIGINT PK, calendar_id BIGINT, title VARCHAR(256), description VARCHAR(1024) NULL,
              location VARCHAR(256) NULL, start_utc TIMESTAMP, end_utc TIMESTAMP NULL,
              all_day BOOL, recurrence_rule VARCHAR(512) NULL, recurrence_end_utc TIMESTAMP NULL,
              create_time TIMESTAMP, update_time TIMESTAMP)
-- 索引：UX_Calendar_Code / IX_CalendarEvent_CalendarId_StartUtc / IX_CalendarEvent_CalendarId_RecurrenceEndUtc
```

- 唯一约束 `UX_Calendar_Code`（Code）；事件索引支撑 C1 分路查询（单次 `StartUtc` 下推 + 重复 `RecurrenceEndUtc` 预筛）。
- `UserId` 无（v0.1.0 无参与者）；`RecurrenceRule` 存规范规则串（非 JSON）；`RecurrenceEndUtc` 与展开算法同源推导（冗余加速范围过滤）。
- 生产建表：框架 `SyncTables` 统一托管（V4.9.92 ADR49），**无 VEntity/无 DBA 手工 DDL 前置**。

## 后续演进（v0.2.0+ 候选）

参与者/邀请（对齐 Notifications 通道抽象）；提醒（`ICalendarReminderNotifier` + 事件驱动）；occurrence 供给 BackgroundJobs 定时调度；完整 RRULE 子句（BYMONTH/BYSETPOS/WKST）；时区（NodaTime 评估）。
