using TKW.Framework.CodeGeneration;

// v0.6.0 (B1-c/F8 端点契约化)：对内端点路由前缀声明——框架注册表按此 + TKWF:Api:RestBasePath 拼接组路径
// （DefaultPrefix "wechat-ops" 不含 basePath：组 = /api/wechat-ops，与旧 [ApiExpose] RestRoute 前缀一致；
//  25 个 [ApiExpose] 已去 /wechat-ops 段——前缀归 ExtensionModule 单一真源，防双段 /api/wechat-ops/wechat-ops/*）
[assembly: ExtensionModule("WeChatOperations", "wechat-ops")]
