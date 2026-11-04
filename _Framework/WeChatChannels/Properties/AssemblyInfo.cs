using TKW.Framework.CodeGeneration;

// v0.1.0（2026-11-05 拆分下沉）：对内端点路由前缀声明——框架注册表按此 + TKWF:Api:RestBasePath 拼接组路径
// （DefaultPrefix "wechat-channels" 不含 basePath：组 = /api/wechat-channels，D5 定案新段——
//  不留旧 /wechat-ops/channels 兼容尾巴；[ApiExpose] RestRoute 相对路径——前缀归 ExtensionModule 单一真源）
[assembly: ExtensionModule("WeChatChannels", "wechat-channels")]