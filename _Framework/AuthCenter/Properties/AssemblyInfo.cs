using TKW.Framework.CodeGeneration;

// B1-c/F8（2026-10-24）：对内端点路由前缀声明——框架注册表按此 + TKWF:Api:RestBasePath 拼接组路径
// （DefaultPrefix "auth" 不含 basePath：组 = /api/auth，与旧 AuthCenterEndpointOptions.RoutePrefix 默认一致，消费方零迁移）
[assembly: ExtensionModule("AuthCenter", "auth")]