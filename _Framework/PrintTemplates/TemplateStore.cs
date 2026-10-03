using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.PrintTemplates
{
    /// <summary>
    /// 模板存储实现——经 <see cref="PrintTemplateEntityDataService"/> + <see cref="PrintTemplateVersionEntityDataService"/>
    /// （SG1/xCodeGen 生成的 DataService）委托持久化，遵循数据访问红线（2026-09-07 用户裁定）：
    /// 扩展不直接注入 IFreeSql / IEntityDAC，只依赖 DataService。
    /// <para>异常传播：所有存储层异常向上传递（模板是审计关键资产，不静默）——
    /// 区别于 Settings/BlobStoring/Emailing 的异常静默模式。</para>
    /// <para>V0.3.0（领域自治根治，ADR90）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文
    /// （IDomainUser 永不注册 DI）；注册形态改 <c>AddConstructibleService&lt;ITemplateStore, TemplateStore&gt;</c>
    /// （接口可构造守卫工厂 + 实现类 throw-factory，消费方经 <c>User.Use&lt;ITemplateStore&gt;()</c> 解析）。</para>
    /// </summary>
    [DiContractIgnore]
    internal sealed class TemplateStore : DomainServiceBase, ITemplateStore
    {
        private PrintTemplateEntityDataService? _templateDataService;
        private PrintTemplateVersionEntityDataService? _versionDataService;

        private PrintTemplateEntityDataService TemplateDataService => _templateDataService ??= User.Use<PrintTemplateEntityDataService>();
        private PrintTemplateVersionEntityDataService VersionDataService => _versionDataService ??= User.Use<PrintTemplateVersionEntityDataService>();

        public TemplateStore(IDomainUser user)
            : base(user)
        {
        }

        /// <inheritdoc />
        public async Task<PrintTemplateEntity?> GetByKeyAsync(string key, CancellationToken ct = default)
            => await TemplateDataService.GetByKeyAsync(key, ct);

        /// <inheritdoc />
        public async Task UpsertTemplateAsync(PrintTemplateEntity template, CancellationToken ct = default)
            => await TemplateDataService.UpsertByKeyAsync(template, ct);

        /// <inheritdoc />
        public async Task<PrintTemplateVersionEntity?> GetVersionAsync(long templateId, string version, CancellationToken ct = default)
            => await VersionDataService.GetByTemplateAndVersionAsync(templateId, version, ct);

        /// <inheritdoc />
        public async Task<PrintTemplateVersionEntity?> GetActiveVersionAsync(long templateId, CancellationToken ct = default)
            => await VersionDataService.GetActiveByTemplateAsync(templateId, ct);

        /// <inheritdoc />
        public async Task<IReadOnlyList<PrintTemplateVersionEntity>> ListVersionsAsync(long templateId, CancellationToken ct = default)
            => await VersionDataService.ListVersionsByTemplateAsync(templateId, ct);

        /// <inheritdoc />
        public async Task UpsertVersionAsync(PrintTemplateVersionEntity version, CancellationToken ct = default)
            => await VersionDataService.UpsertVersionAsync(version, ct);
    }
}