# gen-versions.ps1 — 生成机器可读发布清单 docs/versions.json（发布事实源 = git tag）
#
# 用途：框架组/消费方直接消费"包 → 最新 tag → 版本 → 创建日期"清单，根治 README 手工回填滞后。
# 输出：docs/versions.json（git-tracked，随发布同步提交）
# 用法：pwsh build/gen-versions.ps1 [仓库根目录，默认当前目录]
#
# 边界：
# - 只读 git tag，不修改任何 tag
# - tag 格式 {Package}/vX.Y.Z（包名前缀 + v + SemVer）；无前缀 v tag（公共内容）不纳入 packages，单独列公共基线
# - 契约包/平台库各自独立条目（Federation.Abstractions、Federation.WeChat 等与主包并列）

param(
    [string]$RepoRoot = (Get-Location).Path
)

$ErrorActionPreference = 'Stop'

# 解析仓库根（脚本可能在 build/ 下被调用）
$repo = Resolve-Path $RepoRoot
if (-not (Test-Path (Join-Path $repo 'TKWF.Extensions.slnx'))) {
    # 尝试从 build/ 上溯
    $parent = Split-Path $repo -Parent
    if (Test-Path (Join-Path $parent 'TKWF.Extensions.slnx')) { $repo = $parent }
    else { throw "无法定位 TKWF.Extensions.slnx（给定: $RepoRoot）" }
}

Push-Location $repo
try {
    $tags = git tag --list
    if (-not $tags) { throw "无 tag（git tag --list 为空）——仓库尚未打 tag？" }

    $packages = [ordered]@{}
    $commonTags = [System.Collections.Generic.List[string]]::new()
    foreach ($t in $tags) {
        if ($t -match '^([^/]+)/(v?\d+\.\d+\.\d+)$') {
            $p = $matches[1]
            $ver = $matches[2].TrimStart('v')
            if (-not $packages.Contains($p)) {
                # 取创建日期（annotated tag 用 creatordate）
                $date = git for-each-ref "refs/tags/$t" --format='%(creatordate:short)'
                $packages[$p] = [ordered]@{
                    version   = $ver
                    tag       = $t
                    created   = $date.Trim()
                }
            } else {
                # 已有条目：仅当版本更高才替换（tag 列表无序）
                $existing = [version]$packages[$p].version
                if ([version]$ver -gt $existing) {
                    $date = git for-each-ref "refs/tags/$t" --format='%(creatordate:short)'
                    $packages[$p] = [ordered]@{
                        version   = $ver
                        tag       = $t
                        created   = $date.Trim()
                    }
                }
            }
        }
        elseif ($t -match '^v\d+\.\d+\.\d+$') {
            $commonTags.Add($t)
        }
    }

    # 按包名排序
    $sorted = [ordered]@{}
    foreach ($k in ($packages.Keys | Sort-Object)) { $sorted[$k] = $packages[$k] }

    $result = [ordered]@{
        generatedAt  = (Get-Date -Format 'yyyy-MM-dd')
        repo         = 'TKWF.Extensions'
        note         = '发布事实源 = git tag（{Package}/vX.Y.Z）；README 版本一览表为展示层，打 tag 后须同步。生成：build/gen-versions.ps1'
        commonTags   = $commonTags | Sort-Object
        packages     = $sorted
    }

    $out = Join-Path $repo 'docs/versions.json'
    $result | ConvertTo-Json -Depth 5 | Set-Content -Path $out -Encoding UTF8
    Write-Host "已生成: $out"
    Write-Host "包数: $($sorted.Count)；公共 v tag 数: $($commonTags.Count)"
}
finally {
    Pop-Location
}
