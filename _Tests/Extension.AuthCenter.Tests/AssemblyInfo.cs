using Xunit;

// V0.6.0 归层迭代（2026-10-06）——DevRsaKeyCache 进程内静态缓存（V0.5.3）+ 测试宿主 dev 临时密钥全局共享：
// 并行下 DevRsaKeyCacheTests.ResetForTests 清缓存会与并行用例中途 签发→验签 窗口冲突（既有 flaky——
// 归层前偶发，归层后用例量/调度时序变化更易暴露）。本项目用例量小（91），禁用并行换取确定性。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
