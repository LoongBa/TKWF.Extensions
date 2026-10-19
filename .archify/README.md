# .archify — _TKWF.Extensions 架构图产物与维护说明

本目录存放用 [archify] 生成的自包含 HTML 架构图。`index.html` 是唯一入口，可直接双击打开。

> 注意：`.gitignore` **未忽略** `.archify`，产物可提交。若不想提交，请另行加 ignore 规则。

本目录的图**证据边界只覆盖本扩展仓**（`_TKWF.Extensions` / TKWF.Ext.*）。
主框架（`_TKWF` / TKW.Framework）自己的架构图在主仓的 `.archify/`，由主框架维护。

[archify]: https://github.com/search?q=archify

---

## 一、先看入口

浏览器打开 `.archify/index.html`，即可跳转到任意一张图。每张图都是单文件 HTML，
自带主题切换（T）/ 风格切换（S）/ 演示模式（F）/ 导出（E），无需联网。

---

## 二、浏览器环境（重要）

**本机只装了 Edge，没装 Chrome。** archify 的 `browser-check` 门禁默认探测
Chrome / Chromium，找不到就会以 `spawn UNKNOWN` 失败——这曾让所有图的
`browser-check` 误判为"环境失败"。

解法：archify 支持环境变量 `ARCHIFY_CHROME` 显式指定浏览器可执行路径，
指向 Edge 即可让 `browser-check` 真正跑通并给出真实的视觉回归结果。

```powershell
# PowerShell —— 每个新终端都要先设
$env:ARCHIFY_CHROME = "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
```

设好后单独复检某张图：

```powershell
node "C:\Users\coffe\.config\opencode\skills\archify\bin\archify.mjs" `
  browser-check ".archify/diagrams/A2-ext-map/a2-ext-map.html" --json
```

Edge 的 headless 模式也已验证可用（`--headless=new`），可用于注入脚本做 DOM/溢出探测。

---

## 三、门禁（gates）含义

finalize 产出的 `*.finalize-summary.json` 里有四个门禁，`ok:true` 需四者全 pass：

| 门禁 | 含义 |
|------|------|
| `validate` | candidate.json 符合 schema |
| `deliver` | HTML 交付产物已生成、自包含 |
| `check` | 静态检查（证据引用、结构） |
| `browser-check` | 真实浏览器渲染回归（视口溢出、可读性、图例等） |

`browser-check` 只有设了 `ARCHIFY_CHROME` 才有意义。在此之前它一律 fail，
**不能据此判断图有问题**。

---

## 四、当前图

| ID | 标题 | 类型 | 目录 | HTML |
|----|------|------|------|------|
| A2 | TKWF 扩展仓全景图 | architecture | `diagrams/A2-ext-map/` | `a2-ext-map.html` |

**证据边界**：本仓的图只引用扩展仓 `_TKWF.Extensions` 的 revision。
修改图内容时不要引入主框架仓作为证据来源。

---

## 五、产物目录结构

每个图目录里的文件：

| 文件 | 说明 |
|------|------|
| `candidate.json` | **图的源**。schema + 组件/边界/连接/卡片，是唯一需要手改/重生成的输入 |
| `*.html` | finalize 生成的自包含交付产物（不建议直接改，改了也会被重新 finalize 覆盖） |
| `*.finalize-summary.json` | 四门禁结果，判断图是否健康看这里 |
| `*.finalize.json` | finalize 详细日志 |
| `*.delivery.json` | 交付信息（含 artifact sha256） |
| `*.browser-check.json` | browser-check 详细视觉回归结果 |

---

## 六、重生成 / 修改一张图

不要手改 `.html`，改 `candidate.json` 后重新 finalize：

```powershell
$env:ARCHIFY_CHROME = "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
$cli = "C:\Users\coffe\.config\opencode\skills\archify\bin\archify.mjs"

node $cli finalize <type> ".archify/diagrams/A2-ext-map/candidate.json" `
  ".archify/diagrams/A2-ext-map/a2-ext-map.html" `
  --repo-root "F:\LoongBa_Git\_TKWF.Extensions" --quality showcase --json
```

`<type>` 为该图的 `diagram_type`（读自 candidate.json）。

**`--repo-root` 必须指向本扩展仓**（`F:\LoongBa_Git\_TKWF.Extensions`），且该
checkout 的 origin 与 HEAD 需与 candidate 的 `meta.repository`/`revision` 一致，
否则 `validate` 门禁报 `repository-evidence/origin-mismatch`。当前 revision 为
`3dd778f`。

改完再复检：

```powershell
node $cli browser-check ".archify/diagrams/A2-ext-map/a2-ext-map.html" --json
```

---

## 七、已知问题

- **（已修复）A2 `viewer/viewport-overflow`**：根因不在组件坐标（最大 X 仅 1190），
  也不在 SVG viewBox（`0 0 1524 590`，SVG 会按 `max-width:100%` 缩放）。真正原因在
  viewer 底部卡片栏：第 4 张卡「领域成员一览」里一串 54 字符的斜杠串
  `WeChat/QQ/Alipay/Oidc/Google/Microsoft/DingTalk/WeCom` 没有断行点（CJK 任意处可断，
  但拉丁/斜杠串内部浏览器不换行），单个 `<li>` 拒绝换行把卡片栏撑宽 49px 越过视口。
  修复：分隔符 `/` → ` · `（与图中既有分隔风格一致），内容一字未删。现四门禁全 pass。

---

## 八、快速排查

| 症状 | 原因 / 处理 |
|------|------------|
| `browser-check` 报 `spawn UNKNOWN` | 未设 `ARCHIFY_CHROME`，见第二节 |
| JSON.parse 报 `Unexpected token '﻿'` | 输出带 BOM，解析前先 `.replace(/^\uFEFF/, '')` |
| 内联 `node -e "..."` 语法错 | PowerShell 吞引号，改写成 `.cjs` 脚本文件再 `node xxx.cjs` |
| `check` 报 `Unknown check option` | 门禁命令是 `browser-check <html>`，不是 `check` |
| 临时目录 `require is not defined` | 该目录是 ESM 作用域，脚本用 `.cjs` 后缀 |
| `validate` 报 `origin-mismatch` | `--repo-root` 与 candidate 的 `meta.repository` 不符，见第六节 |
