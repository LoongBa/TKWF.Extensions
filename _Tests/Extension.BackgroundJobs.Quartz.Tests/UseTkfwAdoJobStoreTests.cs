using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Xunit;

namespace TKWF.Ext.BackgroundJobs.Quartz.Tests;

/// <summary>
/// UseTkfwAdoJobStore 配置正确性测试——验证 Quartz AdoJobStore 配置级封装。
/// </summary>
public class UseTkfwAdoJobStoreTests : IDisposable
{
    /// <summary>测试用 SQLite 文件数据库目录——每个测试方法独立文件（避免并发冲突）。</summary>
    private readonly string _tempDir;

    public UseTkfwAdoJobStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"quartz_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>生成一个唯一的 SQLite 文件数据库连接字符串（测试隔离）。</summary>
    private string SqliteConnString(string name)
    {
        var dbFile = Path.Combine(_tempDir, $"{name}.db");
        return $"Data Source={dbFile}";
    }

    // ══════════════════════════════════════════════════════════════════════
    //  SQLite 集成测试（文件数据库，ProvisionSchema 建表 → 调度器可创建）
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UseTkfwAdoJobStore_Sqlite_ConfiguresCorrectly()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q =>
        {
            q.UseTkfwAdoJobStore(o =>
            {
                o.DbProvider = "sqlite";
                o.ConnectionString = SqliteConnString("basic");
            });
        });

        var provider = services.BuildServiceProvider();
        var schedulerFactory = provider.GetRequiredService<ISchedulerFactory>();

        var scheduler = await schedulerFactory.GetScheduler();
        Assert.NotNull(scheduler);
        // 默认 InstanceId="AUTO" → 扩展生成 NODE_xxx 唯一 ID
        Assert.StartsWith("NODE_", scheduler.SchedulerInstanceId);
    }

    [Fact]
    public async Task UseTkfwAdoJobStore_CustomTablePrefix_Applied()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q =>
        {
            q.UseTkfwAdoJobStore(o =>
            {
                o.DbProvider = "sqlite";
                o.ConnectionString = SqliteConnString("prefix");
                o.TablePrefix = "MY_PREFIX_";
            });
        });

        var provider = services.BuildServiceProvider();
        var schedulerFactory = provider.GetRequiredService<ISchedulerFactory>();

        var scheduler = await schedulerFactory.GetScheduler();
        Assert.NotNull(scheduler);
    }

    [Fact]
    public async Task UseTkfwAdoJobStore_ReturnsBuilder_ForChaining()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q =>
        {
            q.UseTkfwAdoJobStore(o =>
            {
                o.DbProvider = "sqlite";
                o.ConnectionString = SqliteConnString("chain");
            })
            .UseDefaultThreadPool(1);
        });

        var provider = services.BuildServiceProvider();
        var schedulerFactory = provider.GetRequiredService<ISchedulerFactory>();

        var scheduler = await schedulerFactory.GetScheduler();
        Assert.NotNull(scheduler);
    }

    [Fact]
    public async Task UseTkfwAdoJobStore_AutoInstanceId_GeneratesUniqueId()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q =>
        {
            q.UseTkfwAdoJobStore(o =>
            {
                o.DbProvider = "sqlite";
                o.ConnectionString = SqliteConnString("autoid");
                o.Clustering = false;
                o.InstanceId = "AUTO";
            });
        });

        var provider = services.BuildServiceProvider();
        var schedulerFactory = provider.GetRequiredService<ISchedulerFactory>();

        var scheduler = await schedulerFactory.GetScheduler();
        Assert.NotNull(scheduler);
        Assert.StartsWith("NODE_", scheduler.SchedulerInstanceId);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  预期异常测试（配置语义验证，不需要真实数据库连接）
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void UseTkfwAdoJobStore_EmptyConnectionString_ThrowsInvalidOperation()
    {
        var services = new ServiceCollection();
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            services.AddQuartz(q =>
            {
                q.UseTkfwAdoJobStore(o =>
                {
                    o.DbProvider = "sqlserver";
                    o.ConnectionString = "";
                });
            });
        });
        Assert.Contains("ConnectionString", ex.Message);
    }

    [Fact]
    public void UseTkfwAdoJobStore_NullConnectionString_ThrowsInvalidOperation()
    {
        var services = new ServiceCollection();
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            services.AddQuartz(q =>
            {
                q.UseTkfwAdoJobStore(o =>
                {
                    o.DbProvider = "sqlserver";
                    o.ConnectionString = null!;
                });
            });
        });
        Assert.Contains("ConnectionString", ex.Message);
    }

    [Fact]
    public void UseTkfwAdoJobStore_UnsupportedDbProvider_ThrowsNotSupported()
    {
        var services = new ServiceCollection();
        var ex = Assert.Throws<NotSupportedException>(() =>
        {
            services.AddQuartz(q =>
            {
                q.UseTkfwAdoJobStore(o =>
                {
                    o.DbProvider = "mysql";
                    o.ConnectionString = "Server=localhost;Database=quartz;";
                });
            });
        });
        Assert.Contains("mysql", ex.Message);
        Assert.Contains("sqlserver", ex.Message);
    }

    [Fact]
    public void UseTkfwAdoJobStore_EmptyDbProvider_ThrowsNotSupported()
    {
        var services = new ServiceCollection();
        var ex = Assert.Throws<NotSupportedException>(() =>
        {
            services.AddQuartz(q =>
            {
                q.UseTkfwAdoJobStore(o =>
                {
                    o.DbProvider = "";
                    o.ConnectionString = "Server=localhost;Database=quartz;";
                });
            });
        });
        Assert.Contains("DbProvider", ex.Message);
    }

    [Fact]
    public void UseTkfwAdoJobStore_NullBuilder_ThrowsArgumentNull()
    {
        IQuartzBuilder? builder = null;
        Assert.Throws<ArgumentNullException>(() =>
        {
            builder!.UseTkfwAdoJobStore(o =>
            {
                o.DbProvider = "sqlite";
                o.ConnectionString = "Data Source=test.db";
            });
        });
    }

    [Fact]
    public void UseTkfwAdoJobStore_NullConfigure_ThrowsArgumentNull()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() =>
        {
            services.AddQuartz(q =>
            {
                q.UseTkfwAdoJobStore(null!);
            });
        });
    }

    [Fact]
    public void QuartzAdoJobStoreOptions_DefaultValues()
    {
        var options = new QuartzAdoJobStoreOptions();
        Assert.Equal("QRTZ_", options.TablePrefix);
        Assert.True(options.AutoCreateSchema);
        Assert.False(options.Clustering);
        Assert.Equal("AUTO", options.InstanceId);
        Assert.Null(options.ClusterCheckinInterval);
        Assert.Equal("", options.DbProvider);
        Assert.Equal("", options.ConnectionString);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  配置接受测试（非 SQLite 方言——驱动程序集不在测试项目中，
    //  只验证 AddQuartz 不抛异常；不调 GetScheduler 触发驱动加载）
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void UseTkfwAdoJobStore_SqlServer_ConfigAcceptsCorrectly()
    {
        // SqlServer 驱动（Microsoft.Data.SqlClient）不在测试项目中，
        // 验证 AddQuartz 配置注册不抛异常（配置级验证——不调 GetScheduler）
        var services = new ServiceCollection();
        services.AddQuartz(q =>
        {
            q.UseTkfwAdoJobStore(o =>
            {
                o.DbProvider = "sqlserver";
                o.ConnectionString = "Server=localhost;Database=Quartz;Trusted_Connection=True;";
                o.TablePrefix = "QRTZ_";
                o.AutoCreateSchema = true;
            });
        });
        // BuildServiceProvider + Dispose 不触发驱动加载——仅验证 DI 注册正确
        var provider = services.BuildServiceProvider();
        provider.Dispose();
    }

    [Fact]
    public void UseTkfwAdoJobStore_Postgresql_ConfigAcceptsCorrectly()
    {
        // PostgreSQL 驱动（Npgsql）不在测试项目中
        var services = new ServiceCollection();
        services.AddQuartz(q =>
        {
            q.UseTkfwAdoJobStore(o =>
            {
                o.DbProvider = "postgresql";
                o.ConnectionString = "Host=localhost;Database=quartz;Username=quartz;Password=quartz;";
                o.Clustering = true;
                o.ClusterCheckinInterval = TimeSpan.FromSeconds(20);
            });
        });
        var provider = services.BuildServiceProvider();
        provider.Dispose();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  DbProvider 大小写不敏感——配置接受测试（非 SQLite 走配置级验证）
    // ══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("SQLSERVER")]
    [InlineData("SqlServer")]
    [InlineData("sqlserver")]
    public void UseTkfwAdoJobStore_SqlServer_CaseInsensitive_Accepts(string provider)
    {
        var services = new ServiceCollection();
        services.AddQuartz(q =>
        {
            q.UseTkfwAdoJobStore(o =>
            {
                o.DbProvider = provider;
                o.ConnectionString = "Server=localhost;Database=quartz;";
            });
        });
        var sp = services.BuildServiceProvider();
        sp.Dispose();
    }

    [Theory]
    [InlineData("POSTGRESQL")]
    [InlineData("PostgreSQL")]
    [InlineData("postgresql")]
    public void UseTkfwAdoJobStore_Postgresql_CaseInsensitive_Accepts(string provider)
    {
        var services = new ServiceCollection();
        services.AddQuartz(q =>
        {
            q.UseTkfwAdoJobStore(o =>
            {
                o.DbProvider = provider;
                o.ConnectionString = "Host=localhost;Database=quartz;";
            });
        });
        var sp = services.BuildServiceProvider();
        sp.Dispose();
    }

    [Theory]
    [InlineData("SQLITE")]
    [InlineData("SQLite")]
    [InlineData("sqlite")]
    public async Task UseTkfwAdoJobStore_Sqlite_CaseInsensitive_Succeeds(string provider)
    {
        // SQLite 有驱动，可以真正创建调度器
        var services = new ServiceCollection();
        services.AddQuartz(q =>
        {
            q.UseTkfwAdoJobStore(o =>
            {
                o.DbProvider = provider;
                o.ConnectionString = SqliteConnString($"case_{provider}");
            });
        });
        var sp = services.BuildServiceProvider();
        var schedulerFactory = sp.GetRequiredService<ISchedulerFactory>();

        var scheduler = await schedulerFactory.GetScheduler();
        Assert.NotNull(scheduler);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  SQLite 预期异常测试（SQLite 特有限制）
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UseTkfwAdoJobStore_ClusteringEnabled_SqliteRejects()
    {
        // Quartz 显式拒绝 SQLite 用于集群（锁机制限制）
        // 用文件数据库避免 in-memory 驱动加载问题
        var services = new ServiceCollection();
        services.AddQuartz(q =>
        {
            q.UseTkfwAdoJobStore(o =>
            {
                o.DbProvider = "sqlite";
                o.ConnectionString = SqliteConnString("cluster_reject");
                o.Clustering = true;
                o.InstanceId = "TEST_NODE";
            });
        });

        var provider = services.BuildServiceProvider();
        var schedulerFactory = provider.GetRequiredService<ISchedulerFactory>();

        // GetScheduler() 时 Quartz 检测 SQLite + Clustering → 抛 InvalidConfigurationException
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => schedulerFactory.GetScheduler().AsTask());
        Assert.Contains("SQLite", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UseTkfwAdoJobStore_AutoCreateSchemaFalse_ThrowsSchemaValidation()
    {
        // AutoCreateSchema=false → SchemaProvisioning.Validate（默认行为）
        // 空文件数据库无 Quartz schema → 验证失败
        var services = new ServiceCollection();
        services.AddQuartz(q =>
        {
            q.UseTkfwAdoJobStore(o =>
            {
                o.DbProvider = "sqlite";
                o.ConnectionString = SqliteConnString("no_schema");
                o.AutoCreateSchema = false;
            });
        });

        var provider = services.BuildServiceProvider();
        var schedulerFactory = provider.GetRequiredService<ISchedulerFactory>();

        var ex = await Assert.ThrowsAsync<SchedulerException>(() => schedulerFactory.GetScheduler().AsTask());
        Assert.Contains("schema", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
