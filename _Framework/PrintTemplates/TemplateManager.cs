using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.PrintTemplates
{
    /// <summary>
    /// 模板管理门面——版本生命周期 + 渲染入口 + 发布版本自动递增。
    /// <para>Scoped 生命周期（按请求）。</para>
    /// <para>⚡ ADR88（v4.10.52）构造注入门控：域服务（Store/Renderer/ViewDataService）改 User.Use&lt;T&gt;() 懒加载。</para>
    /// <para>V0.3.0（领域自治根治，ADR90）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文
    /// （IDomainUser 永不注册 DI）；注册形态改 <c>AddConstructibleService&lt;ITemplateManager, TemplateManager&gt;</c>
    /// （接口可构造守卫工厂 + 实现类 throw-factory，消费方经 <c>User.Use&lt;ITemplateManager&gt;()</c> 解析）。</para>
    /// </summary>
    [DiContractIgnore]
    internal sealed class TemplateManager : DomainServiceBase, ITemplateManager
    {
        private ITemplateStore? _store;
        private ITemplateRenderer? _renderer;
        private PrintTemplateVersionViewDataService? _viewDataService;

        private ITemplateStore Store => _store ??= User.Use<ITemplateStore>();
        private ITemplateRenderer Renderer => _renderer ??= User.Use<ITemplateRenderer>();
        private PrintTemplateVersionViewDataService ViewDataService => _viewDataService ??= User.Use<PrintTemplateVersionViewDataService>();

        public TemplateManager(IDomainUser user)
            : base(user)
        {
        }

        /// <inheritdoc />
        public async Task<PrintTemplateEntity?> GetTemplateAsync(string key, CancellationToken ct = default)
        {
            return await Store.GetByKeyAsync(key, ct);
        }

        /// <inheritdoc />
        public async Task<PrintTemplateVersionView?> GetVersionAsync(string key, string version, CancellationToken ct = default)
            => await ViewDataService.GetVersionByKeyAsync(key, version, ct);

        /// <inheritdoc />
        public async Task<PrintTemplateVersionView?> GetActiveVersionAsync(string key, CancellationToken ct = default)
            => await ViewDataService.GetActiveVersionByKeyAsync(key, ct);

        /// <inheritdoc />
        public async Task<IReadOnlyList<PrintTemplateVersionView>> ListVersionsAsync(string key, CancellationToken ct = default)
            => await ViewDataService.ListVersionsByKeyAsync(key, ct);

        /// <inheritdoc />
        public async Task<string> RenderAsync(string key, IReadOnlyDictionary<string, object?> model, string? version = null, CancellationToken ct = default)
        {
            PrintTemplateVersionView? ver;
            if (version == null)
            {
                ver = await GetActiveVersionAsync(key, ct);
                if (ver == null)
                    throw new InvalidOperationException($"模板 '{key}' 未找到 Active 版本");
            }
            else
            {
                ver = await GetVersionAsync(key, version, ct);
                if (ver == null)
                    throw new InvalidOperationException($"模板 '{key}' 版本 '{version}' 未找到");
            }
            return await Renderer.RenderContentAsync(ver.Content, model, ct);
        }

        /// <inheritdoc />
        public async Task<PrintTemplateVersionEntity> PublishAsync(string key, string content, string? description = null, CancellationToken ct = default)
        {
            // 1. 获取或自动创建模板（C2）
            var template = await Store.GetByKeyAsync(key, ct);
            if (template == null)
            {
                template = new PrintTemplateEntity
                {
                    Key = key,
                    Name = key, // C2: Name=Key
                    Description = description
                };
                await Store.UpsertTemplateAsync(template, ct);
                template = await Store.GetByKeyAsync(key, ct)!;
                if (template == null)
                    throw new InvalidOperationException($"自动创建模板 '{key}' 失败");
            }

            // 2. 计算下一个 minor 版本号（M4：首版 1.0.0，后续 1.{maxMinor+1}.0）
            var versions = await Store.ListVersionsAsync(template.Id, ct);
            var nextMinor = ComputeNextMinor(versions);
            var newVersion = $"1.{nextMinor}.0";

            // 3. 归档旧 Active 版本
            var currentActive = versions.FirstOrDefault(v => v.Status == PrintTemplateVersionStatus.Active);
            if (currentActive != null)
            {
                currentActive.Status = PrintTemplateVersionStatus.Archived;
                await Store.UpsertVersionAsync(currentActive, ct);
            }

            // 4. 创建新 Active 版本
            var entity = new PrintTemplateVersionEntity
            {
                TemplateId = template.Id,
                Version = newVersion,
                Content = content,
                Status = PrintTemplateVersionStatus.Active,
                Description = description,
                PublishedAt = DateTimeOffset.UtcNow
            };

            // 5. Upsert（TemplateId+Version 唯一约束——并发发布失败显式异常，C1）
            await Store.UpsertVersionAsync(entity, ct);
            return entity;
        }

        /// <inheritdoc />
        public async Task<PrintTemplateVersionEntity> DraftAsync(string key, string content, string? description = null, CancellationToken ct = default)
        {
            // 1. 获取或自动创建模板
            var template = await Store.GetByKeyAsync(key, ct);
            if (template == null)
            {
                template = new PrintTemplateEntity
                {
                    Key = key,
                    Name = key,
                    Description = description
                };
                await Store.UpsertTemplateAsync(template, ct);
                template = await Store.GetByKeyAsync(key, ct)!;
                if (template == null)
                    throw new InvalidOperationException($"自动创建模板 '{key}' 失败");
            }

            // 2. 查找已有 Draft
            var versions = await Store.ListVersionsAsync(template.Id, ct);
            var existingDraft = versions.FirstOrDefault(v => v.Status == PrintTemplateVersionStatus.Draft);

            if (existingDraft != null)
            {
                // 更新已有 Draft（upsert 单 Draft）
                existingDraft.Content = content;
                existingDraft.Description = description;
                await Store.UpsertVersionAsync(existingDraft, ct);
                return existingDraft;
            }

            // 3. 创建新 Draft（"1.{next}.0-draft"，next = max(所有版本) + 1）
            var nextMinor = ComputeNextMinor(versions);
            var draftVersion = $"1.{nextMinor}.0-draft";

            var entity = new PrintTemplateVersionEntity
            {
                TemplateId = template.Id,
                Version = draftVersion,
                Content = content,
                Status = PrintTemplateVersionStatus.Draft,
                Description = description
            };
            await Store.UpsertVersionAsync(entity, ct);
            return entity;
        }

        /// <inheritdoc />
        public async Task ArchiveAsync(string key, string version, CancellationToken ct = default)
        {
            var template = await Store.GetByKeyAsync(key, ct);
            if (template == null)
                throw new InvalidOperationException($"模板 '{key}' 未找到");

            var ver = await Store.GetVersionAsync(template.Id, version, ct);
            if (ver == null)
                throw new InvalidOperationException($"模板 '{key}' 版本 '{version}' 未找到");

            ver.Status = PrintTemplateVersionStatus.Archived;
            await Store.UpsertVersionAsync(ver, ct);
        }

        /// <summary>
        /// 计算下一个 minor 版本号（M4）：首版 = 1，后续 = max(现有非 Draft 版本 Minor) + 1。
        /// </summary>
        private static int ComputeNextMinor(IReadOnlyList<PrintTemplateVersionEntity> versions)
        {
            if (versions.Count == 0) return 0; // 首版 1.0.0

            var maxMinor = 0;
            foreach (var v in versions)
            {
                if (v.Version.EndsWith("-draft")) continue; // Draft 不参与计算
                if (Version.TryParse(v.Version, out var semver) && semver.Minor > maxMinor)
                    maxMinor = semver.Minor;
            }
            return maxMinor + 1;
        }

        /// <summary>
        /// 简易 SemVer 解析（Major.Minor.Patch）。
        /// </summary>
        private static class Version
        {
            public static bool TryParse(string version, out (int Major, int Minor, int Patch) result)
            {
                result = (0, 0, 0);
                if (string.IsNullOrEmpty(version)) return false;
                var parts = version.Split('.');
                if (parts.Length < 2 || parts.Length > 3) return false;
                if (!int.TryParse(parts[0], out var major)) return false;
                if (!int.TryParse(parts[1], out var minor)) return false;
                var patch = parts.Length == 3 && int.TryParse(parts[2], out var p) ? p : 0;
                result = (major, minor, patch);
                return true;
            }
        }
    }
}
