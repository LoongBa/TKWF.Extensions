using System.Collections.Generic;
using TKW.Framework.Domain;

namespace TKWF.Ext.WeChatOperations;

/// <summary>
/// 微信公众号运营扩展配置（<c>TKWF:WeChatOperations</c> 节——运营侧领域配置）。
/// <para>配置分层（AGENTS §8）：领域决策（默认兜底文案等）归本节；表现层暴露面（REST 路由）由 SG1b
/// <c>[GenerateController]</c> 生成端点的 <c>[ApiExpose(RestRoute)]</c> 常量承载（V0.4.0 起替换手写
/// WebExtension——路由编译期确定性 + 配置单一事实源）；领域行为无 Web 层镜像开关（防表现层绕过）。</para>
/// <para><see cref="SecretEncryptionKeyPath"/>/<see cref="IsProduction"/>：凭证 AES-GCM 密钥文件路径/生产模式
/// 标记（keyed <see cref="TKW.Framework.Domain.KeyManagement.ISymmetricKeyProvider"/> 键 WeChatOperations——
/// 生产缺密钥 fail-fast / 开发随机兜底，对齐 AuthCenter E4）。</para>
/// </summary>
[Options("TKWF:WeChatOperations")]
public class WeChatOperationsOptions
{
    /// <summary>默认兜底文案（DB 规则 / template 均未命中时——<see cref="WeChatOperationsReplyHandler"/> 兜底返回；
    /// 亦作消息业务总开关关闭时的统一维护提示——复用不新增键）。</summary>
    public string DefaultReplyText { get; set; } = "感谢您的关注，请稍后再试。";

    /// <summary>消息业务总开关（默认 <c>true</c>——既有行为零回归；关闭后关键词/欢迎语/兜底等本地回复全部停发，
    /// 统一返回维护提示 <see cref="DefaultReplyText"/>——DMP-Lite 5.8"消息业务总开关"；<b>仅转发网关不受控</b>
    /// ——<see cref="WeChatOperationsReplyHandler"/> 网关 RouteAsync 先行执行仍转发生效）。</summary>
    public bool MessageBusinessEnabled { get; set; } = true;

    /// <summary>凭证 AES-GCM 密钥文件路径（keyed ISymmetricKeyProvider——生产缺密钥 fail-fast / 开发随机兜底）。</summary>
    public string? SecretEncryptionKeyPath { get; set; }

    /// <summary>生产模式（true=缺密钥拒绝启动，禁 dev 随机兜底；开发置 false）。</summary>
    public bool IsProduction { get; set; } = true;

    /// <summary>交互日志保留天数（0/负数 = 不清理；阶段一查询面够用，清理任务归后续迭代）。</summary>
    public int InteractionLogRetentionDays { get; set; }
}