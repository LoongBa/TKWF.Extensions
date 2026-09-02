using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using TKW.Framework.Domain;

namespace TKWF.Ext.Settings.Tests;

/// <summary>
/// SettingsOptions 测试——覆盖默认值验证、CacheExpirationSeconds、[Options] 声明 + IConfiguration 绑定。
/// </summary>
public class SettingsOptionsTests
{
    [Fact]
    public void Default_DefaultSettingValueProvider_IsGlobal()
    {
        var options = new SettingsOptions();
        Assert.Equal("Global", options.DefaultSettingValueProvider);
    }

    [Fact]
    public void Default_IsEnabled_IsTrue()
    {
        var options = new SettingsOptions();
        Assert.True(options.IsEnabled);
    }

    [Fact]
    public void Custom_DefaultSettingValueProvider_IsRespected()
    {
        var options = new SettingsOptions { DefaultSettingValueProvider = "Tenant" };
        Assert.Equal("Tenant", options.DefaultSettingValueProvider);
    }

    [Fact]
    public void Custom_IsEnabled_CanBeDisabled()
    {
        var options = new SettingsOptions { IsEnabled = false };
        Assert.False(options.IsEnabled);
    }

    [Fact]
    public void Default_CacheExpirationSeconds_Is300()
    {
        var options = new SettingsOptions();
        Assert.Equal(300, options.CacheExpirationSeconds);
    }

    [Fact]
    public void Custom_CacheExpirationSeconds_IsRespected()
    {
        var options = new SettingsOptions { CacheExpirationSeconds = 60 };
        Assert.Equal(60, options.CacheExpirationSeconds);
    }

    // ── V0.2.0：SG1 [Options] 声明 + IConfiguration 绑定 ──

    [Fact]
    public void OptionsAttribute_Declared_WithSettingsSection()
    {
        // [Options("TKWF:Settings")] 声明——SG1 在消费方生成自动绑定（与 Navigation/Permissions 同模式）
        var attr = typeof(SettingsOptions).GetCustomAttributes(typeof(OptionsAttribute), false)
            .Cast<OptionsAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("TKWF:Settings", attr!.SectionPath);
    }

    [Fact]
    public void Bind_FromConfigurationSection_AppliesValues()
    {
        // 模拟消费方 appsettings.json：TKWF:Settings 节 → GetSection 绑定（RegisterOptionsBindings 的等价路径）
        var config = new ConfigurationBuilder()
            .Add(new InMemoryConfigurationSource(new Dictionary<string, string?>
            {
                ["TKWF:Settings:DefaultSettingValueProvider"] = "Global",
                ["TKWF:Settings:IsEnabled"] = "true",
                ["TKWF:Settings:CacheExpirationSeconds"] = "600"
            }))
            .Build();

        var section = config.GetSection("TKWF:Settings");
        var options = section.Get<SettingsOptions>();

        Assert.NotNull(options);
        Assert.Equal("Global", options!.DefaultSettingValueProvider);
        Assert.True(options.IsEnabled);
        Assert.Equal(600, options.CacheExpirationSeconds);
    }

    // ── Test helpers ──

    /// <summary>最小内存配置源（避免测试引入 Configuration.Memory 包）。</summary>
    private sealed class InMemoryConfigurationSource : IConfigurationSource
    {
        private readonly IDictionary<string, string?> _values;

        public InMemoryConfigurationSource(IDictionary<string, string?> values) => _values = values;

        public IConfigurationProvider Build(IConfigurationBuilder builder)
            => new InMemoryConfigurationProvider(_values);
    }

    /// <summary>最小内存配置提供者：直接把字典作为扁平键值暴露。</summary>
    private sealed class InMemoryConfigurationProvider : ConfigurationProvider
    {
        private readonly IDictionary<string, string?> _values;

        public InMemoryConfigurationProvider(IDictionary<string, string?> values) => _values = values;

        public override void Load() => Data = new Dictionary<string, string?>(_values, StringComparer.OrdinalIgnoreCase);
    }
}
