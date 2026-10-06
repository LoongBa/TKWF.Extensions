using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TKW.Framework.Domain.FreeSql;

namespace TKWF.Ext.AuthCenter.Tests;

/// <summary>
/// N2 联盟锚点映射策略（Oracle 评审 P1-4 定案）——<see cref="SsoAccountAnchor"/> 生成器测试：
/// 锚点 = TKWF 自生成平台无关稳定值（<c>anc_</c> 前缀 + CSPRNG 16 字节 base64url——26 字符，非 uid/unionid/null）。
/// <para>验证：格式（前缀 + base64url 字符集无填充）/ 唯一性（CSPRNG 不可预测）/ 生产路径闭环
/// （生成值经 <see cref="ISsoAccountLinkService.SetFederationAnchorAsync"/> 落库 + 反查直认）。</para>
/// </summary>
public class SsoAccountAnchorTests
{
    [Fact]
    public void Create_StartsWithPrefix_AndBase64UrlCharset()
    {
        var anchor = SsoAccountAnchor.Create();

        Assert.StartsWith("anc_", anchor);
        Assert.Equal(26, anchor.Length);   // anc_(4) + base64url(16B→22 无填充)
        Assert.Matches(new Regex("^anc_[A-Za-z0-9_-]+$"), anchor);  // base64url 字符集——无 +、/、= 转义歧义
        Assert.DoesNotContain('+', anchor);
        Assert.DoesNotContain('/', anchor);
        Assert.DoesNotContain('=', anchor);
    }

    [Fact]
    public void Create_DefaultLength_IsStable()
    {
        // 锚点值长度稳定（CSPRNG 16 字节 base64url 恒 22 字符 + 前缀 4）——列长度/索引规划依据
        for (var i = 0; i < 32; i++)
            Assert.Equal(26, SsoAccountAnchor.Create().Length);
    }

    [Fact]
    public void Create_Unique_AcrossCalls()
    {
        // CSPRNG——两次生成不同（锚点具账号关联敏感性，不可预测防枚举）
        var a = SsoAccountAnchor.Create();
        var b = SsoAccountAnchor.Create();
        Assert.NotEqual(a, b);
    }

    [Fact]
    public async Task GeneratedAnchor_CanSetAndGetByAnchor_ProductionPath()
    {
        // N2 定案生产路径：Federation 编排层生成 anc_ 值 → SetFederationAnchorAsync 写
        // AuthAccount.FederationAnchorOpenId → GetByFederationAnchorAsync 独立反查直认（不退化 ≡ GetByUId）
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var stub = AuthenticationTestHost.CreateStub(fsql);
        var authDs = stub.Use<AuthAccountEntityDataService>();
        var linkSvc = new AuthAccountQueryService(stub);

        await authDs.EntityCreateAsync(new AuthAccountEntity { UId = "u-100", Phone = "13800138000" }, default);
        var anchor = SsoAccountAnchor.Create();

        var updated = await linkSvc.SetFederationAnchorAsync("u-100", anchor, default);
        Assert.Equal(anchor, updated.FederationAnchorOpenId);

        var byAnchor = await linkSvc.GetByFederationAnchorAsync(anchor, default);
        Assert.NotNull(byAnchor);
        Assert.Equal("u-100", byAnchor!.UId);

        // 未绑定者反查不误中（锚点不透明——随机值无碰撞）
        Assert.Null(await linkSvc.GetByFederationAnchorAsync(SsoAccountAnchor.Create(), default));
    }
}