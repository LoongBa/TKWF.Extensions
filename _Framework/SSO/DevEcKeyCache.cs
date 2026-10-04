using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace TKWF.Ext.SSO;

/// <summary>
/// 开发模式临时 EC 密钥集进程内缓存（SSO v0.1.0——对齐 Authentication DevRsaKeyCache V0.5.3 模式）。
/// <para>缺陷预防：开发模式（SigningKeyPath 未配置 &amp;&amp; !IsProduction）下 <c>Token2Service.LoadKeysCore</c>
/// 若每次 <c>ECDsa.Create()</c> 新建临时密钥且无进程内共享——每个 scoped 实例（每请求一个）各自
/// <c>Lazy&lt;EcKeySet&gt;</c> → 签发实例与验签实例密钥集不同 → <b>INVALID_SIGNATURE</b>（跨实例验签失败）。</para>
/// <para>修复：开发模式密钥集按进程静态缓存（双重校验锁）；生产分支（PEM 文件）天然跨实例一致，
/// <b>绝不</b>走本缓存（生产缺 PEM 须 fail-fast）。缓存整个 <see cref="Token2Service.EcKeySet"/>
/// （CurrentKid + SigningKey + VerifyKeys）——签发/验签需一致性密钥对。</para>
/// </summary>
internal static class DevEcKeyCache
{
    // volatile：DCL 教科书完整性（对齐 DevRsaKeyCache 先例）
    private static volatile Token2Service.EcKeySet? _cached;
    private static readonly object Gate = new();

    /// <summary>取开发模式密钥集（双重校验锁——并发首个调用者创建，其余复用）。
    /// <paramref name="factory"/> 仅首次执行；由 <see cref="Token2Service.LoadKeysCore"/> dev 分支传入。</summary>
    public static Token2Service.EcKeySet GetOrCreate(Func<Token2Service.EcKeySet> factory)
    {
        if (_cached != null) return _cached;
        lock (Gate)
        {
            if (_cached != null) return _cached;
            _cached = factory();
            return _cached;
        }
    }

    /// <summary>测试隔离钩子——清空缓存并释放已缓存密钥（ECDsa 实现 IDisposable，防托管资源泄漏）。</summary>
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
