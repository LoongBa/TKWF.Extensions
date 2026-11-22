# xCodeGen 扩展仓库入口（薄委托——实现逻辑单一真源：框架 xCodeGen\scripts\run-xcodegen.ps1）
#
# 单源纪律（ADR32 决策 6 / v4.10.143 S6）：生成实现只在框架单源脚本内，
# 本文件仅把扩展仓约定（.xCodeGen\extensions 配置目录 + -Ext 单扩展）翻译为单源脚本参数，
# 不复制生成逻辑。`-Force` 透传单源脚本 → CLI `--force`（真正强制全量，忽略 EnableSkipUnchanged）。
#
# 用法：
#   pwsh .\.xCodeGen\run-xcodegen.ps1                  # 生成全部扩展（增量）
#   pwsh .\.xCodeGen\run-xcodegen.ps1 -Ext permissions  # 生成单个扩展
#   pwsh .\.xCodeGen\run-xcodegen.ps1 -Force            # 强制全量重新生成
param(
    [string]$Ext = "",
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# 定位框架单源脚本（部署根；Machine/User/Process 环境变量兜底）
$deployPath = [Environment]::GetEnvironmentVariable('TKWFDeployPath', 'Machine')
if ([string]::IsNullOrWhiteSpace($deployPath)) {
    $deployPath = [Environment]::GetEnvironmentVariable('TKWFDeployPath', 'User')
}
if ([string]::IsNullOrWhiteSpace($deployPath)) {
    $deployPath = $env:TKWFDeployPath
}
if ([string]::IsNullOrWhiteSpace($deployPath)) {
    throw "环境变量 TKWFDeployPath 未设置（Machine/User/Process）——无法定位框架单源 run-xcodegen.ps1"
}

$singleSource = Join-Path $deployPath 'xCodeGen\scripts\run-xcodegen.ps1'
if (-not (Test-Path -LiteralPath $singleSource)) {
    throw "框架单源脚本不存在：$singleSource（请先执行框架全量部署 build-deployment.ps1）"
}

$configDir = Join-Path $PSScriptRoot 'extensions'

if ($Ext) {
    $configFile = Join-Path $configDir "$Ext.xCodeGen.json"
    if (-not (Test-Path -LiteralPath $configFile)) {
        throw "扩展配置不存在：$configFile"
    }
    & $singleSource -Config $configFile -Force:$Force
} else {
    & $singleSource -ConfigDir $configDir -Force:$Force
}
