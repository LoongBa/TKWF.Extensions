using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Permissions
{
    /// <summary>
    /// V4.9.72 (扩展机制业务模块 W4)：权限扩展初始化器——经 [TKWFExtension] 被 SG1 发现，
    /// 三钩子接线（V4.9.71 Phase 2）：
    /// <list type="bullet">
    /// <item><see cref="ConfigureServices"/>——DI 构建前：注册 IPermissionStore/IPermissionChecker/IPermissionDefinitionRepository，
    /// 并从 <c>ProjectMetaContextBase.Instance.PermissionContributors</c> 收集贡献者定义权限</item>
    /// <item><see cref="ConfigureFilters"/>——ConfigGlobalFilters：注册 <c>PermissionFilterAttribute</c> 到 Tier-S</item>
    /// <item><see cref="InitializeAsync"/>——系统就绪后：V4.9.72 空实现（种子/初始化留后续）</item>
    /// </list>
    /// <para>命名空间 <c>TKWF.Ext.Permissions</c>（D17 §5.1 设计 + 包名约定 §4.6）。</para>
    /// </summary>
    [TKWFExtension("Permissions", "1.0.0", Description = "权限管理扩展——细粒度权限定义/检查（IPermissionChecker + RequirePermission）")]
    public class PermissionExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
        where TUserInfo : class, IUserInfo, new()
    {
        /// <summary>扩展名称（对齐 [TKWFExtension] Name）。</summary>
        public override string Name => "Permissions";

        /// <summary>扩展描述。</summary>
        public override string Description => "权限管理扩展——细粒度权限定义/检查";

        /// <summary>
        /// 注册权限服务 + 从 ProjectMetaContext 桥收集权限贡献者定义。
        /// <para>时序保证：V4.9.71 修复后 <see cref="DomainHostInitializerBase{TUserInfo}.InitializeDiContainer"/>
        /// 在 RegisterInfrastructureInternal（赋 Instance）之后调用本钩子，<c>ProjectMetaContextBase.Instance</c> 已就绪。</para>
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            // 1. 收集权限贡献者定义（编译期清单 → 运行时实例化 → Define）
            var contributors = (ProjectMetaContextBase.Instance as ProjectMetaContextBase)
                               ?.PermissionContributors
                               ?? Array.Empty<PermissionContributorData>();
            var repository = new InMemoryPermissionDefinitionRepository();
            if (contributors.Count > 0)
            {
                var context = new PermissionDefinitionContext();
                foreach (var contributorData in contributors)
                {
                    if (contributorData.ContributorType == null) continue;
                    var contributor = (IPermissionDefinitionContributor?)Activator.CreateInstance(contributorData.ContributorType);
                    if (contributor == null)
                        throw new InvalidOperationException($"权限贡献者无法实例化: {contributorData.FullName}（需要无参构造器）");
                    contributor.Define(context);
                }
                repository.AddRange(context.Definitions);
            }

            // 2. 默认实现注册（TryAdd*：消费方可自定义覆盖，且本填充实例不覆盖消费方实现）
            // V4.9.80：Scoped 生命周期（修复 Singleton→Scoped 矛盾，参与当前请求 UoW 事务）
            // TryAddScoped：消费方自定义 IPermissionStore 优先；若未注册，回退 NoOp（fail-closed）
            services.TryAddScoped<IPermissionStore, NoOpPermissionStore>();
            services.TryAddScoped<IPermissionDefinitionRepository>(_ => repository);
            services.TryAddScoped<IPermissionChecker, PermissionChecker<TUserInfo>>();
        }

        /// <summary>注册权限过滤器到 Tier-S（Security，与 AuthorityFilter 同层，S→F→O 排序自动生效）。</summary>
        public override void ConfigureFilters(FilterBuilder<TUserInfo> builder)
        {
            builder.Add<PermissionFilterAttribute<TUserInfo>>(FilterTier.Security);
        }

        /// <summary>系统就绪后初始化（V4.9.72 空实现——真实种子/初始化留后续迭代）。</summary>
        public override Task InitializeAsync() => Task.CompletedTask;
    }

    /// <summary>
    /// V4.9.72 (W2)：内存权限定义仓库默认实现——收集贡献者定义，供权限名校验（fail-closed）。
    /// </summary>
    internal sealed class InMemoryPermissionDefinitionRepository : IPermissionDefinitionRepository
    {
        private readonly List<PermissionDefinition> _definitions = new();

        public IReadOnlyList<PermissionDefinition> GetAll() => _definitions;

        public bool Contains(string name)
            => _definitions.Any(d => d.Name == name);

        public void AddRange(IEnumerable<PermissionDefinition> definitions)
        {
            foreach (var d in definitions)
            {
                if (d == null || d.Name == null) continue;
                if (!_definitions.Any(x => x.Name == d.Name))
                    _definitions.Add(d);
            }
        }
    }

    /// <summary>
    /// V4.9.72 (W2，M1 修复)：默认权限检查器——基于权限定义仓库（fail-closed：未知权限名 → 拒绝）
    /// + 权限存储（经 <see cref="IPermissionStore"/> 真实解析）+ ambient 当前用户（<c>DomainUserContext.CurrentAopUser</c>）。
    /// <para><b>M1 修复（V4.9.72 审核）</b>：早期版本恒返回 false（未接通 store 与用户上下文），
    /// 导致默认 checker 完全不可用。现泛型化 <c>PermissionChecker&lt;TUserInfo&gt;</c>，
    /// 经 IVT 访问 <c>DomainUserContext.CurrentAopUser</c>（AOP 拦截时 push 的当前 DomainUser 实例）
    /// 解析当前用户 <c>UserIdString</c>，调 <c>IPermissionStore.GetAsync(name, "User", userId)</c> 真实判定。</para>
    /// <para>providers 约定：主谓用户权限 <c>("User", UserIdString)</c>；消费方可扩展 store 支持角色/成员 providers。</para>
    /// </summary>
    internal sealed class PermissionChecker<TUserInfo> : IPermissionChecker
        where TUserInfo : class, IUserInfo, new()
    {
        private readonly IPermissionDefinitionRepository _repository;
        private readonly IPermissionStore _store;

        public PermissionChecker(IPermissionDefinitionRepository repository, IPermissionStore store)
        {
            _repository = repository;
            _store = store;
        }

        public async Task<bool> IsGrantedAsync(string permissionName)
            => await IsGrantedCoreAsync(permissionName).ConfigureAwait(false);

        public async Task<Dictionary<string, bool>> IsGrantedAsync(params string[] permissionNames)
        {
            var result = new Dictionary<string, bool>();
            foreach (var name in permissionNames)
                result[name] = await IsGrantedCoreAsync(name).ConfigureAwait(false);
            return result;
        }

        private async Task<bool> IsGrantedCoreAsync(string permissionName)
        {
            // fail-closed：权限名未定义 → 拒绝
            if (string.IsNullOrWhiteSpace(permissionName) || !_repository.Contains(permissionName))
                return false;

            // 解析当前用户（ambient AOP 上下文——PermissionFilter 触发时 StaticDomainInterceptor 已 push）
            var current = DomainUserContext.CurrentAopUser as DomainUser<TUserInfo>;
            var userId = current?.UserInfo?.UserIdString;
            if (string.IsNullOrEmpty(userId))
                return false; // 未认证/无用户上下文 → 拒绝（与 AuthorityFilter 未认证拦截一致）

            var result = await _store.GetAsync(permissionName, "User", userId).ConfigureAwait(false);
            return result.IsGranted;
        }
    }

    /// <summary>
    /// NoOp 权限存储默认实现——读恒拒绝、写空操作。
    /// 当消费方未注册 <c>IEntityDAC&lt;PermissionGrantEntity&gt;</c>（未调用 <c>UseFreeSqlEntityDAC()</c>）
    /// 或自定义 <c>IPermissionStore</c> 时的回退。
    /// </summary>
    internal sealed class NoOpPermissionStore : IPermissionStore
    {
        public Task<PermissionGrantResult> GetAsync(string permissionName, string providerName, string providerKey)
            => Task.FromResult(PermissionGrantResult.Denied);

        public Task SetAsync(string permissionName, string providerName, string providerKey, bool isGranted)
            => Task.CompletedTask;
    }
}
