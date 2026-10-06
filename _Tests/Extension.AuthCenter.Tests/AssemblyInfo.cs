// V0.6.0 归层迭代（2026-10-06）曾因 DevRsaKeyCache 进程内静态缓存（V0.5.3）+ 测试宿主 dev 临时密钥全局共享
// 禁用测试并行（DisableTestParallelization）——E4 密钥管理抽象（V0.7.0）后 DevRsaKeyCache 已删、
// 改经 DevKeyCache DI 单例（非静态，每用例独立实例）——并行冲突根因消失，恢复默认并行（无此文件属性）。
// 文件保留以记录该决定的历史注记。
