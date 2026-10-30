using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Exceptions;
using TKW.Framework.Domain.KeyManagement;
using TKWF.Ext.WeChatOperations.DTOs;

namespace TKWF.Ext.WeChatOperations.Tests;

/// <summary>
/// <see cref="IChannelConfigService"/>（ChannelConfigService 门面）测试——凭证安全（Oracle P1）：
/// SaveChannelAsync 入参明文 → AES-GCM 密文落库（DB 读回密文非明文）；DTO 投影不含凭证（密文不回显）；
/// 解密仅出站瞬时（DbWeChatCredentialResolver——见其测试类）；冲突预查（ChannelId/AppId 唯一）。
/// <para>生产路径：<c>user.Use&lt;IChannelConfigService&gt;()</c> AOP（AddConstructibleService 守卫工厂）。</para>
/// </summary>
public class ChannelConfigServiceTests : WeChatOperationsTestHost
{
    /// <summary>SaveChannelAsync 新建——三凭证明文加密落库（DB 读回密文非明文 + keyed provider 可解密回明文）。</summary>
    [Fact]
    public async Task SaveAsync_New_CredentialsEncryptedAtRest()
    {
        using var _ = this;
        var (provider, user) = BindScope();
        var service = user.Use<IChannelConfigService>();
        var aesKey = WeChatOperationsTestSupport.GenerateEncodingAESKey();

        var id = await service.SaveChannelAsync(new SaveChannelRequest(
            Id: null, ChannelId: "ch-1", AppId: "wx0001", DisplayName: "主号", IsEnabled: true,
            ChannelType: "wechat_event", AppSecret: "secret-plain-1", Token: "token-plain-1", EncodingAesKey: aesKey),
            ct: TestContext.Current.CancellationToken);

        Assert.True(id > 0);

        // DB 读回——密文非明文
        var fsql = provider.GetRequiredService<IFreeSql>();
        var saved = await fsql.Select<WeChatChannelConfigEntity>().Where(c => c.Id == id)
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(saved);
        Assert.NotEqual("secret-plain-1", saved!.AppSecretCipher);
        Assert.NotEqual("token-plain-1", saved.TokenCipher);
        Assert.NotEqual(aesKey, saved.EncodingAESKeyCipher);
        Assert.False(string.IsNullOrWhiteSpace(saved.AppSecretCipher));
        Assert.False(string.IsNullOrWhiteSpace(saved.TokenCipher));
        Assert.False(string.IsNullOrWhiteSpace(saved.EncodingAESKeyCipher));

        // keyed provider 解密回明文（解密仅出站瞬时——凭证可恢复）
        var keys = provider.GetRequiredKeyedService<ISymmetricKeyProvider>(WeChatOperationsKeyProviderKeys.WeChatOperations);
        Assert.Equal("secret-plain-1", keys.Decrypt(saved.AppSecretCipher));
        Assert.Equal("token-plain-1", keys.Decrypt(saved.TokenCipher));
        Assert.Equal(aesKey, keys.Decrypt(saved.EncodingAESKeyCipher));
    }

    /// <summary>GetAsync DTO 投影不含凭证密文（HasCredentials 指示已配置；无 AppSecretCipher/TokenCipher/EncodingAESKeyCipher 属性）。</summary>
    [Fact]
    public async Task GetAsync_Dto_Projection_HidesCredentials()
    {
        using var _ = this;
        var (provider, user) = BindScope();
        var service = user.Use<IChannelConfigService>();
        await service.SaveChannelAsync(new SaveChannelRequest(
            Id: null, ChannelId: "ch-2", AppId: "wx0002", DisplayName: "二号", IsEnabled: true,
            ChannelType: "wechat_event", AppSecret: "secret-2"), ct: TestContext.Current.CancellationToken);

        var dto = await service.GetAsync("wx0002", ct: TestContext.Current.CancellationToken);

        Assert.NotNull(dto);
        Assert.Equal("ch-2", dto!.ChannelId);
        Assert.Equal("wx0002", dto.AppId);
        Assert.True(dto.HasCredentials);   // 已配置凭证（密文存在）
        // 投影不含凭证字段（Oracle P1 DTO 安全——密文不回显）
        Assert.Null(typeof(WeChatChannelConfigDto).GetProperty("AppSecretCipher"));
        Assert.Null(typeof(WeChatChannelConfigDto).GetProperty("TokenCipher"));
        Assert.Null(typeof(WeChatChannelConfigDto).GetProperty("EncodingAESKeyCipher"));
        Assert.Null(typeof(WeChatChannelConfigDto).GetProperty("AppSecret"));
    }

    /// <summary>SaveChannelAsync 更新——未传明文 → 库中既有密文保持不变（逐字段加密边界）。</summary>
    [Fact]
    public async Task SaveAsync_Update_KeepCipherWhenPlainNull()
    {
        using var _ = this;
        var (provider, user) = BindScope();
        var service = user.Use<IChannelConfigService>();
        var id = await service.SaveChannelAsync(new SaveChannelRequest(
            Id: null, ChannelId: "ch-3", AppId: "wx0003", DisplayName: "三号", IsEnabled: true,
            ChannelType: "wechat_event", AppSecret: "secret-3"), ct: TestContext.Current.CancellationToken);

        var fsql = provider.GetRequiredService<IFreeSql>();
        var before = await fsql.Select<WeChatChannelConfigEntity>().Where(c => c.Id == id)
            .FirstAsync(TestContext.Current.CancellationToken);

        // 更新——不传任何明文（null = 保持既有密文）
        await service.SaveChannelAsync(new SaveChannelRequest(
            Id: id, ChannelId: "ch-3", AppId: "wx0003", DisplayName: "三号-改名", IsEnabled: true,
            ChannelType: "wechat_event"), ct: TestContext.Current.CancellationToken);

        var after = await fsql.Select<WeChatChannelConfigEntity>().Where(c => c.Id == id)
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.Equal("三号-改名", after!.DisplayName);
        Assert.Equal(before!.AppSecretCipher, after.AppSecretCipher);   // 密文保持（未覆盖）
    }

    /// <summary>SaveChannelAsync 新建 ChannelId 冲突 → 拒绝（唯一索引 UX——预查语义）。</summary>
    [Fact]
    public async Task SaveAsync_ChannelIdConflict_Throws()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<IChannelConfigService>();
        await service.SaveChannelAsync(new SaveChannelRequest(
            Id: null, ChannelId: "ch-dup", AppId: "wx-dup-1", DisplayName: "第一", IsEnabled: true,
            ChannelType: "wechat_event"), ct: TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveChannelAsync(new SaveChannelRequest(
            Id: null, ChannelId: "ch-dup",   // 同 ChannelId
            AppId: "wx-dup-2", DisplayName: "第二", IsEnabled: true,
            ChannelType: "wechat_event"), ct: TestContext.Current.CancellationToken));

        Assert.Contains("冲突", ex.Message);
    }

    /// <summary>DeleteChannelAsync 删除配置（物理删——删除后出站凭证经 Options 降级解析）。</summary>
    [Fact]
    public async Task DeleteAsync_Removes()
    {
        using var _ = this;
        var (provider, user) = BindScope();
        var service = user.Use<IChannelConfigService>();
        var id = await service.SaveChannelAsync(new SaveChannelRequest(
            Id: null, ChannelId: "ch-del", AppId: "wx-del", DisplayName: "待删", IsEnabled: true,
            ChannelType: "wechat_event"), ct: TestContext.Current.CancellationToken);

        var deleted = await service.DeleteChannelAsync(id, ct: TestContext.Current.CancellationToken);

        Assert.True(deleted);
        var fsql = provider.GetRequiredService<IFreeSql>();
        Assert.Equal(0, await fsql.Select<WeChatChannelConfigEntity>().Where(c => c.Id == id)
            .CountAsync(TestContext.Current.CancellationToken));
    }

    // ============================ D03 1.6 channel alias 规则 ============================

    /// <summary>SaveChannelAsync 新建——请求 Alias 不合法 slug → DomainException CHANNEL_ALIAS_INVALID（消息含实际值）。
    /// 非法样本：2 位（"AB"——降归一后过短）、1 位（"a"）、下划线（"a_b"）、空格（"a b"）、65 位超长。</summary>
    [Fact]
    public async Task Save_NewChannel_InvalidAlias_Throws()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<IChannelConfigService>();

        string[] invalidAliases = { "AB", "a", "a_b", "a b", new string('a', 65) };
        foreach (var alias in invalidAliases)
        {
            var ex = await Assert.ThrowsAsync<DomainException>(() => service.SaveChannelAsync(new SaveChannelRequest(
                Id: null,
                ChannelId: "ch-inv-" + Guid.NewGuid().ToString("N")[..6],
                AppId: "wx-inv-" + Guid.NewGuid().ToString("N")[..6],
                DisplayName: "非法别名", IsEnabled: true,
                ChannelType: "wechat_event", Alias: alias),
                ct: TestContext.Current.CancellationToken));

            Assert.Equal("CHANNEL_ALIAS_INVALID", ex.ErrorCode);
            Assert.Contains(alias, ex.Message);
        }
    }

    /// <summary>SaveChannelAsync 新建——合法 Alias 归一化落库（"My-Shop" → "my-shop"——Trim + ToLowerInvariant）。</summary>
    [Fact]
    public async Task Save_NewChannel_ValidAlias_Normalized()
    {
        using var _ = this;
        var (provider, user) = BindScope();
        var service = user.Use<IChannelConfigService>();

        var id = await service.SaveChannelAsync(new SaveChannelRequest(
            Id: null, ChannelId: "ch-valid", AppId: "wx-valid", DisplayName: "合法别名", IsEnabled: true,
            ChannelType: "wechat_event", Alias: " My-Shop "), ct: TestContext.Current.CancellationToken);

        var fsql = provider.GetRequiredService<IFreeSql>();
        var saved = await fsql.Select<WeChatChannelConfigEntity>().Where(c => c.Id == id)
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.Equal("my-shop", saved!.Alias);
    }

    /// <summary>SaveChannelAsync 更新——既有 alias="shop-a" 请求改 "shop-b" → DomainException CHANNEL_ALIAS_IMMUTABLE
    /// （alias 不可修改，如需改名请新建通道）。</summary>
    [Fact]
    public async Task Save_UpdateChannel_ChangeAlias_Throws()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<IChannelConfigService>();
        var id = await service.SaveChannelAsync(new SaveChannelRequest(
            Id: null, ChannelId: "ch-upd1", AppId: "wx-upd1", DisplayName: "一号", IsEnabled: true,
            ChannelType: "wechat_event", Alias: "shop-a"), ct: TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.SaveChannelAsync(new SaveChannelRequest(
            Id: id, ChannelId: "ch-upd1", AppId: "wx-upd1", DisplayName: "一号", IsEnabled: true,
            ChannelType: "wechat_event", Alias: "shop-b"), ct: TestContext.Current.CancellationToken));

        Assert.Equal("CHANNEL_ALIAS_IMMUTABLE", ex.ErrorCode);
    }

    /// <summary>SaveChannelAsync 更新——请求 Alias 与既有相同 → 成功（不可变守卫仅拦修改，同值放行）。</summary>
    [Fact]
    public async Task Save_UpdateChannel_SameAlias_Ok()
    {
        using var _ = this;
        var (provider, user) = BindScope();
        var service = user.Use<IChannelConfigService>();
        var id = await service.SaveChannelAsync(new SaveChannelRequest(
            Id: null, ChannelId: "ch-upd2", AppId: "wx-upd2", DisplayName: "二号", IsEnabled: true,
            ChannelType: "wechat_event", Alias: "shop-a"), ct: TestContext.Current.CancellationToken);

        await service.SaveChannelAsync(new SaveChannelRequest(
            Id: id, ChannelId: "ch-upd2", AppId: "wx-upd2", DisplayName: "二号-改名", IsEnabled: true,
            ChannelType: "wechat_event", Alias: "shop-a"), ct: TestContext.Current.CancellationToken);

        var fsql = provider.GetRequiredService<IFreeSql>();
        var saved = await fsql.Select<WeChatChannelConfigEntity>().Where(c => c.Id == id)
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.Equal("shop-a", saved!.Alias);
        Assert.Equal("二号-改名", saved.DisplayName);
    }

    /// <summary>SaveChannelAsync 更新——请求 Alias=null → 保留既有 alias（不清空；null 请求不写 Alias 列）。</summary>
    [Fact]
    public async Task Save_UpdateChannel_NullAlias_KeepsExisting()
    {
        using var _ = this;
        var (provider, user) = BindScope();
        var service = user.Use<IChannelConfigService>();
        var id = await service.SaveChannelAsync(new SaveChannelRequest(
            Id: null, ChannelId: "ch-upd3", AppId: "wx-upd3", DisplayName: "三号", IsEnabled: true,
            ChannelType: "wechat_event", Alias: "shop-a"), ct: TestContext.Current.CancellationToken);

        await service.SaveChannelAsync(new SaveChannelRequest(
            Id: id, ChannelId: "ch-upd3", AppId: "wx-upd3", DisplayName: "三号-改名", IsEnabled: true,
            ChannelType: "wechat_event"), ct: TestContext.Current.CancellationToken);

        var fsql = provider.GetRequiredService<IFreeSql>();
        var saved = await fsql.Select<WeChatChannelConfigEntity>().Where(c => c.Id == id)
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.Equal("shop-a", saved!.Alias);
    }

    /// <summary>SaveChannelAsync 更新——既有 alias 为空，请求 "shop-b" → 一次性设置成功（校验通过后写入）。</summary>
    [Fact]
    public async Task Save_UpdateChannel_SetAliasOnce_Ok()
    {
        using var _ = this;
        var (provider, user) = BindScope();
        var service = user.Use<IChannelConfigService>();
        var id = await service.SaveChannelAsync(new SaveChannelRequest(
            Id: null, ChannelId: "ch-upd4", AppId: "wx-upd4", DisplayName: "四号", IsEnabled: true,
            ChannelType: "wechat_event"), ct: TestContext.Current.CancellationToken);

        await service.SaveChannelAsync(new SaveChannelRequest(
            Id: id, ChannelId: "ch-upd4", AppId: "wx-upd4", DisplayName: "四号", IsEnabled: true,
            ChannelType: "wechat_event", Alias: "shop-b"), ct: TestContext.Current.CancellationToken);

        var fsql = provider.GetRequiredService<IFreeSql>();
        var saved = await fsql.Select<WeChatChannelConfigEntity>().Where(c => c.Id == id)
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.Equal("shop-b", saved!.Alias);
    }
}
