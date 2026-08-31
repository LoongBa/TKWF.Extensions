using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    public class PermissionExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>, IServiceProviderAware
        where TUserInfo : class, IUserInfo, new()
    {
        /// <summary>扩展名称（对齐 [TKWFExtension] Name）。</summary>
        public override string Name => "Permissions";

        /// <summary>扩展描述。</summary>
        public override string Description => "权限管理扩展——细粒度权限定义/检查";

        /// <summary>注入的 IServiceProvider（InitializeExtensionsAsync 阶段设置，V4.9.76 D2）。</summary>
        public IServiceProvider? ServiceProvider { get; set; }

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
            // V0.6.0：角色提供者——TryAddScoped（消费方可自定义覆盖，如从角色服务/外部身份提供商解析）
            services.TryAddScoped<IRoleProvider<TUserInfo>, DefaultRoleProvider<TUserInfo>>();
            services.TryAddScoped<IPermissionChecker, PermissionChecker<TUserInfo>>();

            // V0.3.0：权限管理 Service——TryAddScoped（消费方可自定义覆盖）
            services.TryAddScoped<PermissionGrantEntityDataService>();
        }

        /// <summary>注册权限过滤器到 Tier-S（Security，与 AuthorityFilter 同层，S→F→O 排序自动生效）。</summary>
        public override void ConfigureFilters(FilterBuilder<TUserInfo> builder)
        {
            builder.Add<PermissionFilterAttribute<TUserInfo>>(FilterTier.Security);
        }

        /// <summary>
        /// 系统就绪后初始化（V0.4.0 G3 + V0.7.0 增强）。
        /// <list type="bullet">
        /// <item><b>V0.7.0 W1（自建表）</b>：扩展实体 <c>PermissionGrant</c> 不在消费方 <c>SyncTables</c> 范围
        /// （只扫消费方 assembly）——此处复用 <see cref="ITableStructureSynchronizer"/> 对扩展自身程序集主动
        /// <see cref="ITableStructureSynchronizer.SyncStructure"/>（幂等建表：创建缺失表/列）。未注册实现时静默跳过
        /// （InMemory/NoOp 场景无影响）。</item>
        /// <item><b>V0.4.0 G3 + V0.7.0 W3（种子）</b>：幂等预置默认 admin 角色 <see cref="PermissionNames.AdminAll"/>
        /// 系统权限（替代 V0.4.0 的逐权限授予——Admin.All 拥有者对全部权限放行，未来新增权限自动覆盖）。
        /// 幂等：仅当记录不存在时授予，绝不覆盖消费方已设置的授予/撤销。</item>
        /// </list>
        /// <para>经 <see cref="IServiceProviderAware.ServiceProvider"/> 解析 <see cref="PermissionGrantEntityDataService"/>
        /// 与 <see cref="ITableStructureSynchronizer"/>。通过 <see cref="PermissionOptions.SeedAdminRoleName"/> 控制种子角色；
        /// 空字符串 = 禁用种子。未注册 <see cref="IEntityDAC{TEntity}"/>（无真实持久化）时跳过。</para>
        /// </summary>
        public override async Task InitializeAsync()
        {
            // 无 DI 容器（未实现 IServiceProviderAware 或未注入）→ 跳过
            if (ServiceProvider is null) return;

            using var scope = ServiceProvider.CreateScope();
            var scoped = scope.ServiceProvider;

            // ── V0.7.0 W1：扩展自建表（幂等，未注册 synchronizer 时跳过）──
            // 扩展实体不在消费方 SyncTables 范围（只扫 GetType().Assembly）——主动建扩展自己的表。
            // FreeSqlTableStructureSynchronizer.CodeFirst.SyncStructure 幂等：仅创建缺失表/列。
            var synchronizer = scoped.GetService<ITableStructureSynchronizer>();
            if (synchronizer != null)
                synchronizer.SyncStructure(typeof(PermissionGrantEntity).Assembly);

            // 真实持久化未接线（未注册 IEntityDAC）→ 跳过种子（NoOp 模式下无意义）
            var dataService = scoped.GetService<PermissionGrantEntityDataService>();
            if (dataService is null) return;

            var options = scoped.GetService<IOptions<PermissionOptions>>()?.Value;
            var seedRole = options?.SeedAdminRoleName;
            if (string.IsNullOrWhiteSpace(seedRole)) return;

            // ── V0.7.0 W3：种子高级化——预置 admin 角色 Admin.All 系统权限（替代逐权限授予）──
            var existingSystem = await dataService.GetGrantAsync(PermissionNames.AdminAll, "Role", seedRole);
            if (existingSystem is null)
                await dataService.SetGrantAsync(PermissionNames.AdminAll, "Role", seedRole, isGranted: true);
        }
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
    /// <para><b>V0.6.0 角色→权限映射</b>：新增 <see cref="IRoleProvider{TUserInfo}"/> 依赖，
    /// 支持用户+角色双重检查——用户级显式授予优先；未授予时回退角色级判定（任一角色授予即通过）。
    /// fail-closed：用户未授权 + 角色未授权 → 拒绝。</para>
    /// <para>providers 约定：用户权限 <c>("User", UserIdString)</c>；角色权限 <c>("Role", roleName)</c>。</para>
    /// </summary>
    internal sealed class PermissionChecker<TUserInfo> : IPermissionChecker
        where TUserInfo : class, IUserInfo, new()
    {
        private readonly IPermissionDefinitionRepository _repository;
        private readonly IPermissionStore _store;
        private readonly IRoleProvider<TUserInfo> _roleProvider;

        public PermissionChecker(IPermissionDefinitionRepository repository, IPermissionStore store, IRoleProvider<TUserInfo> roleProvider)
        {
            _repository = repository;
            _store = store;
            _roleProvider = roleProvider;
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
            // fail-closed：权限名为空 → 拒绝
            if (string.IsNullOrWhiteSpace(permissionName))
                return false;

            // 解析当前用户（ambient AOP 上下文——PermissionFilter 触发时 StaticDomainInterceptor 已 push）
            var current = DomainUserContext.CurrentAopUser as DomainUser<TUserInfo>;
            var user = current?.UserInfo;
            var userId = user?.UserIdString;
            if (string.IsNullOrEmpty(userId))
                return false; // 未认证/无用户上下文 → 拒绝（与 AuthorityFilter 未认证拦截一致）

            // V0.7.0 W3：系统权限 Admin.All——用户或任一角色拥有 → 对所有权限放行
            // （系统权限是隐式定义，不依赖贡献者声明，不走下方 _repository.Contains 校验）
            if (await HasSystemPermissionAsync(userId, user!).ConfigureAwait(false))
                return true;

            // fail-closed：权限名未定义 → 拒绝
            if (!_repository.Contains(permissionName))
                return false;

            // 1. 用户级检查（显式授权优先）
            var userResult = await _store.GetAsync(permissionName, "User", userId).ConfigureAwait(false);
            if (userResult.IsGranted) return true; // 用户显式授予

            // 2. 角色级检查（兜底——用户未授权时回退角色判定）
            //    PermissionGrantResult 无 NotFound 状态，Denied 既表示"显式撤销"也表示"未设置"，
            //    因此统一回退角色检查，不在此处短路。
            var roles = await _roleProvider.GetRolesAsync(user!).ConfigureAwait(false);
            foreach (var role in roles)
            {
                var roleResult = await _store.GetAsync(permissionName, "Role", role).ConfigureAwait(false);
                if (roleResult.IsGranted) return true; // 任一角色授予即通过
            }

            return false; // 用户未授权 + 角色未授权 → fail-closed
        }

        /// <summary>
        /// V0.7.0 W3：检查用户或任一角色是否拥有系统权限 <see cref="PermissionNames.AdminAll"/>。
        /// 拥有者对所有已定义权限放行。用户级优先，其次任一角色。
        /// </summary>
        private async Task<bool> HasSystemPermissionAsync(string userId, TUserInfo user)
        {
            var userSystem = await _store.GetAsync(PermissionNames.AdminAll, "User", userId).ConfigureAwait(false);
            if (userSystem.IsGranted) return true;

            var roles = await _roleProvider.GetRolesAsync(user).ConfigureAwait(false);
            foreach (var role in roles)
            {
                var roleSystem = await _store.GetAsync(PermissionNames.AdminAll, "Role", role).ConfigureAwait(false);
                if (roleSystem.IsGranted) return true;
            }

            return false;
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
