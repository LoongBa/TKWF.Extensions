# 健康自检报告目录

> 本目录存放 `build/build-ext-healthcheck.ps1` 生成的巡检报告（机制见 [`../扩展模块健康自检与优化机制.md`](../扩展模块健康自检与优化机制.md)）。

| 文件 | 说明 |
|------|------|
| `健康自检报告-YYYY-MM-DD.md` | 每次全量执行一份（同日重跑覆盖）；含摘要、检查项结果、明细、整改清单 |
| `健康自检报告-YYYY-MM-DD-scoped-{Ext}.md` | 范围化运行（`-Scope`）单独成文，不覆盖全量报告 |
| `healthcheck-history.jsonl` | 趋势台账（每次运行一行；闭环率/误报率数据源） |
| `healthcheck-latest.json` | 最近一次机器可读结果（已 gitignore） |

运行：

```powershell
pwsh build/build-ext-healthcheck.ps1            # L1 标准
pwsh build/build-ext-healthcheck.ps1 -Mode L0    # 快检
pwsh build/build-ext-healthcheck.ps1 -UpdateBaseline  # 修复后收紧存量债务基线
```
