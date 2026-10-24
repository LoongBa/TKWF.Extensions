using TKW.Framework.CodeGeneration;

// B1-c/F8（2026-10-24）：对外连接层端点路由前缀声明——框架注册表按此 + TKWF:Api:RestBasePath 拼接组路径
// （FederationWebExtension.DeclareProtocolEndpoints 的模板相对该组前缀；DefaultPrefix 不含首尾斜杠）
[assembly: ExtensionModule("Federation", "feberation")]