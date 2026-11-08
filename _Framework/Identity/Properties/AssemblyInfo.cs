using TKW.Framework.CodeGeneration;

// 程序集级 ExtensionModule 声明（模块前缀机制，v4.10.85+ F8 语义）——消除 TKWF_SG1b_EXTMODULE001 警告
// （非 Web 扩展无 WebExtension/MapGenerated，运行时前缀不生效；框架批次后此诊断转 Error，须提前补齐）
[assembly: ExtensionModule("Identity", "identity")]
