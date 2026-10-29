using System;
using System.Collections.Generic;
using System.Text.Json;
using TKWF.Ext.Federation;
using Xunit;

namespace TKWF.Ext.Federation.Tests;

/// <summary>
/// D03 联邦免登载荷切换（1.1/1.3）——FA 契约静态测试（纯契约，零 DI 零宿主）。
/// <para>覆盖：① payload 字节预算（R9 防御线 <see cref="FederatedTicketPayloadBudget"/>）；②
/// <see cref="ChannelTypeRegistry"/> provider×channelType 取值矩阵（命名碰撞红线——协议类型不得入
/// channelType 槽位）；③ <see cref="FederatedTicketPayloadBuilder"/> 签票侧组装（显式/回退/非法组合
/// fail-fast/resolver 覆盖/扩展字典合入）；④ <see cref="FederatedTicketPayloadExtensions.ToFederatedIdentity"/>
/// 兑付适配（协议类型优先 → 形态回退 → null）；⑤ record 不可变 + JSON 序列化往返逐字段等价。</para>
/// </summary>
public sealed class FederatedTicketPayloadContractTests
{
    // ── ① payload 字节预算（R9） ──────────────────────────────────────────────

    /// <summary>全字段 payload（含扩展字典）序列化字节数 &lt; MaxPayloadBytes(1024) 且 &lt; SealedTicket 硬上限（R9 防御线锁定）。</summary>
    [Fact]
    public void Budget_Serialize_UnderLimit()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = new FederatedTicketPayload(
            Provider: "wechat",
            ChannelType: "mp_service",
            ChannelId: "gh_xxx1234567890",
            ChannelAlias: "prana",
            ExternalUserId: "oXXXX_abcdefghijklmnopqrstuvwxyz0123456789",
            UnionId: "u_union_abcdef123456",
            PlatformUid: "puid_abcdef1234567890",
            Assurance: "userinfo",
            Iat: now,
            Exp: now + 300,
            Jti: "Jjjjjjjjjjjjjjjjjjjjjjjjjjjjjjjj")
        {
            Extensions = new Dictionary<string, string?>
            {
                [FederatedTicketPayloadExtensions.ProtocolTypeKey] = "wechat_oauth",
                [FederatedTicketPayloadExtensions.IsTestKey] = "true",
            },
        };

        int bytes = JsonSerializer.SerializeToUtf8Bytes(payload).Length;

        Assert.True(bytes < FederatedTicketPayloadBudget.MaxPayloadBytes,
            $"payload 序列化 {bytes}B 须 < MaxPayloadBytes({FederatedTicketPayloadBudget.MaxPayloadBytes})");
        Assert.True(bytes < FederatedTicketPayloadBudget.SealedTicketHardLimit,
            $"payload 序列化 {bytes}B 须 < SealedTicketHardLimit({FederatedTicketPayloadBudget.SealedTicketHardLimit})");
    }

    // ── ② ChannelTypeRegistry 取值矩阵 ───────────────────────────────────────

    /// <summary>合法形态矩阵（wechat 4 合法值）与非法组合拒绝（空入参 / 协议类型 / 跨 provider）。</summary>
    [Theory]
    [InlineData("wechat", "mp_service", true)]
    [InlineData("wechat", "mp_subscription", true)]
    [InlineData("wechat", "mini_program", true)]
    [InlineData("wechat", "work_wechat", true)]
    [InlineData("wechat", "wechat_oauth", false)]          // 协议类型——命名碰撞红线，非形态
    [InlineData("wechat", "wechat_event", false)]          // 协议类型——命名碰撞红线，非形态
    [InlineData("wechat", "alipay_mini_program", false)]   // 跨 provider 非法组合
    [InlineData("", "", false)]                            // 空入参
    [InlineData("wechat", "", false)]                      // 空白形态
    [InlineData("unregistered_provider", "mp_service", false)] // 未注册 provider
    public void Registry_IsLegal_Matrix(string provider, string channelType, bool expected)
    {
        Assert.Equal(expected, ChannelTypeRegistry.IsLegal(provider, channelType));
    }

    /// <summary>合法组合断言不抛（签票入口 fail-fast 正路径）。</summary>
    [Fact]
    public void Registry_AssertLegal_Valid_DoesNotThrow()
    {
        ChannelTypeRegistry.AssertLegal("wechat", "mp_service");
    }

    /// <summary>误传协议类型（wechat_oauth）→ ArgumentException 且消息含合法形态（mp_ 前缀）提示（命名碰撞红线的装配期拦截）。</summary>
    [Fact]
    public void Registry_AssertLegal_ProtocolTypeMismatch_ThrowsWithHint()
    {
        var ex = Assert.Throws<ArgumentException>(() => ChannelTypeRegistry.AssertLegal("wechat", "wechat_oauth"));

        Assert.Equal("channelType", ex.ParamName);
        Assert.Contains("mp_", ex.Message);        // 合法值提示（mp_subscription/mp_service/mini_program/work_wechat）
        Assert.Contains("非协议类型", ex.Message);
    }

    /// <summary>未注册 provider → ArgumentException（消息提示先登记注册表）。</summary>
    [Fact]
    public void Registry_AssertLegal_UnregisteredProvider_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => ChannelTypeRegistry.AssertLegal("alipay", "mp_service"));

        Assert.Equal("channelType", ex.ParamName);
        Assert.Contains("未注册", ex.Message);
    }

    // ── ③ Builder 签票侧组装 ─────────────────────────────────────────────────

    /// <summary>全参数显式组装：字段逐项对齐 + Exp-Iat==TTL + Jti 满足强度 + protocol_type 双写 Extensions。</summary>
    [Fact]
    public void Build_AllParamsExplicit_AssemblesFields()
    {
        var now = DateTimeOffset.UtcNow;
        var ttl = TimeSpan.FromSeconds(300);
        var payload = FederatedTicketPayloadBuilder.Build(
            provider: "wechat",
            protocolChannelType: "wechat_oauth",
            channelId: "gh-1",
            externalUserId: "o-1",
            channelForm: "mp_service",
            channelAlias: "prana",
            platformUid: "u-1",
            assurance: "userinfo",
            ttl: ttl);

        Assert.Equal("wechat", payload.Provider);
        Assert.Equal("mp_service", payload.ChannelType);
        Assert.Equal("gh-1", payload.ChannelId);
        Assert.Equal("prana", payload.ChannelAlias);
        Assert.Equal("o-1", payload.ExternalUserId);
        Assert.Equal("u-1", payload.PlatformUid);
        Assert.Equal("userinfo", payload.Assurance);
        Assert.Equal(300, payload.Exp - payload.Iat);
        Assert.NotNull(payload.Jti);
        Assert.True(payload.Jti.Length >= 16, $"Jti 长度 {payload.Jti.Length} 须 ≥ 16（防重放判据）");
        Assert.Equal("wechat_oauth", payload.Extensions[FederatedTicketPayloadExtensions.ProtocolTypeKey]);
    }

    /// <summary>形态回退（channelForm=null + 协议类型可映射）：wechat_event→mp_subscription、wechat_oauth→mp_service（与兑付回退表成对互逆）。</summary>
    [Theory]
    [InlineData("wechat_event", "mp_subscription")]
    [InlineData("wechat_oauth", "mp_service")]
    public void Build_ChannelFormNull_FallsBackFromProtocolType(string protocolChannelType, string expectedForm)
    {
        var payload = FederatedTicketPayloadBuilder.Build(
            provider: "wechat",
            protocolChannelType: protocolChannelType,
            channelId: "gh-1",
            externalUserId: "o-1");

        Assert.Equal(expectedForm, payload.ChannelType);
    }

    /// <summary>非法组合 fail-fast：协议类型无法映射且 channelForm 非法（"bogus"）→ AssertLegal 在组装时抛 ArgumentException。</summary>
    [Fact]
    public void Build_InvalidChannelForm_FailsFast()
    {
        var ex = Assert.Throws<ArgumentException>(() => FederatedTicketPayloadBuilder.Build(
            provider: "wechat",
            protocolChannelType: "wechat_oauth",   // 映射 mp_service 可用但 channelForm 显式非法 → 不落回退
            channelId: "gh-1",
            externalUserId: "o-1",
            channelForm: "bogus"));

        Assert.Equal("channelType", ex.ParamName);
    }

    /// <summary>resolver 委托：返回 (form, alias, provider) 覆盖入参（channelForm/channelAlias/provider 取 resolver 解析值）。</summary>
    [Fact]
    public void Build_Resolver_OverridesInputArgs()
    {
        var payload = FederatedTicketPayloadBuilder.Build(
            provider: "alipay",                    // 入参被 resolver 覆盖
            protocolChannelType: "wechat_oauth",
            channelId: "gh-2",
            externalUserId: "o-2",
            channelForm: "mp_service",             // 入参被 resolver 覆盖
            channelAlias: "prana",                 // 入参被 resolver 覆盖
            resolver: static _ => ("mini_program", "wx-mini", "wechat"));

        Assert.Equal("wechat", payload.Provider);
        Assert.Equal("mini_program", payload.ChannelType);
        Assert.Equal("wx-mini", payload.ChannelAlias);
        Assert.Equal("gh-2", payload.ChannelId);   // 不变键透传
    }

    /// <summary>resolver 仅解析形态/别名（各项可空）：缺省项回落入参（null 不回退 → 回退映射/原值）。</summary>
    [Fact]
    public void Build_Resolver_PartialNull_FallsBackToArgs()
    {
        var payload = FederatedTicketPayloadBuilder.Build(
            provider: "wechat",
            protocolChannelType: "wechat_oauth",
            channelId: "gh-3",
            externalUserId: "o-3",
            channelForm: "mp_service",
            channelAlias: "prana",
            resolver: static _ => (null, null, "wechat"));

        Assert.Equal("mp_service", payload.ChannelType);   // resolvedForm null → 保留入参
        Assert.Equal("prana", payload.ChannelAlias);       // resolvedAlias null → 保留入参
    }

    /// <summary>扩展字典合入：调用方传入键保留（is_test）+ protocol_type 由本类规范化双写（调用方覆盖无效）。</summary>
    [Fact]
    public void Build_Extensions_MergedWithProtocolType()
    {
        var payload = FederatedTicketPayloadBuilder.Build(
            provider: "wechat",
            protocolChannelType: "wechat_event",
            channelId: "gh-4",
            externalUserId: "o-4",
            extensions: new Dictionary<string, string?>
            {
                [FederatedTicketPayloadExtensions.IsTestKey] = "true",
                [FederatedTicketPayloadExtensions.ProtocolTypeKey] = "wechat_oauth",  // 调用方误设 → 规范化覆盖
            });

        Assert.Equal("true", payload.Extensions[FederatedTicketPayloadExtensions.IsTestKey]);
        Assert.Equal("wechat_event", payload.Extensions[FederatedTicketPayloadExtensions.ProtocolTypeKey]);
        Assert.Equal(2, payload.Extensions.Count);
    }

    // ── ④ ToFederatedIdentity 兑付适配（FederatedTicketPayloadExtensions） ───

    /// <summary>协议类型优先：Extensions["protocol_type"] 存在 → ChannelType 取协议类型（覆盖形态回退）；字段映射与 AuthLevel==2。</summary>
    [Fact]
    public void ToFederatedIdentity_ProtocolTypePresent_PriorityOverForm()
    {
        var payload = new FederatedTicketPayload(
            Provider: "wechat", ChannelType: "mp_service", ChannelId: "gh-5",
            ChannelAlias: "prana", ExternalUserId: "o-5", UnionId: null, PlatformUid: "puid-5",
            Assurance: "userinfo", Iat: 1, Exp: 301, Jti: "jti-5")
        {
            Extensions = new Dictionary<string, string?> { [FederatedTicketPayloadExtensions.ProtocolTypeKey] = "wechat_event" },
        };

        var identity = payload.ToFederatedIdentity();

        Assert.Equal("wechat_event", identity.ChannelType);   // 协议类型优先（mp_service 形态回退应为 wechat_oauth——被覆盖）
        Assert.Equal("puid-5", identity.UId);                 // UId == PlatformUid
        Assert.Equal("gh-5", identity.ChannelId);             // 透传
        Assert.Equal("o-5", identity.ExternalUserId);         // 透传
        Assert.Equal(2, identity.AuthLevel);                  // 联邦快捷恒定
    }

    /// <summary>形态回退：无 protocol_type + ChannelType=mp_service → 回退表 wechat_oauth（旧票/未双写兼容路径）。</summary>
    [Theory]
    [InlineData("mp_service", "wechat_oauth")]
    [InlineData("mp_subscription", "wechat_event")]
    [InlineData("mini_program", "wechat_oauth")]
    [InlineData("work_wechat", "wechat_event")]
    public void ToFederatedIdentity_NoProtocolType_FallsBackFromForm(string channelType, string expectedProtocolType)
    {
        var payload = new FederatedTicketPayload(
            Provider: "wechat", ChannelType: channelType, ChannelId: "gh-6",
            ChannelAlias: null, ExternalUserId: "o-6", UnionId: null, PlatformUid: "puid-6",
            Assurance: "event", Iat: 1, Exp: 301, Jti: "jti-6");

        var identity = payload.ToFederatedIdentity();

        Assert.Equal(expectedProtocolType, identity.ChannelType);
    }

    /// <summary>未知形态：无 protocol_type + ChannelType 未注册形态 → ChannelType null（协议类型无可推断）。</summary>
    [Fact]
    public void ToFederatedIdentity_UnknownForm_ChannelTypeIsNull()
    {
        var payload = new FederatedTicketPayload(
            Provider: "wechat", ChannelType: "some_future_form", ChannelId: "gh-7",
            ChannelAlias: null, ExternalUserId: "o-7", UnionId: null, PlatformUid: "puid-7",
            Assurance: "event", Iat: 1, Exp: 301, Jti: "jti-7");

        var identity = payload.ToFederatedIdentity();

        Assert.Null(identity.ChannelType);
    }

    /// <summary>PlatformUid null → UId==""（空串兜底，不抛）。</summary>
    [Fact]
    public void ToFederatedIdentity_PlatformUidNull_UIdIsEmpty()
    {
        var payload = new FederatedTicketPayload(
            Provider: "wechat", ChannelType: "mp_service", ChannelId: "gh-8",
            ChannelAlias: null, ExternalUserId: "o-8", UnionId: null, PlatformUid: null,
            Assurance: "event", Iat: 1, Exp: 301, Jti: "jti-8");

        var identity = payload.ToFederatedIdentity();

        Assert.Equal(string.Empty, identity.UId);
        Assert.Equal("o-8", identity.ExternalUserId);   // UId 兜底不影响外部键透传
    }

    /// <summary>null 参 → ArgumentNullException（扩展方法入口守卫）。</summary>
    [Fact]
    public void ToFederatedIdentity_NullPayload_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ((FederatedTicketPayload?)null)!.ToFederatedIdentity());
    }

    // ── ⑤ record 不可变 + JSON 序列化往返 ───────────────────────────────────

    /// <summary>Serialize → Deserialize 往返逐字段等价（含 Extensions 字典键保真——Ordinal 字典序列化键不转换）。</summary>
    [Fact]
    public void RoundTrip_SerializeDeserialize_FieldByFieldEqual()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = new FederatedTicketPayload(
            Provider: "wechat", ChannelType: "mini_program", ChannelId: "gh-9",
            ChannelAlias: "wx-mini", ExternalUserId: "o-9", UnionId: "u-9", PlatformUid: "puid-9",
            Assurance: "base", Iat: now, Exp: now + 600, Jti: "J9J9J9J9J9J9J9J9J9J9J9J9")
        {
            Extensions = new Dictionary<string, string?>
            {
                [FederatedTicketPayloadExtensions.ProtocolTypeKey] = "wechat_oauth",
                [FederatedTicketPayloadExtensions.IsTestKey] = "true",
            },
        };

        var json = JsonSerializer.Serialize(payload);
        var roundTripped = JsonSerializer.Deserialize<FederatedTicketPayload>(json)!;

        Assert.Equal("wechat", roundTripped.Provider);
        Assert.Equal("mini_program", roundTripped.ChannelType);
        Assert.Equal("gh-9", roundTripped.ChannelId);
        Assert.Equal("wx-mini", roundTripped.ChannelAlias);
        Assert.Equal("o-9", roundTripped.ExternalUserId);
        Assert.Equal("u-9", roundTripped.UnionId);
        Assert.Equal("puid-9", roundTripped.PlatformUid);
        Assert.Equal("base", roundTripped.Assurance);
        Assert.Equal(now, roundTripped.Iat);
        Assert.Equal(now + 600, roundTripped.Exp);
        Assert.Equal(payload.Jti, roundTripped.Jti);
        // 扩展字典逐键等价（System.Text.Json 反序列化新字典实例——record 值相等对字典成员走引用比较，须逐键断言）
        Assert.Equal(2, roundTripped.Extensions.Count);
        Assert.Equal(payload.Extensions[FederatedTicketPayloadExtensions.ProtocolTypeKey],
            roundTripped.Extensions[FederatedTicketPayloadExtensions.ProtocolTypeKey]);
        Assert.Equal(payload.Extensions[FederatedTicketPayloadExtensions.IsTestKey],
            roundTripped.Extensions[FederatedTicketPayloadExtensions.IsTestKey]);
    }

    /// <summary>JSON 序列化键保真：Extensions 字典键原样输出（snake_case 键 protocol_type 不被改写）。</summary>
    [Fact]
    public void RoundTrip_ExtensionsKeys_PreservedInJson()
    {
        var payload = new FederatedTicketPayload(
            Provider: "wechat", ChannelType: "mp_service", ChannelId: "gh-10",
            ChannelAlias: null, ExternalUserId: "o-10", UnionId: null, PlatformUid: null,
            Assurance: "event", Iat: 1, Exp: 301, Jti: "jti-10")
        {
            Extensions = new Dictionary<string, string?>
            {
                [FederatedTicketPayloadExtensions.ProtocolTypeKey] = "wechat_oauth",
                [FederatedTicketPayloadExtensions.IsTestKey] = "false",
            },
        };

        var json = JsonSerializer.Serialize(payload);

        Assert.Contains("\"protocol_type\":\"wechat_oauth\"", json);
        Assert.Contains("\"is_test\":\"false\"", json);
    }

    /// <summary>with 表达式创建副本不改原实例（record 不可变性——原 Jti/ChannelType 不变）。</summary>
    [Fact]
    public void WithExpression_OriginalInstance_Unchanged()
    {
        var original = new FederatedTicketPayload(
            Provider: "wechat", ChannelType: "mp_service", ChannelId: "gh-11",
            ChannelAlias: null, ExternalUserId: "o-11", UnionId: null, PlatformUid: null,
            Assurance: "event", Iat: 1, Exp: 301, Jti: "original-jti-11");

        var copy = original with { ChannelId = "gh-11-copy" };

        Assert.Equal("gh-11-copy", copy.ChannelId);       // 副本变化
        Assert.Equal("gh-11", original.ChannelId);        // 原实例不变
        Assert.Equal("original-jti-11", original.Jti);
        Assert.Equal("original-jti-11", copy.Jti);        // 未触碰字段共享
    }
}