using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace TKWF.Ext.Authentication;

/// <summary>
/// 开发模式临时 RSA 密钥集进程内缓存（V0.5.3——转达-2026-10-05 修复）。
/// <para>缺陷：`TokenService` 在开发模式（SigningKeyPath 未配置 &amp;&amp; IsProduction=false）下每次
/// <see cref="LoadKeysCore"/> 都 <c>RSA.Create()</c> 新建临时密钥且无进程内共享——每个 scoped 实例
/// （每请求一个）各自 <c>Lazy&lt;RsaKeySet&gt;</c> → 签发实例与验签实例（LocalJwtTokenVerifier 经
/// <c>User.Use&lt;ITokenService&gt;()</c> 解析的另一实例）密钥集不同 → **INVALID_SIGNATURE**（跨实例验签失败）。</para>
/// <para>修复：开发模式密钥集按进程静态缓存（双重校验锁，镜像 <see cref="PlatformCredentialKeyStore"/>
/// 既有模式）；生产分支（PEM 文件）天然跨实例一致，**绝不**走本缓存（生产缺 PEM 须 fail-fast）。</para>
/// <para>缓存整个 <see cref="RsaKeySet"/>（CurrentKid + SigningKey + VerifyKeys）——签发/验签需一致性密钥对；
/// 勿只缓存 SigningKey 而每次重算派生公钥。测试隔离经 <see cref="ResetForTests"/>（<c>InternalsVisibleTo</c>）。</para>
/// </summary>
internal static class DevRsaKeyCache
{
    // volatile：DCL 教科书完整性（Oracle 评审 MINOR——dev-only 实际无影响，ARM 弱内存模型零成本保险）
    private static volatile TokenService.RsaKeySet? _cached;
    private static readonly object Gate = new();

    /// <summary>
    /// 取开发模式密钥集（双重校验锁——并发首个调用者创建，其余复用）。
    /// <paramref name="factory"/> 仅首次执行（后续直接返回缓存）；由 <see cref="TokenService.LoadKeysCore"/>
    /// dev 分支传入（生成 RSA.Create 临时密钥 + verifyKeys 构建）。
    /// </summary>
    public static TokenService.RsaKeySet GetOrCreate(Func<TokenService.RsaKeySet> factory)
    {
        if (_cached != null) return _cached;
        lock (Gate)
        {
            if (_cached != null) return _cached;
            _cached = factory();
            return _cached;
        }
    }

    /// <summary>
    /// 测试隔离钩子——清空缓存并释放已缓存密钥（RSA 实现 IDisposable，防托管资源泄漏）。
    /// 测试 setup/teardown 调用；进程内仅 dev 模式缓存，生产路径零接触（安全）。
    /// </summary>
    public static void ResetForTests()
    {
        lock (Gate)
        {
            if (_cached != null)
            {
                _cached.SigningKey.Dispose();
                foreach (var kv in _cached.VerifyKeys)
                    kv.Value.Dispose();
                _cached = null;
            }
        }
    }
}
