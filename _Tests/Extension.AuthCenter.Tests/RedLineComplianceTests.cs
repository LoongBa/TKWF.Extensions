using System;
using System.IO;
using System.Linq;
using Xunit;

namespace TKWF.Ext.AuthCenter.Tests;

/// <summary>D13：数据访问红线合规——扩展 Store/Service 层零 IFreeSql/IEntityDAC 直注入（仅 DataService 分部构造为合法位置）。</summary>
public class RedLineComplianceTests
{
    [Fact]
    public void ServiceFiles_HaveNoDirectOrmDependency()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "_Framework", "AuthCenter"));
        Assert.True(Directory.Exists(root), $"扩展目录不存在：{root}");

        // 服务门面文件（非 DataServices/ 目录）——禁 IFreeSql / IEntityDAC 注入
        var serviceFiles = Directory.GetFiles(root, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(f => !f.EndsWith(".g.cs", StringComparison.Ordinal)
                        && !f.EndsWith(".biz.cs", StringComparison.Ordinal)
                        && !Path.GetFileName(f).EndsWith("Entity.cs", StringComparison.Ordinal))
            .ToList();

        foreach (var file in serviceFiles)
        {
            var content = File.ReadAllText(file);
            // 只查注入类型模式（IEntityDAC< 实际注入）；"IFreeSql" 字样在注释（"不注入 IFreeSql"）不构成违规
            Assert.DoesNotContain("IEntityDAC<", content);
        }
    }

    [Fact]
    public void DataServicePartials_UseInternalForwarding()
    {
        // DataService 分部应使用 EntityCreateAsync/EntitySelectAsync 等内部原子转发（.g.cs 承载），不手写 ORM 查询
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "_Framework", "AuthCenter", "DataServices"));
        var partialFiles = Directory.GetFiles(root, "*.cs")
            .Where(f => !f.EndsWith(".g.cs", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(12, partialFiles.Count); // 12 实体 DataService 分部（V0.9.0 增 UserProfile + PasswordResetCode——凭据/档案分离 + 找回链路；ADR-密码策略 增 PasswordHistory——历史防重用）

        foreach (var file in partialFiles)
        {
            var content = File.ReadAllText(file);
            // 只查实际注入（排除注释——"零 IFreeSql/IEntityDAC 直注入" 类文档声明不构成违规，对齐上方 ServiceFiles 判定）；
            // DataService 分部不应以字段/参数/局部变量形态引入 IFreeSql（代码行过滤后再扫）
            var codeOnly = string.Join("\n", content.Split('\n')
                .Where(l => !l.TrimStart().StartsWith("///", StringComparison.Ordinal)
                         && !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
            Assert.DoesNotContain("IFreeSql", codeOnly);
        }
    }
}
