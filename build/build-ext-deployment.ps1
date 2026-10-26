<#
.SYNOPSIS
  扩展仓库（_TKWF.Extensions）全量部署脚本——部署根（TKWFDeployPath）build\refs\ 一致性收口。

.DESCRIPTION
  对偶主框架 _TKWF\build\build-deployment.ps1（框架侧负责提取框架 DLL + xCodeGen + AC-Kit，
  本脚本只负责扩展侧产物：TKWF.Ext.* 与 TKWF.Federation.* 前缀 DLL——与框架 DLL 混驻同一
  %TKWFDeployPath%\build\refs\，消费方 DLL 模式经 $(TKWFDeployPath)build\refs\ 统一引用）。

  扩展 DLL 日常由各项目 _PushToRefs（Directory.Build.props）构建后自动推送，无需脚本介入。
  本脚本解决 _PushToRefs 覆盖不到的三类收口问题：
    1. 残留：项目已删除/重命名 → refs 中孤儿 DLL 永不被覆盖（框架侧 -Cleanup 同型问题，扩展侧无解）
    2. 一致性：多次零散构建后 refs 混入不同批次（时间戳聚类报告，供人眼判断）
    3. 首装/初始化：refs 为空时（新机器/新部署包）需要一次性初始化

  幂等：不破坏框架 DLL（TKWF.* 非 Ext/Federation 前缀一概不碰）。
  默认不自动构建（-Cleanup 触发构建时自动带出）。

.PARAMETER Cleanup
  清空部署根 refs 中全部扩展前缀 DLL → 重新构建 slnx（_PushToRefs 推送全量）→ 孤儿清理。
  推荐在扩展发布后/定期一致性收口使用。与 -SkipBuild 互斥。

.PARAMETER Ext
  单扩展快速模式：仅构建指定扩展项目（-c Release）触发 _PushToRefs 推送，不清理不校验。
  与 -Cleanup 互斥（单扩展无"清理全量"语义）。支持逗号分隔多扩展。

.PARAMETER SkipBuild
  跳过构建：仅执行孤儿清理 + 校验/报告（refs 已最新时用，默认 false）。
  不跳过构建时默认执行 dotnet build TKWF.Extensions.slnx -c Release。

.PARAMETER Config
  构建配置。默认 Release。注：DLL 模式 HintPath 不依赖配置，但 _PushToRefs 推送 Debug 产物
  会与 Release 混批，故仅 Debug 联调场景使用。

.PARAMETER NoCleanOrphans
  默认清理孤儿；本开关跳过孤儿删除（仅报告）。配合 CI/只读场景。

.PARAMETER Zip
  分发模式：完成后将部署根压缩为 TKWF.Extensions-<版本>.zip（置于部署根父目录）。
  仅打包扩展前缀 DLL 及轻量说明（不打包整个部署根——框架侧 -Zip 才打包全量）。

.EXAMPLE
  .\build\build-ext-deployment.ps1
  # 构建 slnx Release → _PushToRefs 推送 → 孤儿清理 → 报告批次聚类

.EXAMPLE
  .\build\build-ext-deployment.ps1 -Cleanup
  # 清空 refs 扩展 DLL → 重建 slnx → 全量推送 → 孤儿清理（一致性收口，对偶框架 -Cleanup）

.EXAMPLE
  .\build\build-ext-deployment.ps1 -Ext WeChatOperations
  # 单扩展快速重推（连带 ProjectReference 依赖链构建）

.EXAMPLE
  .\build\build-ext-deployment.ps1 -SkipBuild -NoCleanOrphans
  # 只读巡检：构建跳过 + 孤儿只报不删

.EXAMPLE
  .\build\build-ext-deployment.ps1 -Cleanup -Zip
  # 一致性收口 + 分发压缩
#>

param(
  [switch]$Cleanup,
  [string]$Ext = "",
  [switch]$SkipBuild,
  [string]$Config = "Release",
  [switch]$NoCleanOrphans,
  [switch]$Zip
)

$ErrorActionPreference = 'Stop'

# ── 参数互斥校验（对齐框架脚本风格）─────────────────────────
if ($Cleanup -and $SkipBuild) {
  Write-Host "[ERR] -Cleanup 与 -SkipBuild 互斥：清空 refs 后必须重建" -ForegroundColor Red
  exit 1
}
if ($Cleanup -and $Ext) {
  Write-Host "[ERR] -Cleanup 与 -Ext 互斥：单扩展模式不做全量清理" -ForegroundColor Red
  exit 1
}

# ── 路径定位 ──────────────────────────────────────────────────
$ScriptDir         = Split-Path -Parent $MyInvocation.MyCommand.Path   # build\
$ExtRepoRoot       = Split-Path $ScriptDir -Parent                     # _TKWF.Extensions
$SlnPath           = Join-Path $ExtRepoRoot "TKWF.Extensions.slnx"

# 部署根：环境变量 TKWFDeployPath 优先（对齐 Directory.Build.props 语义），fallback 本仓库 build\
$DeployRoot = if ($env:TKWFDeployPath) { $env:TKWFDeployPath.TrimEnd('\') } else { Join-Path $ExtRepoRoot "build" }
$RefsDir = Join-Path $DeployRoot "build\refs"

# 本仓库源码树 refs（_PushToRefs 双写副本，初始化兜底源）
$RepoRefsDir = Join-Path $ExtRepoRoot "build\refs"

if (-not (Test-Path $RefsDir)) {
  Write-Host "[ERR] 部署根 refs 不存在：$RefsDir`n      请先运行框架侧 ..\_TKWF\build\build-deployment.ps1 生成部署包（或设置 TKWFDeployPath）" -ForegroundColor Red
  exit 1
}

# ── 文件锁重试（对齐框架 V4.10.68 加固：DLL 可能被 VBCSCompiler/MSBuild 节点驻留锁定）──
function Remove-FilesWithRetry([string[]]$Files, [string]$Label) {
  for ($attempt = 1; $attempt -le 3; $attempt++) {
    try {
      foreach ($f in $Files) { Remove-Item -LiteralPath $f -Force -ErrorAction Stop }
      return
    } catch {
      if ($attempt -lt 3) {
        Write-Host "      → ${Label} 被占用（尝试 $attempt/3），2s 后重试..." -ForegroundColor Yellow
        Start-Sleep -Seconds 2
      }
    }
  }
  Write-Host "[ERR] ${Label} 清理失败：文件被持续占用（3 次重试后仍失败）" -ForegroundColor Red
  Write-Host "      可能持有者：VBCSCompiler / MSBuild 节点——请先 'dotnet build-server shutdown' 后重试。" -ForegroundColor Yellow
  exit 1
}

# ── 孤儿判定：refs 扩展 DLL vs 全仓库 csproj 合法产出名 ─────────
# 以 refs 现有扩展前缀 DLL 为基准，比对仓库内所有 csproj（_Framework + _Tests）的
# BaseName（= 程序集名，无 AssemblyName 覆盖）。项目已删除/重命名 → csproj 不存在 → 判孤儿。
# 排除 Federation.Template（{Namespace}/{Platform} 占位符，故意不可编译、不在 slnx）。
function Get-OrphanDlls {
  $legal = @{}
  Get-ChildItem (Join-Path $ExtRepoRoot "_Framework"), (Join-Path $ExtRepoRoot "_Tests") -Filter *.csproj -Recurse -Depth 1 -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch 'Federation\.Template' } |
    ForEach-Object { $legal[$_.BaseName] = $true }
  # ⚠️ 变量命名纪律：函数内 foreach 循环变量不得与全局作用域变量重名
  #    （PowerShell 函数不隔离变量——$d 曾与全局 $d 冲突致孤儿误报空条目，已修 2026-10-26）
  $refExt = @(Get-ChildItem $RefsDir -Filter "TKWF.Ext.*.dll" -ErrorAction SilentlyContinue) +
            @(Get-ChildItem $RefsDir -Filter "TKWF.Federation.*.dll" -ErrorAction SilentlyContinue)
  $orphans = @()
  foreach ($refDll in $refExt) {
    if (-not $legal.ContainsKey($refDll.BaseName)) { $orphans += $refDll.FullName }
  }
  return ,$orphans
}

# ── -Cleanup：清空 refs 扩展 DLL → 重建（对偶框架 -Cleanup）──
if ($Cleanup) {
  Write-Host "[pre] 清空 refs 扩展 DLL（保留框架 DLL）..." -ForegroundColor Yellow
  $toRemove = @(Get-ChildItem $RefsDir -Filter "TKWF.Ext.*.dll" -ErrorAction SilentlyContinue) +
              @(Get-ChildItem $RefsDir -Filter "TKWF.Federation.*.dll" -ErrorAction SilentlyContinue) +
              @(Get-ChildItem $RefsDir -Filter "TKWF.Ext.*.xml" -ErrorAction SilentlyContinue) +
              @(Get-ChildItem $RefsDir -Filter "TKWF.Federation.*.xml" -ErrorAction SilentlyContinue)
  if ($toRemove.Count -gt 0) {
    Remove-FilesWithRetry ($toRemove | ForEach-Object FullName) "refs 扩展 DLL（-Cleanup）"
    Write-Host "      → 已清空 $($toRemove.Count) 个文件" -ForegroundColor Green
  } else {
    Write-Host "      → refs 无扩展 DLL，跳过" -ForegroundColor Gray
  }
}

# ── 构建（默认执行，确保 refs 版本一致）──────────────────────
if (-not $SkipBuild) {
  if ($Ext) {
    # 单扩展模式：构建指定扩展项目（ProjectReference 依赖链自动带上；测试项目不构建）
    foreach ($name in ($Ext -split ',') | ForEach-Object { $_.Trim() }) {
      $proj = Get-ChildItem (Join-Path $ExtRepoRoot "_Framework") -Filter "*.csproj" -Recurse -Depth 1 |
        Where-Object { $_.BaseName -eq $name -or $_.BaseName -eq "TKWF.Ext.$name" -or $_.BaseName -eq "TKWF.Federation.$name" } |
        Select-Object -First 1
      if (-not $proj) {
        Write-Host "[ERR] 未找到扩展项目：$name（_Framework 下无匹配 csproj）" -ForegroundColor Red
        exit 1
      }
      Write-Host "[pre] 构建扩展（$($proj.BaseName)，$Config）..." -ForegroundColor Cyan
      dotnet build $proj.FullName -c $Config --nologo -v q
      if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERR] 扩展构建失败（$($proj.BaseName)），退出码 $LASTEXITCODE" -ForegroundColor Red
        exit 1
      }
    }
  } else {
    Write-Host "[pre] 构建 TKWF.Extensions（$Config，slnx）..." -ForegroundColor Cyan
    dotnet build $SlnPath -c $Config --nologo -v q
    if ($LASTEXITCODE -ne 0) {
      Write-Host "[ERR] 扩展仓库构建失败，退出码 $LASTEXITCODE" -ForegroundColor Red
      exit 1
    }
  }

  # 构建后验证：refs 扩展 DLL 非空（单扩展模式可能只推 1 个，仅全量/清理模式做空校验）
  if (-not $Ext) {
    $refExtCount = @(Get-ChildItem $RefsDir -Filter "TKWF.Ext.*.dll" -ErrorAction SilentlyContinue).Count +
                   @(Get-ChildItem $RefsDir -Filter "TKWF.Federation.*.dll" -ErrorAction SilentlyContinue).Count
    if ($refExtCount -eq 0) {
      Write-Host "[ERR] 构建完成但 refs 无扩展 DLL——_PushToRefs 未触发或推送目标异常" -ForegroundColor Red
      exit 1
    }
    Write-Host "      → 构建完成，refs 扩展 DLL 已更新（$refExtCount 个）" -ForegroundColor Green
  }
}

# ── 首装兜底：refs 扩展 DLL 为空 → 从本仓库源码树 refs 初始化 ──
# （对齐框架脚本兜底语义：部署包 refs 为空时一次性初始化，后续由 _PushToRefs 推送维护）
$refExtCount0 = @(Get-ChildItem $RefsDir -Filter "TKWF.Ext.*.dll" -ErrorAction SilentlyContinue).Count +
                @(Get-ChildItem $RefsDir -Filter "TKWF.Federation.*.dll" -ErrorAction SilentlyContinue).Count
if ($refExtCount0 -eq 0 -and (Test-Path $RepoRefsDir)) {
  $srcExt = @(Get-ChildItem $RepoRefsDir -Filter "TKWF.Ext.*.dll" -ErrorAction SilentlyContinue) +
            @(Get-ChildItem $RepoRefsDir -Filter "TKWF.Federation.*.dll" -ErrorAction SilentlyContinue)
  if ($srcExt.Count -gt 0) {
    Write-Host "[INFO] refs 扩展 DLL 为空——从本仓库源码树 refs 初始化复制（$($srcExt.Count) 个）..." -ForegroundColor Yellow
    Copy-Item $srcExt.FullName $RefsDir -Force
    Write-Host "      → 初始化完成" -ForegroundColor Green
  }
}

# ── 孤儿清理（-Ext 单扩展模式跳过）───────────────────────────
if (-not $Ext) {
  # ⚠️ Get-OrphanDlls 用 return ,$orphans 输出"单对象=数组"，调用处不可再包 @()（嵌套数组）
  #    （2026-10-26 实证：@() 包装致 $_ 为内层数组、GetFileName 返回空 → 空孤儿条目误报）
  $orphans = Get-OrphanDlls
  if ($orphans.Count -gt 0) {
    Write-Host "=== 孤儿清理：refs 中存在仓库已无对应项目的扩展 DLL ===" -ForegroundColor Cyan
    $orphans | ForEach-Object { Write-Host "  [孤儿] $([System.IO.Path]::GetFileName($_))" -ForegroundColor Yellow }
    if ($NoCleanOrphans) {
      Write-Host "      → -NoCleanOrphans：仅报告不删除" -ForegroundColor Yellow
    } else {
      Write-Host "      → 删除 $($orphans.Count) 个孤儿 DLL..." -ForegroundColor Yellow
      Remove-FilesWithRetry $orphans "孤儿 DLL"
      Write-Host "      → 孤儿已清理" -ForegroundColor Green
    }
  } else {
    Write-Host "[OK] 孤儿检查通过：refs 扩展 DLL 全部有源" -ForegroundColor Green
  }
}

# ── 一致性报告（时间戳批次聚类，供人眼判断混合批次）──────────
$allExt = @(Get-ChildItem $RefsDir -Filter "TKWF.Ext.*.dll" -ErrorAction SilentlyContinue) +
          @(Get-ChildItem $RefsDir -Filter "TKWF.Federation.*.dll" -ErrorAction SilentlyContinue)
Write-Host "=== refs 扩展 DLL 批次报告（$($allExt.Count) 个）===" -ForegroundColor Cyan
$allExt | Group-Object { $_.LastWriteTime.ToString("yyyy-MM-dd HH:mm") } |
  Sort-Object Name |
  ForEach-Object { Write-Host ("  {0}  →  {1} 个" -f $_.Name, $_.Count) }

# ── -Zip 分发（仅扩展前缀 + 轻量说明，不打包整个部署根）──────
if ($Zip) {
  if ($Ext) {
    Write-Host "[WARN] -Zip 与 -Ext 同时使用：单扩展模式不打包（Zip 面向全量分发）" -ForegroundColor Yellow
  } else {
    $ver = git -C $ExtRepoRoot describe --tags --match '*/v*' --abbrev=0 2>$null
    if (-not $ver) { $ver = git -C $ExtRepoRoot describe --tags --abbrev=0 2>$null }
    if (-not $ver) { $ver = "dev" }
    $zipName = "TKWF.Extensions-$($ver.TrimStart('v'))-refs.zip"
    $zipPath = Join-Path (Split-Path $DeployRoot -Parent) $zipName
    Write-Host "[6/4] 分发压缩（仅扩展 DLL）..." -ForegroundColor Cyan
    Remove-Item $zipPath -ErrorAction Ignore
    # 压缩：扩展前缀 DLL + 简单说明（zip 内平铺，避免目录结构噪音）
    $tmpDir = Join-Path $env:TEMP ("ext-deploy-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tmpDir -Force | Out-Null
    try {
      Copy-Item ($allExt | ForEach-Object FullName) $tmpDir -Force
      Set-Content -Path (Join-Path $tmpDir "README.txt") -Value (
        "TKWF.Extensions refs 部署包（仅扩展前缀 DLL，框架 DLL 由 ..\_TKWF\build\build-deployment.ps1 提供）`r`n" +
        "生成时间: $(Get-Date -Format 'yyyy-MM-dd HH:mm')`r`n" +
        "来源版本: $ver`r`n" +
        "DLL 数量: $($allExt.Count)") -Encoding UTF8
      Compress-Archive -Path (Join-Path $tmpDir "*") -DestinationPath $zipPath -Force
    } finally {
      Remove-Item $tmpDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    Write-Host "      → $zipPath" -ForegroundColor Green
  }
}

Write-Host ""
Write-Host "=== 扩展部署收口完成 ✅ ===" -ForegroundColor Green
Write-Host "部署根: $DeployRoot"
Write-Host "提示: 扩展 DLL 与框架 DLL 混驻 $(Split-Path $RefsDir -Leaf)，消费方 DLL 模式经 TKWFDeployPath 统一引用。"
