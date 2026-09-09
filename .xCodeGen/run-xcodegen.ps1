# xCodeGen 统一生成脚本（扩展仓库）
#
# 设计（D1 集中化，用户裁定 2026-09-07）：
# - 二进制/模板：集中在 %TKWFDeployPath%\xCodeGen\（环境变量指向框架部署根，各项目零复制）
# - JSON 配置：集中在 .xCodeGen\extensions\{扩展名}.xCodeGen.json（本仓库根共享目录）
# - 用法：pwsh .\.xCodeGen\run-xcodegen.ps1            # 生成全部扩展
#          pwsh .\.xCodeGen\run-xcodegen.ps1 -Ext permissions   # 生成单个扩展
# - 前置：主框架 xCodeGen.Cli 已发布到 %TKWFDeployPath%\xCodeGen\
#         （dotnet build _xCodeGen\xCodeGen.Cli -c Release 触发 _DeployToXCodeGen）
param(
    [string]$Ext = "",          # 扩展名（不带 .xCodeGen.json 后缀）；空 = 全部
    [switch]$Force              # 忽略 EnableSkipUnchanged 强制全量
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot   # 扩展仓库根
$configDir = Join-Path $PSScriptRoot 'extensions'
$frameworkPath = [Environment]::GetEnvironmentVariable('TKWFDeployPath', 'Machine')
if ([string]::IsNullOrWhiteSpace($frameworkPath)) {
    $frameworkPath = [Environment]::GetEnvironmentVariable('TKWFDeployPath', 'User')
}
if ([string]::IsNullOrWhiteSpace($frameworkPath)) {
    throw "环境变量 TKWFDeployPath 未设置（Machine/User）——无法定位 xCodeGen 二进制/模板"
}

$cliExe = Join-Path $frameworkPath 'xCodeGen\xCodeGen.Cli.exe'
if (-not (Test-Path $cliExe)) {
    throw "xCodeGen 二进制不存在：$cliExe`n请先构建主框架：dotnet build `"$frameworkPath\_xCodeGen\xCodeGen.Cli\xCodeGen.Cli.csproj`" -c Release"
}

$configs = if ($Ext) {
    $p = Join-Path $configDir "$Ext.xCodeGen.json"
    if (-not (Test-Path $p)) { throw "扩展配置不存在：$p" }
    @($p)
} else {
    Get-ChildItem $configDir -Filter '*.xCodeGen.json' | ForEach-Object { $_.FullName }
}

Write-Host "xCodeGen 二进制: $cliExe"
Write-Host "配置目录: $configDir"
Write-Host "待生成: $($configs.Count) 个扩展"
Write-Host "----------------------------------------"

foreach ($cfg in $configs) {
    Write-Host "`n=== 生成 $(Split-Path $cfg -Leaf) ==="
    if ($Force) {
        # 强制全量：直接调用 CLI（xCodeGen 无 --force 参数时，临时改配置 EnableSkipUnchanged）
        & $cliExe gen -j $cfg
    } else {
        & $cliExe gen -j $cfg
    }
    if ($LASTEXITCODE -ne 0) { throw "生成失败：$cfg" }
}

Write-Host "`n✅ 全部完成"
