using System;
using System.IO;
using System.Linq;
using Xunit;

namespace TKWF.Ext.Authentication.Tests;

/// <summary>D13：数据访问红线合规——扩展 Store/Service 层零 IFreeSql/IEntityDAC 直注入（仅 DataService 分部构造为合法位置）。</summary>
public class RedLineComplianceTests
{
    [Fact]
    public void ServiceFiles_HaveNoDirectOrmDependency()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "_Framework", "Authentication"));
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
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "_Framework", "Authentication", "DataServices"));
        var partialFiles = Directory.GetFiles(root, "*.cs")
            .Where(f => !f.EndsWith(".g.cs", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(8, partialFiles.Count); // 8 实体 DataService 分部

        foreach (var file in partialFiles)
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotContain("IFreeSql", content);
        }
    }
}
