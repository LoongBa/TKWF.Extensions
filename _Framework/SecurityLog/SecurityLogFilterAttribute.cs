using System;
using System.Collections.Generic;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TKW.Framework.Core.AuthController;
using TKW.Framework.Domain;
using TKW.Framework.Domain.AuthController;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Interception;

namespace TKWF.Ext.SecurityLog
{
    /// <summary>
    /// 安全日志采集过滤器（Oracle C1 选定路径——全局注册 + CanWeGo 正向白名单判定，对齐 <c>AuditLogFilterAttribute</c> 先例）。
    /// <para>经 <c>FilterBuilder.AddSecurityLog()</c> 全局注册（消费方 opt-in）——对 Domain AOP 管线内调用生效：
    /// <c>AuthController</c> 是 <c>DomainControllerBase</c> 派生，经 <c>User.Use&lt;IAuthController&gt;()</c> 调用时
    /// 由装饰器路由进 <see cref="StaticDomainInterceptor"/>，本过滤器随 GlobalFilters 执行。</para>
    /// <para><b>CanWeGo 正向白名单</b>（区别于 AuditLog 的声明式排除）：仅拦截安全方法——
    /// 目标类型 <c>IAuthController</c>/<c>IPasswordResetFlow</c> 实现，或方法名命中安全方法集合。</para>
    /// <para><b>Result 判定</b>：PostProceed 正常返回 = Success（RegisterResult/bool/ResetResult 返回值业务失败 = Failed）；
    /// 异常路径（<see cref="IExceptionAwareFilter"/>）仅 <see cref="AuthenticationException"/> 记 Failed + 脱敏 Detail，
    /// 其他异常不记录（保持原异常语义，不吞不遮蔽）。</para>
    /// <para><b>Lockout 判定（Oracle C4）</b>：AuthenticationException 消息含"锁定"关键字 → 记 Lockout 事件。</para>
    /// </summary>
    public class SecurityLogFilterAttribute<TUserInfo> : DomainFilterAttribute<TUserInfo>, IExceptionAwareFilter
        where TUserInfo : class, IUserInfo, new()
    {
        /// <summary>客户端 IP 的 ambient 键（对齐主框架 <c>GetClientIp</c>——<see cref="IAmbientContext"/>["ClientIp"]）。</summary>
        public const string ClientIpAmbientKey = "ClientIp";

        /// <summary>客户端 UserAgent 的 ambient 键（约定键，无则记录 null）。</summary>
        public const string UserAgentAmbientKey = "UserAgent";

        /// <summary>Bag 键：异常路径（StaticDomainInterceptor 设置）。</summary>
        private const string ExceptionBagKey = "__Exception";

        /// <summary>Lockout 判定关键字（对齐主框架"账户已锁定，请联系管理员解锁"异常消息）。</summary>
        private const string LockoutKeyword = "锁定";

        /// <summary>
        /// 安全方法名白名单（C1 正向判定）：AuthController 6 安全方法 + IPasswordResetFlow 2 方法。
        /// </summary>
        public static readonly HashSet<string> SecurityMethodNames = new(StringComparer.Ordinal)
        {
            "LoginByPasswordAsync",
            "LoginByContextAsync",
            "LogoutAsync",
            "RequestChallengeAsync",
            "RegisterSecureAsync",
            "ChangePasswordSecureAsync",
            "InitiateResetAsync",
            "CompleteResetAsync",
        };

        /// <summary>方法名 → 安全事件类型映射（白名单命中且非"锁定"异常时的 EventType）。</summary>
        private static readonly Dictionary<string, string> EventTypeByMethod = new(StringComparer.Ordinal)
        {
            ["LoginByPasswordAsync"] = SecurityLogEventTypes.Login,
            ["LoginByContextAsync"] = SecurityLogEventTypes.Login,
            ["LogoutAsync"] = SecurityLogEventTypes.Logout,
            ["RequestChallengeAsync"] = SecurityLogEventTypes.Challenge,
            ["RegisterSecureAsync"] = SecurityLogEventTypes.Register,
            ["ChangePasswordSecureAsync"] = SecurityLogEventTypes.PasswordChange,
            ["InitiateResetAsync"] = SecurityLogEventTypes.PasswordReset,
            ["CompleteResetAsync"] = SecurityLogEventTypes.PasswordReset,
        };

        /// <summary>
        /// C1 正向白名单判定：目标类型（IAuthController / IPasswordResetFlow 实现）或方法名集合。
        /// </summary>
        public override bool CanWeGo(DomainInvocationWhereType invocationWhere, DomainContext<TUserInfo> context)
        {
            var target = context.Invocation.Target;
            return target is IAuthController
                || target is IPasswordResetFlow
                || SecurityMethodNames.Contains(context.Invocation.MethodName);
        }

        /// <summary>采集无需前置逻辑（纯后置写事件）。</summary>
        public override Task PreProceedAsync(DomainInvocationWhereType where, DomainContext<TUserInfo> context)
            => Task.CompletedTask;

        /// <summary>
        /// 方法执行后写安全事件。异常路径（Bag["__Exception"]）由 <see cref="IExceptionAwareFilter"/> 保证触发。
        /// <para>零开销语义：Options.Enabled=false 时直接返回（不解析 Store/不构造事件）；Store 未注册时跳过；
        /// 过滤器自身任何异常都不阻断业务（审计旁路）。</para>
        /// </summary>
        public override async Task PostProceedAsync(DomainInvocationWhereType where, DomainContext<TUserInfo> context)
        {
            try
            {
                // ① Options：Enabled=false → 零开销直接返回
                var options = context.DomainUser.GetOptionalService<IOptions<SecurityLoggingOptions>>()?.Value;
                if (options == null || !options.Enabled) return;

                // ② Store 未注册（消费方未启用/自定义移除）→ 跳过
                var store = context.DomainUser.GetOptionalService<ISecurityLogStore>();
                if (store == null) return;

                // ③ 构造事件（Result 判定 + Lockout 判定 + 脱敏 + 尝试用户名 + IP/UA/CorrelationId）
                var entry = BuildEntry(context);
                if (entry == null) return;

                // ④ 事件类型开关（EventTypes 子集，空 = 全部）
                if (options.EventTypes.Count > 0 && !options.EventTypes.Contains(entry.EventType)) return;

                // ⑤ 落库（Store 内部异常静默；此处再兜底——绝不阻断认证流程）
                await store.SaveAsync(entry, CancellationToken.None);
            }
            catch
            {
                // 过滤器自身异常绝不阻断业务（审计旁路语义）
            }
        }

        /// <summary>
        /// 构造安全事件。返回 null 表示不应记录（未知方法/非认证异常）。
        /// </summary>
        private static SecurityLogEntry? BuildEntry(DomainContext<TUserInfo> context)
        {
            var methodName = context.Invocation.MethodName;
            var exception = context.Invocation.Bag.GetValueOrDefault(ExceptionBagKey) as Exception;

            // 白名单外的方法名 → 不记录（安全兜底；白名单命中必定有已知映射）
            if (!EventTypeByMethod.TryGetValue(methodName, out var eventType))
                return null;

            string result;
            string? detail = null;

            if (exception != null)
            {
                // 异常路径：仅 AuthenticationException 记 Failed（登录/锁定语义）；其他异常不记录（保持原异常语义）
                if (exception is not AuthenticationException authEx) return null;

                // C4 Lockout 判定：异常消息含"锁定"关键字 → 记 Lockout 事件
                if (authEx.Message.Contains(LockoutKeyword, StringComparison.Ordinal))
                    eventType = SecurityLogEventTypes.Lockout;

                result = SecurityLogEventTypes.ResultFailed;
                detail = authEx.Message; // 脱敏：仅异常消息（AuthController 异常消息不含密码/令牌明文，绝不含凭据输入）
            }
            else
            {
                // 成功路径：正常返回 = Success；返回值承载业务失败（RegisterResult/bool/ResetResult）→ Failed
                result = SecurityLogEventTypes.ResultSuccess;
                switch (context.Invocation.ReturnValue)
                {
                    case RegisterResult r: result = r.Success ? SecurityLogEventTypes.ResultSuccess : SecurityLogEventTypes.ResultFailed; detail = r.Message; break;
                    case ResetResult r: result = r.Success ? SecurityLogEventTypes.ResultSuccess : SecurityLogEventTypes.ResultFailed; detail = r.Message; break;
                    case bool b: result = b ? SecurityLogEventTypes.ResultSuccess : SecurityLogEventTypes.ResultFailed; break;
                }
            }

            // 尝试用户名：优先请求输入（登录失败 = 防枚举语义下保留审计来源）；回退当前认证用户
            var userName = GetAttemptedUserName(context.Invocation);
            if (string.IsNullOrEmpty(userName))
                userName = context.DomainUser.UserInfo?.UserName ?? "";

            // 用户 ID：UserInfo.UserIdString 数值化解析（游客/匿名不解析为 long → null；登录成功后回填）
            long? userId = null;
            if (context.DomainUser.UserInfo != null
                && long.TryParse(context.DomainUser.UserInfo.UserIdString, out var parsedId))
            {
                userId = parsedId;
            }

            var (ipAddress, userAgent) = GetClientInfo(context);

            return new SecurityLogEntry(
                EventType: eventType,
                EventCategory: SecurityLogEventTypes.CategoryAuthentication,
                UserName: userName,
                UserId: userId,
                IpAddress: ipAddress,
                UserAgent: userAgent,
                Result: result,
                Detail: detail,
                CorrelationId: context.CorrelationId);
        }

        /// <summary>
        /// 从方法参数提取尝试用户名（各安全方法 args[0] 语义：string / LoginContextInput / RegisterSecureInput）。
        /// </summary>
        private static string? GetAttemptedUserName(InvocationContext invocation)
        {
            if (invocation.Arguments.Length == 0) return null;
            return invocation.Arguments[0] switch
            {
                string s => s,
                LoginContextInput lci => lci.UserName,
                RegisterSecureInput rsi => rsi.UserName,
                // LogoutAsync(bool broadcast)/ChangePasswordSecureInput 无用户名载体——回退当前认证用户
                //（避免 record/bool ToString 污染 UserName）
                _ => null,
            };
        }

        /// <summary>
        /// 经 <see cref="IAmbientContext"/> 采集客户端 IP 与 UserAgent（对齐 AuthController.GetClientIp 语义；
        /// UserAgent 为约定键，无则 null）。ambient 解析异常忽略（IP/UA 为 null）。
        /// </summary>
        private static (string? IpAddress, string? UserAgent) GetClientInfo(DomainContext<TUserInfo> context)
        {
            string? ipAddress = null;
            string? userAgent = null;
            try
            {
                var ambient = context.DomainUser.GetOptionalService<IAmbientContext>();
                if (ambient != null)
                {
                    if (ambient.TryGet<string>(ClientIpAmbientKey, out var ip) && !string.IsNullOrWhiteSpace(ip))
                        ipAddress = ip;
                    if (ambient.TryGet<string>(UserAgentAmbientKey, out var ua) && !string.IsNullOrWhiteSpace(ua))
                        userAgent = ua;
                }
            }
            catch
            {
                // 忽略 ambient 解析异常（未注册/作用域不可用）——IP/UA 保持 null
            }
            return (ipAddress, userAgent);
        }
    }
}
