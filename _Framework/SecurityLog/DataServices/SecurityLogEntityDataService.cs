using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.SecurityLog;
using TKWF.Ext.SecurityLog.DTOs;

namespace TKWF.Ext.SecurityLog;

/// <summary>数据服务：安全日志表实体——记录认证/授权相关安全事件（登录成功/失败、登出、改密、密码重置、账户锁定、注册、挑战）。</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 SecurityLogEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
//
// 【只增不改语义（Oracle C2）】
// 安全日志为追加写日志——本分部类刻意【不】提供任何 Update/Delete 业务方法（对比 AuditLogging 的
// AdminDeleteAsync），且 DataService 不标注 [GenerateController(FromDataService=true)]：
//   ① 无 Update/Delete 公开业务方法（.g.cs 内部转发访问器不构成业务方法，仅同程序集 Store 可用）；
//   ② 无 [GenerateController] → 不生成任何 REST/GraphQL 管理端点。
// 唯一写路径 = SecurityLogStore.SaveAsync → EntityCreateAsync（追加）。
// 查询计数直接用基类 DomainReadOnlyDataServiceBase.CountAsync（public virtual，QueryForUser 路径）——
// 不再手写 CountAsync（避免隐藏基类成员，且基类实现为 SQL COUNT 更高效）。
partial class SecurityLogEntityDataService(IDomainUser user, IEntityDAC<SecurityLogEntity> dac)
        : DomainDataServiceBase<SecurityLogEntity, SecurityLogEntityDto>(user, dac, hasSoftDelete:false)
{
}
