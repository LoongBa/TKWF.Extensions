<#
.SYNOPSIS
  TKWF.Extensions 仓库健康自检（只读巡检）——产出分级报告 + 棘轮基线。

.DESCRIPTION
  机制文档：docs/扩展模块健康自检与优化机制.md
  本脚本只读工作区（不修改源码/文档），除 -UpdateBaseline 外只写报告/摘要/趋势文件。
  退出码：0 通过；1 棘轮新增；2 存在 P0；3 基线操作被拒绝。

  模式：
    L0  快检（秒级）：项目注册/命名(A1)、文档存在(A2)、xCodeGen(A3)、链接(C1)、文档命名/ADR(C2)、路由(C4)、tag规范(E1)、目录卫生(G2)
    L1  标准（默认）：L0 + 生成物(A4)、红线(A5)、内容列(A6)、测试映射(A7)、依赖方向(B1)、表名(B2)、
        契约包(B3)、权限键(B4)、关键事实断言(C3)、版本一致(D1)、指南类型(D2)、配置键(D3)、CPM(E2)、refs(E3)、README覆盖(E4)、
        机密(F3)、退役(G1)、文档时效(G3)、机制时效(G4)
    L2  全量：L1 + 构建(F1) + 测试(F2)（需显式 -WithBuild / -WithTests，或 -Mode L2 自动开启）

.PARAMETER Mode
  L0 / L1 / L2，默认 L1。

.PARAMETER Scope
  只巡检指定扩展（如 AuthCenter）的 A/B 类检查；C/D/E/G 类仍全局执行（成本低）。支持逗号分隔。

.PARAMETER UpdateBaseline
  将当前存量债务（A5/A6/G2）写入 build/healthcheck-baseline.json（棘轮收紧）。修复后使用。
  仅允许全仓 L1/L2；若本轮有"新增存量"默认拒绝，需先修复或 -ForceBaseline。

.PARAMETER ForceBaseline
  显式吸收本轮新增存量（仅用于误报/历史等价迁移），会打印并写入基线。

.PARAMETER WithBuild
  执行编译门（F1：dotnet build slnx Release）。

.PARAMETER WithTests
  执行全量测试（dotnet test slnx --no-build；需先 -WithBuild 或已有 Release 产物）。

.PARAMETER NoWrite
  只输出到控制台，不写报告/摘要/趋势（基线仍会更新）。

.EXAMPLE
  pwsh build/build-ext-healthcheck.ps1
.EXAMPLE
  pwsh build/build-ext-healthcheck.ps1 -Mode L0
.EXAMPLE
  pwsh build/build-ext-healthcheck.ps1 -Scope "AuthCenter,WeChatOperations"
.EXAMPLE
  pwsh build/build-ext-healthcheck.ps1 -UpdateBaseline
#>
[CmdletBinding()]
param(
    [ValidateSet('L0', 'L1', 'L2')]
    [string]$Mode = 'L1',
    [string]$Scope = '',
    [switch]$UpdateBaseline,
    [switch]$ForceBaseline,
    [switch]$WithBuild,
    [switch]$WithTests,
    [switch]$NoWrite,
    [string]$OutDir = ''
)

$ErrorActionPreference = 'Stop'

# ── 路径与上下文 ────────────────────────────────────────────────
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Repo = Split-Path $ScriptDir -Parent
$FrameworkRoot = Join-Path $Repo '_Framework'
$TestsRoot = Join-Path $Repo '_Tests'
$DocsRoot = Join-Path $Repo 'docs'
$SlnxPath = Join-Path $Repo 'TKWF.Extensions.slnx'
$CpmPath = Join-Path $Repo 'Directory.Packages.props'
$RootReadme = Join-Path $Repo 'README.md'
$AgentsFile = Join-Path $Repo 'Agents_TKWF_Ext.md'
$DocsAgents = Join-Path $DocsRoot 'AGENTS.md'
$BaselinePath = Join-Path $ScriptDir 'healthcheck-baseline.json'
$InvariantsPath = Join-Path $ScriptDir 'healthcheck-doc-invariants.json'
if (-not $OutDir) { $OutDir = Join-Path $DocsRoot '健康自检' }

if ($Mode -eq 'L2') { $WithBuild = $true; $WithTests = $true }

$ScopeList = @()
if ($Scope) { $ScopeList = @($Scope -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }

# R1 防呆：基线必须由"全仓 L1+"生成；范围化/L0 运行会截断基线（Oracle 评审 R1）
if ($UpdateBaseline -and ($ScopeList.Count -gt 0 -or $Mode -eq 'L0')) {
    Write-Host '[ERR] -UpdateBaseline 仅允许在全仓 L1/L2 运行中使用（-Scope/L0 会截断基线）。' -ForegroundColor Red
    exit 3
}

function Test-InScope([string]$name) {
    if ($ScopeList.Count -eq 0) { return $true }
    foreach ($s in $ScopeList) { if ($name -eq $s -or $name -like "$s.*" -or $name -like "$s") { return $true } }
    return $false
}

function Read-Text([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return '' }
    return [System.IO.File]::ReadAllText($path)
}

function Get-RelPath([string]$path) {
    $full = [System.IO.Path]::GetFullPath($path)
    $base = [System.IO.Path]::GetFullPath($Repo).TrimEnd('\') + '\'
    if ($full.StartsWith($base, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $full.Substring($base.Length).Replace('\', '/')
    }
    return $full
}

function Get-CsFiles([string]$root) {
    Get-ChildItem -LiteralPath $root -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[/\\](obj|bin)[/\\]' }
}

function Get-MdFiles([string]$root, [int]$maxDepth = 99) {
    Get-ChildItem -LiteralPath $root -Recurse -Filter *.md -File -Depth $maxDepth -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[/\\](obj|bin|node_modules|\.git)[/\\]' }
}

# 实体"声明源"文件：项目根 *.cs 中带 [DomainGenerateCode] 的手写文件
# （本仓库约定：实体声明在 _Framework/{Ext}/{Entity}.cs；Entities/ 下是 xCodeGen 生成的 .g.cs/.biz.cs）
function Get-EntitySourceFiles($proj) {
    foreach ($f in (Get-CsFiles $proj.DirPath)) {
        if ($f.Name -like '*.g.cs' -or $f.Name -like '*.biz.cs') { continue }
        if ($f.FullName -match '[/\\](Entities|DataServices|DTOs|Conditions|Generated)[/\\]') { continue }
        $txt = Read-Text $f.FullName
        if ($txt -match '\[DomainGenerateCode') { $f }
    }
}

$script:Findings = [System.Collections.Generic.List[object]]::new()
function Add-Finding {
    param(
        [string]$Id, [string]$Severity, [string]$Title, [string]$Target,
        [string]$Detail = '', [string]$Hint = '', [switch]$Ratchet
    )
    $script:Findings.Add([pscustomobject]@{
            Id      = $Id
            Severity = $Severity
            Title   = $Title
            Target  = $Target
            Detail  = $Detail
            Hint    = $Hint
            Ratchet = [bool]$Ratchet
            IsNew   = $false
            Key     = "$Id|$Target"
        }) | Out-Null
}

$script:CheckCatalog = @(
    @{ Id = 'HK-A1'; Name = '项目注册与命名（slnx/csproj/MinVer/PackageId）' }
    @{ Id = 'HK-A2'; Name = 'README 与使用指南存在性' }
    @{ Id = 'HK-A3'; Name = 'xCodeGen 配置 1:1 与路径合法' }
    @{ Id = 'HK-A4'; Name = '生成物新鲜度（实体 → .g.cs）' }
    @{ Id = 'HK-A5'; Name = '数据访问红线（IFreeSql/IEntityDAC/Store，棘轮）' }
    @{ Id = 'HK-A6'; Name = '内容列 MaxLength(-1)（棘轮）' }
    @{ Id = 'HK-A7'; Name = '测试项目映射' }
    @{ Id = 'HK-B1'; Name = '跨扩展依赖方向（Abstractions/同族）' }
    @{ Id = 'HK-B2'; Name = '表/视图/索引前缀与撞名' }
    @{ Id = 'HK-B3'; Name = '契约包成对与合法性' }
    @{ Id = 'HK-B4'; Name = '权限键抽样（弱）' }
    @{ Id = 'HK-C1'; Name = 'Markdown 相对链接有效性' }
    @{ Id = 'HK-C2'; Name = '文档命名与 ADR 三问' }
    @{ Id = 'HK-C3'; Name = '关键事实断言（文档不变量）' }
    @{ Id = 'HK-C4'; Name = 'AGENTS 路由路径可解析' }
    @{ Id = 'HK-D1'; Name = '版本一致性（tag/versions.json/README）' }
    @{ Id = 'HK-D2'; Name = '指南中 API 类型存在性' }
    @{ Id = 'HK-D3'; Name = '指南配置键与代码一致' }
    @{ Id = 'HK-E1'; Name = 'tag 命名规范' }
    @{ Id = 'HK-E2'; Name = 'CPM/框架基线一致性' }
    @{ Id = 'HK-E3'; Name = '部署 refs 一致性（只读）' }
    @{ Id = 'HK-E4'; Name = '根 README 版本表覆盖' }
    @{ Id = 'HK-F1'; Name = '编译门（slnx 0 错误，L2/-WithBuild）' }
    @{ Id = 'HK-F2'; Name = '全量测试（L2/-WithTests）' }
    @{ Id = 'HK-F3'; Name = '硬编码机密样板' }
    @{ Id = 'HK-G1'; Name = '退役扩展残留' }
    @{ Id = 'HK-G2'; Name = '目录卫生（棘轮）' }
    @{ Id = 'HK-G3'; Name = '文档时效与体量' }
    @{ Id = 'HK-G4'; Name = '机制自身时效（是否按节奏执行）' }
)

# ── 基础数据 ────────────────────────────────────────────────────
$script:Projects = @(
    Get-ChildItem -LiteralPath $FrameworkRoot -Directory | ForEach-Object {
        $dir = $_
        $csproj = Get-ChildItem -LiteralPath $dir.FullName -Filter *.csproj -File -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($csproj) {
            [pscustomobject]@{
                Dir     = $dir.Name
                DirPath = $dir.FullName
                Csproj  = $csproj
                Name    = $csproj.BaseName
                Text    = (Read-Text $csproj.FullName)
            }
        }
    }
)
$script:BuildableProjects = @($script:Projects | Where-Object { $_.Dir -ne 'Federation.Template' })

$slnxText = Read-Text $SlnxPath
$script:SlnxProjects = @([regex]::Matches($slnxText, '<Project\s+Path="([^"]+)"') | ForEach-Object { $_.Groups[1].Value.Replace('\', '/').Replace('//', '/') })

$script:Tags = @(git -C $Repo tag --list)
$script:PackageTags = @{}
foreach ($t in $script:Tags) {
    if ($t -match '^([^/]+)/(v?\d+\.\d+\.\d+)$') {
        $pkg = $Matches[1]; $ver = $Matches[2].TrimStart('v')
        if (-not $script:PackageTags.ContainsKey($pkg) -or [version]$ver -gt [version]$script:PackageTags[$pkg].Version) {
            $script:PackageTags[$pkg] = [pscustomobject]@{ Version = $ver; Tag = $t }
        }
    }
}

# 代码中声明的类型名（供 D2）
$script:DeclaredTypes = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($p in $script:BuildableProjects) {
    foreach ($f in (Get-CsFiles $p.DirPath)) {
        $txt = Read-Text $f.FullName
        foreach ($m in [regex]::Matches($txt, '\b(?:class|interface|record|struct|enum)\s+([A-Za-z_]\w*)')) {
            [void]$script:DeclaredTypes.Add($m.Groups[1].Value)
        }
    }
}

# 指南/文档缓存（只读一次）
$script:GuideFiles = @(Get-MdFiles $DocsRoot | Where-Object { $_.Name -like '*使用指南*.md' })
$script:AllDocsMd = @(Get-MdFiles $DocsRoot)

# 框架类型名（供 D2 区分"指南引用框架 API"与"指南引用已改名/不存在的扩展类型"）——从部署根 refs 的 XML 文档提取
$script:FrameworkTypes = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$refsDir = if ($env:TKWFDeployPath) { Join-Path $env:TKWFDeployPath 'build\refs' } else { Join-Path $Repo 'build\refs' }
if (Test-Path -LiteralPath $refsDir) {
    foreach ($x in (Get-ChildItem -LiteralPath $refsDir -Filter 'TKWF.*.xml' -File -ErrorAction SilentlyContinue)) {
        if ($x.Name -like 'TKWF.Ext.*.xml' -or $x.Name -like 'TKWF.Federation.*.xml') { continue }
        foreach ($m in [regex]::Matches((Read-Text $x.FullName), 'name="T:[\w\.]+\.([A-Za-z_]\w*)"')) {
            [void]$script:FrameworkTypes.Add($m.Groups[1].Value)
        }
    }
}

# ── 检查实现 ────────────────────────────────────────────────────

function Invoke-HKA1 {
    $slnSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($sp in $script:SlnxProjects) { [void]$slnSet.Add($sp) }

    foreach ($p in $script:Projects) {
        if (-not (Test-InScope $p.Dir)) { continue }
        $isTemplate = $p.Dir -eq 'Federation.Template'
        $rel = "_Framework/$($p.Dir)/$($p.Csproj.Name)"
        if (-not $isTemplate -and -not $slnSet.Contains($rel)) {
            Add-Finding 'HK-A1' 'P0' '项目未注册到 slnx' $rel "slnx 缺少该项目（构建/CI 不会覆盖）" '加入 TKWF.Extensions.slnx 对应 Folder'
        }
        # 命名（Federation.* 平台库用 TKWF.Federation.{X}；Federation 主包用 TKWF.Ext.Federation；“Federation.Template” 为占位脚手架不校验）
        if (-not $isTemplate) {
            $expect = if ($p.Dir -eq 'Federation') { 'TKWF.Ext.Federation' }
            elseif ($p.Dir -match '^Federation\.') { "TKWF.$($p.Dir)" }
            else { "TKWF.Ext.$($p.Dir)" }
            if ($p.Name -ne $expect) {
                Add-Finding 'HK-A1' 'P1' 'csproj 文件名不符合约定' (Get-RelPath $p.Csproj.FullName) "期望 $expect.csproj，实际 $($p.Name).csproj" '对齐 目录结构与版本管理规则 §二'
            }
        }
        # MinVer 前缀（缺失 = 继承全局 v 前缀 → 会匹配公共 tag，版本静默错误）
        if (-not $isTemplate) {
            if ($p.Text -match '<MinVerTagPrefix>\s*([^<]+?)\s*</MinVerTagPrefix>') {
                $prefix = $Matches[1]
                $expPrefix = "$($p.Dir)/v"
                if ($prefix -ne $expPrefix) {
                    Add-Finding 'HK-A1' 'P1' 'MinVerTagPrefix 与目录名不一致' (Get-RelPath $p.Csproj.FullName) "实际 '$prefix'，期望 '$expPrefix'" '否则 tag 不会驱动该包版本'
                }
            }
            else {
                Add-Finding 'HK-A1' 'P1' 'csproj 缺 MinVerTagPrefix' (Get-RelPath $p.Csproj.FullName) '将继承全局 v 前缀（匹配公共 tag）' "补 <MinVerTagPrefix>$($p.Dir)/v</MinVerTagPrefix>"
            }
        }
        # PackageId
        if ($p.Text -match '<IsPackable>\s*false\s*</IsPackable>') {
            # 不打包：PackageId 不强制
        }
        elseif ($p.Text -match '<PackageId>\s*([^<]+?)\s*</PackageId>') {
            $pkgId = $Matches[1]
            if ($pkgId -ne $p.Name) {
                Add-Finding 'HK-A1' 'P1' 'PackageId 与程序集名不一致' (Get-RelPath $p.Csproj.FullName) "实际 '$pkgId'，程序集 '$($p.Name)'" 'NuGet 依赖图与 tag 前缀会脱节'
            }
        }
    }

    # slnx 指向不存在的项目
    foreach ($sp in $script:SlnxProjects) {
        $full = Join-Path $Repo ($sp.Replace('/', '\'))
        if (-not (Test-Path -LiteralPath $full)) {
            Add-Finding 'HK-A1' 'P0' 'slnx 指向不存在的项目' $sp '文件缺失（构建失败）' '更新 slnx'
        }
    }

    # 测试项目注册（模板/共享基建项目为已知豁免）
    $testExempt = @('TKWF.Ext.Testing.Shared', 'TKWF.Federation.{Namespace}.Tests')
    $testProjects = @(Get-ChildItem -LiteralPath $TestsRoot -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            Get-ChildItem -LiteralPath $_.FullName -Filter *.csproj -File -ErrorAction SilentlyContinue | Select-Object -First 1
        } | Where-Object { $_ })
    foreach ($tp in $testProjects) {
        if ($testExempt -contains $tp.BaseName) { continue }
        $rel = (Get-RelPath $tp.FullName).Replace('\', '/')
        if (-not $slnSet.Contains($rel)) {
            Add-Finding 'HK-A1' 'P2' '测试项目未注册到 slnx' $rel 'CI 受影响面/全量测试不会覆盖' '加入 slnx'
        }
    }
}

function Invoke-HKA2 {
    # README
    foreach ($p in $script:Projects) {
        if (-not (Test-InScope $p.Dir)) { continue }
        if (-not (Test-Path -LiteralPath (Join-Path $p.DirPath 'README.md'))) {
            Add-Finding 'HK-A2' 'P1' '扩展缺 README.md' "($($p.Dir))" '技术规范/随包发布文档缺失' '按 _Framework/{X}/README.md 模板补'
        }
    }

    # 使用指南（主包；契约/附属/模板并入族内）
    $docDirs = @(Get-ChildItem -LiteralPath $DocsRoot -Directory -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name)
    $dirMap = @{ 'AuthSurface' = '授权面' }
    $exempt = @('Federation.Template', 'Permissions.Validation')
    foreach ($p in $script:BuildableProjects) {
        if (-not (Test-InScope $p.Dir)) { continue }
        if ($exempt -contains $p.Dir) { continue }
        if ($p.Dir -match '\.Abstractions$') { continue }
        # 默认可打包（仅显式 false 才跳过）；避免新项目未写 <IsPackable>true 时静默漏检
        if ($p.Text -match '<IsPackable>\s*false\s*</IsPackable>') { continue }

        $base = ($p.Dir -split '\.')[0]
        $candidates = @()
        if ($dirMap.ContainsKey($p.Dir)) { $candidates += $dirMap[$p.Dir] }
        $candidates += $p.Dir
        $candidates += $base
        $found = $false
        foreach ($c in ($candidates | Select-Object -Unique)) {
            if ($docDirs -contains $c) {
                $g = Get-ChildItem -LiteralPath (Join-Path $DocsRoot $c) -Filter '*使用指南*.md' -File -ErrorAction SilentlyContinue
                if ($g) { $found = $true; break }
            }
        }
        if (-not $found) {
            # 全仓指南正文提及该扩展也算覆盖（合并指南）
            $hit = $script:GuideFiles | Where-Object { (Read-Text $_.FullName) -match [regex]::Escape($base) } | Select-Object -First 1
            if ($hit) { $found = $true }
        }
        if (-not $found) {
            Add-Finding 'HK-A2' 'P2' '主包缺使用指南（或未登记到族内合并指南）' "($($p.Dir))" 'docs/ 下无对应 *使用指南*.md' '补指南或在本机制 §4.1 登记豁免理由'
        }
    }
}

function Invoke-HKA3 {
    $cfgDir = Join-Path $Repo '.xCodeGen\extensions'
    foreach ($p in $script:BuildableProjects) {
        if (-not (Test-InScope $p.Dir)) { continue }
        $hasEntity = $false
        foreach ($f in (Get-CsFiles $p.DirPath)) {
            # 注释感知：仅统计真实特性使用，跳过 XML 文档注释/行注释中的示例（HealthCheck 曾误报）
            foreach ($line in [System.IO.File]::ReadAllLines($f.FullName)) {
                $t = $line.Trim()
                if ($t.StartsWith('//') -or $t.StartsWith('*') -or $t.StartsWith('/*')) { continue }
                if ($t -match '\[DomainGenerateCode') { $hasEntity = $true; break }
            }
            if ($hasEntity) { break }
        }
        if (-not $hasEntity) { continue }

        $cfg = Join-Path $cfgDir ("$($p.Dir.ToLower()).xCodeGen.json")
        if (-not (Test-Path -LiteralPath $cfg)) {
            Add-Finding 'HK-A3' 'P1' '实体型扩展缺 xCodeGen 配置' "($($p.Dir)) → .xCodeGen/extensions/$($p.Dir.ToLower()).xCodeGen.json" '生成物将无法重生成/陈旧' '按现有配置复制并改 TargetProject/OutputRoot'
            continue
        }
        try {
            $json = (Read-Text $cfg) | ConvertFrom-Json
            $tp = [string]$json.TargetProject
            if ([string]::IsNullOrWhiteSpace($tp)) {
                Add-Finding 'HK-A3' 'P1' 'xCodeGen 配置缺 TargetProject' (Get-RelPath $cfg) '' '补 TargetProject'
            }
            else {
                $tpFull = [System.IO.Path]::GetFullPath((Join-Path $cfgDir $tp))
                if (-not (Test-Path -LiteralPath $tpFull)) {
                    Add-Finding 'HK-A3' 'P1' 'xCodeGen TargetProject 不可解析' (Get-RelPath $cfg) "→ $tp" '修正相对路径'
                }
            }
            foreach ($art in $json.Artifacts.PSObject.Properties) {
                foreach ($k in @('OutputDir', 'SkeletonDir')) {
                    $v = [string]$art.Value.$k
                    if ($v -and ($v -match '^[A-Za-z]:' -or $v -match '^\\\\' -or $v -match '_Framework[\\/]_Framework')) {
                        Add-Finding 'HK-A3' 'P1' "xCodeGen $($art.Name).$k 使用绝对/翻目录路径" (Get-RelPath $cfg) "= $v" '改为相对 OutputRoot 的 ..\ 风格（AGENTS §8 自检清单 #2）'
                    }
                }
            }
        }
        catch {
            Add-Finding 'HK-A3' 'P1' 'xCodeGen 配置 JSON 解析失败' (Get-RelPath $cfg) "$($_.Exception.Message)" '修正 JSON'
        }
    }
    # 反向 1:1：配置文件指向的 TargetProject 必须存在（防项目删除/改名后遗留孤儿配置）
    foreach ($cfg in (Get-ChildItem -LiteralPath $cfgDir -Filter '*.xCodeGen.json' -File -ErrorAction SilentlyContinue)) {
        try {
            $json = (Read-Text $cfg.FullName) | ConvertFrom-Json
            $tp = [string]$json.TargetProject
            if ($tp) {
                $tpFull = [System.IO.Path]::GetFullPath((Join-Path $cfgDir $tp))
                if (-not (Test-Path -LiteralPath $tpFull)) {
                    Add-Finding 'HK-A3' 'P1' 'xCodeGen 孤儿配置（TargetProject 不存在）' (Get-RelPath $cfg.FullName) "→ $tp" '删除配置或修正 TargetProject'
                }
            }
        }
        catch { }
    }
}

function Invoke-HKA4 {
    $status = @(git -C $Repo status --porcelain -uno)
    $changed = @()
    foreach ($line in $status) {
        if ($line.Length -gt 3) { $changed += ($line.Substring(3).Trim('"').Replace('\', '/')) }
    }

    foreach ($p in $script:BuildableProjects) {
        if (-not (Test-InScope $p.Dir)) { continue }
        $gcs = @(Get-ChildItem -LiteralPath $p.DirPath -Recurse -Filter *.g.cs -File -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -notmatch '[/\\](obj|bin)[/\\]' })
        if ($gcs.Count -eq 0) { continue }

        $srcFiles = @(Get-EntitySourceFiles $p)
        if ($srcFiles.Count -eq 0) { continue }

        # git 变更判定：实体源有变更但该扩展无任何 .g.cs 变更
        $srcChanged = @()
        foreach ($sf in $srcFiles) {
            $sr = Get-RelPath $sf.FullName
            if ($changed -contains $sr) { $srcChanged += $sr }
        }
        $gChanged = @($changed | Where-Object { $_.StartsWith("_Framework/$($p.Dir)/", [System.StringComparison]::OrdinalIgnoreCase) -and $_ -like '*.g.cs' })
        if ($srcChanged.Count -gt 0 -and $gChanged.Count -eq 0) {
            Add-Finding 'HK-A4' 'P1' '实体已变更但生成物未变（疑似未重生成）' "($($p.Dir))" ("实体: " + (($srcChanged | Select-Object -First 3) -join ', ')) '运行 pwsh .\.xCodeGen\run-xcodegen.ps1 -Ext ' + $p.Dir.ToLower() + ' 并提交 .g.cs'
            continue
        }

        # 已提交的实体漂移（Oracle R2）：实体源最后提交时间 > 生成物最后提交时间 → 可能"提交了实体但没重生成"
        $srcRel = @($srcFiles | ForEach-Object { Get-RelPath $_.FullName })
        $srcCommitRaw = & git -C $Repo log -1 --format=%ct -- @srcRel 2>$null
        $genCommitRaw = & git -C $Repo log -1 --format=%ct -- ":(glob)_Framework/$($p.Dir)/**/*.g.cs" 2>$null
        $srcCommit = if ($srcCommitRaw -match '^\d+$') { [long]$srcCommitRaw } else { 0 }
        $genCommit = if ($genCommitRaw -match '^\d+$') { [long]$genCommitRaw } else { 0 }
        if ($srcCommit -gt $genCommit) {
            Add-Finding 'HK-A4' 'P1' '已提交的实体变更早于生成物（疑似提交后未重生成）' "($($p.Dir))" "实体提交 $(Get-Date -UnixTimeSeconds $srcCommit -Format 'yyyy-MM-dd') > 生成物 $(Get-Date -UnixTimeSeconds $genCommit -Format 'yyyy-MM-dd')" '重跑 xCodeGen 并提交 .g.cs（本项是 Permissions 2 周漂移的防线）'
            continue
        }

        # mtime 兜底（注意 fresh clone 无意义：git checkout 会重置时间）
        $maxSrc = ($srcFiles | Measure-Object -Property LastWriteTime -Maximum).Maximum
        $maxGen = ($gcs | Measure-Object -Property LastWriteTime -Maximum).Maximum
        if ($maxSrc -and $maxGen -and ($maxSrc -gt $maxGen.AddMinutes(1))) {
            Add-Finding 'HK-A4' 'P2' '实体源文件比生成物新（mtime 提示）' "($($p.Dir))" "实体 $($maxSrc.ToString('yyyy-MM-dd HH:mm')) > 生成物 $($maxGen.ToString('yyyy-MM-dd HH:mm'))" '若为 fresh clone 可忽略；否则重生成'
        }
    }
}

function Invoke-HKA5 {
    # Oracle R5：Abstractions 内的 *Store 接口是"对外门面契约"（AGENTS §8），不算伪层；Metrics 的 IMetricResultStore 为刻意契约
    $contractExempt = @('_Framework/Metrics/IMetricResultStore.cs')
    foreach ($p in $script:BuildableProjects) {
        if (-not (Test-InScope $p.Dir)) { continue }
        if ($p.Dir -match '\.Abstractions$') { continue }
        foreach ($f in (Get-CsFiles $p.DirPath)) {
            $rel = Get-RelPath $f.FullName
            if ($contractExempt -contains $rel) { continue }
            # SG1/xCodeGen 生成的 DataService 基座/分部（DataServices/、Generated/）以 IEntityDAC 为既定注入点——红线约束的是 Store/Service 自定义层
            if ($rel -match '/DataServices/' -or $rel -match '/Generated/') { continue }
            $lines = [System.IO.File]::ReadAllLines($f.FullName)
            for ($i = 0; $i -lt $lines.Count; $i++) {
                $ln = $lines[$i]
                $trim = $ln.Trim()
                if ($trim.StartsWith('//') -or $trim.StartsWith('*') -or $trim.StartsWith('/*')) { continue }
                if ($trim -match '^\s*$') { continue }
                if ($ln -match '\bIFreeSql\b' -and $ln -notmatch '\busing\b' -and $ln -notmatch '<see\b') {
                    Add-Finding 'HK-A5' 'P2' '裸 IFreeSql 使用' "${rel}:$($i + 1)" $trim '红线：改用 SG1 DataService（AGENTS §8 数据访问红线）' -Ratchet
                }
                if ($ln -match '\bIEntityDAC<' -and $ln -notmatch 'GetService<|is null|is not null|typeof\(') {
                    Add-Finding 'HK-A5' 'P2' '直接使用 IEntityDAC<T>' "${rel}:$($i + 1)" $trim '红线：改用 SG1 DataService/ReadOnlyDataService' -Ratchet
                }
                if ($ln -match '\b(?:class|interface)\s+(I?\w+Store)\b') {
                    Add-Finding 'HK-A5' 'P2' '手写 Store 类/接口（存量债务棘轮）' "${rel}:$($i + 1)" $trim '红线：DataService 已含 CRUD，门面直接 User.Use<DataService>()' -Ratchet
                }
            }
        }
    }
}

function Invoke-HKA6 {
    $namePattern = '(Json|Content|Body|Payload|Html|Xml|Sql|Message|Template)'
    foreach ($p in $script:BuildableProjects) {
        if (-not (Test-InScope $p.Dir)) { continue }
        foreach ($f in (Get-EntitySourceFiles $p)) {
            $rel = Get-RelPath $f.FullName
            $lines = [System.IO.File]::ReadAllLines($f.FullName)
            for ($i = 0; $i -lt $lines.Count; $i++) {
                if ($lines[$i] -notmatch "public\s+string\??\s+([A-Za-z_]\w*$namePattern)\s*\{\s*get") { continue }
                $prop = $Matches[1]
                $window = $lines[[Math]::Max(0, $i - 8)..$i] -join "`n"
                # 无界等价写法：BCL [MaxLength(-1)] 或 FreeSql [Column(StringLength = -1)]；文档注明"截断"的有界列视为有意设计
                if ($window -match 'MaxLength\(\s*-1\s*\)|StringLength\s*=\s*-1') { continue }
                if ($window -match '截断') { continue }
                Add-Finding 'HK-A6' 'P2' '内容列疑似缺 [MaxLength(-1)]' "${rel}:$($i + 1)" "属性 $prop" '无界 string 须显式 [MaxLength(-1)]（防 Pg 22001）' -Ratchet
            }
        }
    }
}

function Invoke-HKA7 {
    $exempt = @('Federation.Template', 'Permissions.Validation', 'Analytics', 'Dashboard', 'HealthCheck', 'RateLimiting')
    foreach ($p in $script:BuildableProjects) {
        if (-not (Test-InScope $p.Dir)) { continue }
        if ($exempt -contains $p.Dir) { continue }
        if ($p.Dir -match '\.Abstractions$') { continue }
        if ($p.Text -match '<IsPackable>\s*false\s*</IsPackable>') { continue }
        $candidates = @(
            (Join-Path $TestsRoot "Extension.$($p.Dir).Tests"),
            (Join-Path $TestsRoot "Extension.$($p.Dir -replace '\.', '.').Tests")
        )
        $found = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if (-not $found) {
            Add-Finding 'HK-A7' 'P2' '主包缺测试项目' "($($p.Dir))" "_Tests/Extension.$($p.Dir).Tests 不存在" '补测试或在本机制登记豁免'
        }
    }
}

function Invoke-HKB1 {
    foreach ($p in $script:BuildableProjects) {
        if (-not (Test-InScope $p.Dir)) { continue }
        $csprojDir = Split-Path $p.Csproj.FullName -Parent
        foreach ($m in [regex]::Matches($p.Text, '<ProjectReference\s+Include="([^"]+)"')) {
            $inc = $m.Groups[1].Value
            if ($inc -notmatch '_Framework[\\/]([^\\/]+)[\\/]') { continue }
            $target = $Matches[1]
            if ($target -eq $p.Dir) { continue }
            $sameFamily = ($target -split '\.')[0] -eq ($p.Dir -split '\.')[0]
            $isContract = $target -match '\.Abstractions$'
            if (-not ($isContract -or $sameFamily)) {
                $sev = if ($p.Dir -match '\.Abstractions$') { 'P0' } else { 'P1' }
                Add-Finding 'HK-B1' $sev '跨扩展引用实现包（应走 Abstractions/同族）' (Get-RelPath $p.Csproj.FullName) "→ $target" 'L2 门控 TKWF0022；同族（Federation.*/Notifications.*）复用除外'
            }
        }
        foreach ($m in [regex]::Matches($p.Text, '<PackageReference\s+Include="(TKWF\.(?:Ext|Federation)\.[^"]+)"')) {
            $pkg = $m.Groups[1].Value
            if ($pkg -eq $p.Name) { continue }
            $short = $pkg -replace '^TKWF\.(Ext|Federation)\.', ''
            $sameFamily = ($short -split '\.')[0] -eq ($p.Dir -split '\.')[0]
            if ($short -match '\.Abstractions$' -or $sameFamily) { continue }
            $sev = if ($p.Dir -match '\.Abstractions$') { 'P0' } else { 'P1' }
            Add-Finding 'HK-B1' $sev '跨扩展以 NuGet 包引用实现包' (Get-RelPath $p.Csproj.FullName) "→ $pkg" '改引 *-Abstractions 契约包'
        }
    }
}

function Invoke-HKB2 {
    $tableMap = @{}
    $pairs = @()
    foreach ($p in $script:BuildableProjects) {
        if (-not (Test-InScope $p.Dir)) { continue }
        foreach ($f in (Get-EntitySourceFiles $p)) {
            $rel = Get-RelPath $f.FullName
            $raw = Read-Text $f.FullName
            $txt = [regex]::Replace($raw, '(?m)^\s*///.*$', '')
            $txt = [regex]::Replace($txt, '(?m)^\s*//.*$', '')
            $tableNames = @()
            foreach ($m in [regex]::Matches($txt, '\[Table\(\s*Name\s*=\s*"([^"]+)"')) { $tableNames += $m.Groups[1].Value }
            foreach ($m in [regex]::Matches($txt, '\[Table\(\s*"([^"]+)"')) { $tableNames += $m.Groups[1].Value }
            # 视图（DisableSyncStructure=true）必须用 TKWFV_ 前缀
            foreach ($tm in [regex]::Matches($txt, '\[Table\((?<body>[^\]]*)\)\]')) {
                $body = $tm.Groups['body'].Value
                if ($body -match 'DisableSyncStructure\s*=\s*true') {
                    $nm = [regex]::Match($body, 'Name\s*=\s*"([^"]+)"')
                    if (-not $nm.Success) { $nm = [regex]::Match($body, '"([^"]+)"') }
                    if ($nm.Success -and $nm.Groups[1].Value -notmatch '^TKWFV_') {
                        Add-Finding 'HK-B2' 'P1' '视图名须 TKWFV_ 前缀' $rel "= $($nm.Groups[1].Value)" '目录结构与版本管理规则 §8'
                    }
                }
            }
            foreach ($tn in $tableNames) {
                if ($tn -notmatch '^(TKWF_|TKWFV_)') {
                    Add-Finding 'HK-B2' 'P1' '表/视图名缺 TKWF_/TKWFV_ 前缀' $rel "= $tn" '目录结构与版本管理规则 §8'
                }
                if (-not $tableMap.ContainsKey($tn)) { $tableMap[$tn] = [System.Collections.Generic.HashSet[string]]::new() }
                [void]$tableMap[$tn].Add($p.Dir)
            }
            if ($txt -match '\[Table\]' -and $tableNames.Count -eq 0) {
                Add-Finding 'HK-B2' 'P2' '裸 [Table]（未声明 Name 别名）' $rel '' '扩展实体应手写别名（TABLE 门控族）'
            }
            foreach ($m in [regex]::Matches($txt, '\[Index\(\s*"([^"]+)"')) {
                $ix = $m.Groups[1].Value
                if ($ix -notmatch '^TKWFIX_') {
                    Add-Finding 'HK-B2' 'P1' '索引名缺 TKWFIX_ 前缀' $rel "= $ix" '目录结构与版本管理规则 §8'
                }
            }
        }
    }
    foreach ($kv in $tableMap.GetEnumerator()) {
        if ($kv.Value.Count -gt 1) {
            Add-Finding 'HK-B2' 'P1' '跨扩展表名撞名' $kv.Key ("涉及: " + (($kv.Value | Sort-Object) -join ', ')) '按 §8.4 追尾方加扩展简称'
        }
    }
}

function Invoke-HKB3 {
    foreach ($p in $script:BuildableProjects) {
        if (-not (Test-InScope $p.Dir)) { continue }
        foreach ($m in [regex]::Matches($p.Text, '<ProjectReference\s+Include="([^"]+)"')) {
            $inc = $m.Groups[1].Value
            if ($inc -notmatch '\.Abstractions[\\/]') { continue }
            $parts = @($inc -split '[\\/]' | Where-Object { $_ })
            if ($parts.Count -lt 2) { continue }
            $target = $parts[$parts.Count - 2]   # csproj 所在目录名
            $tp = $script:Projects | Where-Object { $_.Dir -eq $target } | Select-Object -First 1
            if (-not $tp) {
                Add-Finding 'HK-B3' 'P1' '引用的 Abstractions 项目不存在' (Get-RelPath $p.Csproj.FullName) "→ $target" '检查契约包是否被删除/改名'
            }
        }
    }
}

function Invoke-HKB4 {
    # 弱检查：权限键命名（Xxx.Yyy）+ 重复键跨扩展
    $keys = @{}
    foreach ($p in $script:BuildableProjects) {
        if (-not (Test-InScope $p.Dir)) { continue }
        foreach ($f in (Get-CsFiles $p.DirPath)) {
            $txt = Read-Text $f.FullName
            foreach ($m in [regex]::Matches($txt, 'RequirePermission\(\s*"([^"]+)"')) {
                $k = $m.Groups[1].Value
                if ($k -notmatch '^[A-Za-z][\w\-]*\.[A-Za-z][\w\-]*') {
                    Add-Finding 'HK-B4' 'P2' '权限键命名不规范' (Get-RelPath $f.FullName) "= $k" '建议 {模块}.{动作}'
                }
                if (-not $keys.ContainsKey($k)) { $keys[$k] = [System.Collections.Generic.HashSet[string]]::new() }
                [void]$keys[$k].Add($p.Dir)
            }
        }
    }
    foreach ($kv in $keys.GetEnumerator()) {
        if ($kv.Value.Count -gt 1) {
            Add-Finding 'HK-B4' 'P2' '权限键跨扩展重复（确认是否共享语义）' $kv.Key ("涉及: " + (($kv.Value | Sort-Object) -join ', ')) '共享键须由属主声明并加注释'
        }
    }
}

function Invoke-HKC1 {
    $files = @()
    $files += Get-Item $RootReadme, $AgentsFile -ErrorAction SilentlyContinue
    $files += $script:AllDocsMd
    $files += Get-ChildItem -LiteralPath $FrameworkRoot -Recurse -Depth 1 -Filter README.md -File -ErrorAction SilentlyContinue

    $linkPattern = '\[[^\]]*\]\(([^)\s]+)'
    foreach ($f in ($files | Sort-Object FullName -Unique)) {
        $raw = Read-Text $f.FullName
        if (-not $raw) { continue }
        # 去掉围栏代码块与行内代码，避免示例链接误报
        $txt = [regex]::Replace($raw, '(?s)```.*?```', '')
        $txt = [regex]::Replace($txt, '`[^`]*`', '')
        $dir = Split-Path $f.FullName -Parent
        foreach ($m in [regex]::Matches($txt, $linkPattern)) {
            $target = $m.Groups[1].Value.Trim()
            if ($target -match '^(https?:|mailto:|ftp:|#)') { continue }
            if ($target -match '^<|^\{|\{|\}') { continue }
            $t = $target
            if ($t.Contains('#')) { $t = $t.Substring(0, $t.IndexOf('#')) }
            if ($t.Contains('?')) { $t = $t.Substring(0, $t.IndexOf('?')) }
            if ([string]::IsNullOrWhiteSpace($t)) { continue }
            try { $t = [System.Uri]::UnescapeDataString($t) } catch { }
            $t = $t.Replace('/', '\')
            $full = [System.IO.Path]::GetFullPath((Join-Path $dir $t))
            if (-not (Test-Path -LiteralPath $full)) {
                Add-Finding 'HK-C1' 'P1' 'Markdown 相对链接目标不存在' (Get-RelPath $f.FullName) "→ $target" '文档搬迁后需同步链接'
            }
        }
    }
}

function Invoke-HKC2 {
    # ADR 三问
    foreach ($f in $script:AllDocsMd) {
        if ($f.FullName -notmatch '[/\\]ADR[/\\]') { continue }
        if ($f.Name -notlike 'ADR-*') {
            Add-Finding 'HK-C2' 'P2' 'ADR 文件名不符合 ADR-*.md' (Get-RelPath $f.FullName) '' '按 AGENTS §4 命名'
        }
        $txt = Read-Text $f.FullName
        foreach ($need in @('目的与目标', '问题', '使用场景')) {
            # 锚定到标题（Oracle 建议：避免正文/变更记录中出现关键词即通过）
            if ($txt -notmatch ("(?m)^#{1,4}\s*.*" + [regex]::Escape($need))) {
                Add-Finding 'HK-C2' 'P1' "ADR 缺三问小节：$need" (Get-RelPath $f.FullName) '' 'ADR 三问必填（AGENTS §4）'
            }
        }
    }

    # 开发方案命名（历史文档多，仅作 INFO 提示，不逐条）
    $badPlans = @($script:AllDocsMd | Where-Object { $_.Name -like '*开发方案*.md' -and $_.Name -notmatch '^v\d+\.\d+\.\d+-' })
    if ($badPlans.Count -gt 0) {
        Add-Finding 'HK-C2' 'INFO' '开发方案缺版本前缀（历史文档可豁免）' "(docs)" ("$($badPlans.Count) 个；新方案统一 vX.Y.Z-*-开发方案.md") '仅新方案强制，旧文档渐进迁移'
    }

    # 指南命名（模板目录豁免）
    $badGuides = @($script:GuideFiles | Where-Object { $_.Name -notlike '*-使用指南*.md' -and $_.FullName -notmatch '[/\\]模板[/\\]' })
    foreach ($g in $badGuides) {
        Add-Finding 'HK-C2' 'P2' '使用指南命名不规范' (Get-RelPath $g.FullName) '' '统一 *-使用指南.md'
    }
}

function Invoke-HKC3 {
    if (-not (Test-Path -LiteralPath $InvariantsPath)) { return }
    try { $rules = (Read-Text $InvariantsPath) | ConvertFrom-Json }
    catch { Add-Finding 'HK-C3' 'P2' '关键事实断言文件解析失败' (Get-RelPath $InvariantsPath) $_.Exception.Message ''; return }
    foreach ($r in @($rules)) {
        $path = Join-Path $Repo $r.file
        if (-not (Test-Path -LiteralPath $path)) {
            Add-Finding 'HK-C3' 'P2' '断言目标文件不存在' $r.file '' '更新 healthcheck-doc-invariants.json'
            continue
        }
        $txt = Read-Text $path
        if ($r.mustContain) {
            foreach ($need in @($r.mustContain)) {
                if ($txt -notmatch $need) {
                    Add-Finding 'HK-C3' 'P1' '关键事实缺失（mustContain）' $r.file "期望匹配: $need" $r.note
                }
            }
        }
        if ($r.mustNotContain) {
            foreach ($bad in @($r.mustNotContain)) {
                if ($txt -match $bad) {
                    Add-Finding 'HK-C3' 'P1' '过期表述仍存在（mustNotContain）' $r.file "命中: $bad" $r.note
                }
            }
        }
    }
}

function Invoke-HKC4 {
    $txt = Read-Text $DocsAgents
    if (-not $txt) { return }
    foreach ($m in [regex]::Matches($txt, '`([^`]+\.md)`')) {
        $p = $m.Groups[1].Value
        if ($p -match '\*|\{|\}|^https?:') { continue }
        $docsDir = Split-Path $DocsAgents -Parent
        $candidates = @(
            [System.IO.Path]::GetFullPath((Join-Path $Repo $p)),          # 仓库根相对（Agents_TKWF_Ext.md / docs/...）
            [System.IO.Path]::GetFullPath((Join-Path $docsDir $p)),      # docs 相对（AuthCenter/xxx.md）
            [System.IO.Path]::GetFullPath((Join-Path $docsDir ($p -replace '^docs[/\\]', '')))
        )
        $exists = $false
        foreach ($c in $candidates) { if (Test-Path -LiteralPath $c) { $exists = $true; break } }
        if (-not $exists -and $p -notmatch '[/\\]') {
            # 裸文件名：在 docs 下按文件名兜底（路由表中常见"认证中心-使用指南.md"写法）
            if (Get-ChildItem -LiteralPath $docsDir -Recurse -Filter $p -File -ErrorAction SilentlyContinue | Select-Object -First 1) { $exists = $true }
        }
        if (-not $exists) {
            Add-Finding 'HK-C4' 'P2' 'AGENTS 路由路径不存在' "docs/AGENTS.md" "→ $p" '更新路由表'
        }
    }
}

function Invoke-HKD1 {
    # versions.json vs tag
    $vjPath = Join-Path $DocsRoot 'versions.json'
    if (Test-Path -LiteralPath $vjPath) {
        try {
            $vj = (Read-Text $vjPath) | ConvertFrom-Json
            $known = @{}
            foreach ($prop in $vj.packages.PSObject.Properties) { $known[$prop.Name] = [string]$prop.Value.version }
            foreach ($pkg in $script:PackageTags.Keys) {
                $latest = $script:PackageTags[$pkg].Version
                if (-not $known.ContainsKey($pkg)) {
                    Add-Finding 'HK-D1' 'P1' 'versions.json 缺包' $pkg "git tag 最新 $latest" '重跑 build/gen-versions.ps1 并提交'
                }
                elseif ($known[$pkg] -ne $latest) {
                    Add-Finding 'HK-D1' 'P1' 'versions.json 版本滞后' $pkg "json=$($known[$pkg]) / tag=$latest" '重跑 build/gen-versions.ps1 并提交'
                }
            }
            foreach ($pkg in $known.Keys) {
                if (-not $script:PackageTags.ContainsKey($pkg)) {
                    Add-Finding 'HK-D1' 'P2' 'versions.json 含无 tag 包' $pkg "json=$($known[$pkg])" '检查 tag 是否缺失或包已退役'
                }
            }
        }
        catch { Add-Finding 'HK-D1' 'P1' 'versions.json 解析失败' 'docs/versions.json' $_.Exception.Message '重跑 gen-versions.ps1' }
    }

    # 根 README 版本表：Tag 列（逐行解析，规避 CRLF 下 (?m)$ 边界问题）
    $seen = @{}
    foreach ($line in [System.IO.File]::ReadAllLines($RootReadme)) {
        $r = [regex]::Match($line, '^\|\s*\*\*([A-Za-z0-9_.]+)\*\*\s*\|(.*)$')
        if (-not $r.Success) { continue }
        $pkg = $r.Groups[1].Value
        $rest = $r.Groups[2].Value
        $tm = [regex]::Match($rest, '`([A-Za-z0-9_.]+)/v(\d+\.\d+\.\d+)`')
        if (-not $tm.Success) { continue }
        $tagPkg = $tm.Groups[1].Value
        $ver = $tm.Groups[2].Value
        $seen[$tagPkg] = $true
        if (-not $script:PackageTags.ContainsKey($tagPkg)) {
            Add-Finding 'HK-D1' 'P1' 'README Tag 列指向不存在的包/前缀' $tagPkg "行: $pkg" '修正 Tag 列为真实 tag'
        }
        elseif ($script:PackageTags[$tagPkg].Version -ne $ver) {
            Add-Finding 'HK-D1' 'P1' 'README 版本表滞后于最新 tag' $tagPkg "README=$ver / tag=$($script:PackageTags[$tagPkg].Version)" '按 §3.3 发布检查清单同步'
        }
    }
    # E4 逻辑独立在 Invoke-HKE4（本函数仅做版本表比对与 Tag 合法性）

    # _Framework README 版本记录（仅对已有"版本记录/Version 历史"形态的 README 校验；无版本段视为不适用）
    foreach ($p in $script:BuildableProjects) {
        if (-not (Test-InScope $p.Dir)) { continue }
        if (-not $script:PackageTags.ContainsKey($p.Dir)) { continue }
        $ver = $script:PackageTags[$p.Dir].Version
        $rp = Join-Path $p.DirPath 'README.md'
        if (-not (Test-Path -LiteralPath $rp)) { continue }
        $txt = Read-Text $rp
        if ($txt -notmatch '版本记录|###\s+V?\d+\.\d+\.\d+') { continue }
        if ($txt -notmatch ("V?" + [regex]::Escape($ver))) {
            Add-Finding 'HK-D1' 'P2' '扩展 README 版本记录可能滞后（人工确认）' (Get-RelPath $rp) "最新 tag v$ver 未在版本记录中出现" '按 §3.3 同步（功能线领先可用双值写法）'
        }
    }
}

function Invoke-HKD2 {
    # 环境依赖显式化（Oracle R3）：未部署 refs 时 FrameworkTypes 不可得 → D2 降级为 INFO，避免框架类型误报洪泛
    if ($script:FrameworkTypes.Count -eq 0) {
        Add-Finding 'HK-D2' 'INFO' 'D2 降级：框架类型清单不可用（未找到部署根 refs XML）' '(env:TKWFDeployPath\build\refs)' '' '本地部署后复跑可获得完整结果；CI 无部署根时该项不参与判定'
        return
    }
    foreach ($g in $script:GuideFiles) {
        $rel = Get-RelPath $g.FullName
        $types = [System.Collections.Generic.HashSet[string]]::new()
        # 按行扫描：跳过"更名/废弃/迁移/历史"类对照表（旧类型名出现在迁移说明中属正常）
        foreach ($line in [System.IO.File]::ReadAllLines($g.FullName)) {
            if ($line -match '更名|废弃|迁移|历史|旧名|→|已删|拆分自|迁入|破坏性') { continue }
            foreach ($m in [regex]::Matches($line, 'Use<\s*([A-Za-z_]\w*)\s*>')) { [void]$types.Add($m.Groups[1].Value) }
            foreach ($m in [regex]::Matches($line, 'typeof\(\s*([A-Za-z_]\w*)')) { [void]$types.Add($m.Groups[1].Value) }
        }
        foreach ($t in $types) {
            # 只校验"扩展契约/门面/初始化器"形态：I* 接口 或 *ExtensionInitializer；类型参数与占位符跳过
            if ($t -notmatch '^(I[A-Z]\w*|\w+ExtensionInitializer)$') { continue }
            if ($t -match '^(IXxx|Xxx)') { continue }
            if ($script:DeclaredTypes.Contains($t)) { continue }
            if ($script:FrameworkTypes.Contains($t)) { continue }
            Add-Finding 'HK-D2' 'P2' '指南引用的类型在扩展代码中不存在' $rel "类型: $t" '可能是消费方类型（人工确认）或 API 已改名；改名则同步指南'
        }
    }
}

function Invoke-HKD3 {
    $codeSections = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($p in $script:BuildableProjects) {
        foreach ($f in (Get-CsFiles $p.DirPath)) {
            $txt = Read-Text $f.FullName
            foreach ($m in [regex]::Matches($txt, '(?:\[Options\(|GetSection\()\s*"(TKWF:[^"]+)"')) {
                [void]$codeSections.Add($m.Groups[1].Value)
            }
        }
    }
    $sectionList = @($codeSections)
    # 代码侧"配置节证据"：除 [Options]/GetSection 外，包含常量/绑定字符串（如 TKWF:Federation:LoginEntry 经 AddOptions().Configure）
    $codeAll = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($p in $script:BuildableProjects) {
        foreach ($f in (Get-CsFiles $p.DirPath)) {
            foreach ($m in [regex]::Matches((Read-Text $f.FullName), '\b(TKWF:[A-Za-z0-9:_\-]+)')) { [void]$codeAll.Add($m.Groups[1].Value.TrimEnd(':')) }
        }
    }
    foreach ($s in $codeAll) { $sectionList += $s }
    # 仅校验"扩展自己的配置根"（TKWF:{扩展节}）；TKWF:Api:* / TKWF:OAuthResourceServer 等框架/消费方键不归扩展管
    $knownRoots = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($s in $sectionList) {
        $segs = $s -split ':'
        if ($segs.Count -ge 2) { [void]$knownRoots.Add($segs[0] + ':' + $segs[1]) }
    }
    foreach ($g in $script:GuideFiles) {
        $txt = Read-Text $g.FullName
        $rel = Get-RelPath $g.FullName
        $keys = [System.Collections.Generic.HashSet[string]]::new()
        foreach ($m in [regex]::Matches($txt, '\bTKWF:[A-Za-z0-9:_\-]+')) { [void]$keys.Add($m.Value.TrimEnd(':')) }
        foreach ($k in $keys) {
            $segs = $k -split ':'
            if ($segs.Count -lt 2) { continue }
            # 框架/消费方配置不归扩展校验：TKWF:Api:*（路由模块前缀）、TKWF:OAuthResourceServer（框架中间件）
            if ($k -match '^TKWF:(Api|OAuthResourceServer)\b') { continue }
            $root = $segs[0] + ':' + $segs[1]
            if (-not $knownRoots.Contains($root)) { continue }
            $compatible = $false
            foreach ($s in $sectionList) {
                if ($k.StartsWith($s, [System.StringComparison]::OrdinalIgnoreCase) -or $s.StartsWith($k, [System.StringComparison]::OrdinalIgnoreCase)) { $compatible = $true; break }
            }
            if (-not $compatible) {
                Add-Finding 'HK-D3' 'P2' '指南配置键在代码中无对应节' $rel "键: $k" '核对 Options 节名；文档更新或代码补 Options'
            }
        }
    }
}

function Invoke-HKE1 {
    $legacyPrefixes = @('Authentication', 'UserCenter', 'UserCenter.Abstractions', 'SSO', 'KeyStore')
    foreach ($t in $script:Tags) {
        if ($t -match '^([^/]+)/(v?\d+\.\d+\.\d+)$') { continue }
        if ($t -match '^v\d+\.\d+\.\d+$') { continue }
        Add-Finding 'HK-E1' 'P1' 'tag 命名不符合规范' $t '' '格式 {Package}/vX.Y.Z 或无前缀 vX.Y.Z（仅公共内容）'
    }
    # 孤儿 tag 前缀：包前缀既无对应项目、也不在退役白名单（拼写错误 tag 会静默存在）
    foreach ($pkg in $script:PackageTags.Keys) {
        $hasProject = $script:Projects | Where-Object { $_.Dir -eq $pkg } | Select-Object -First 1
        if (-not $hasProject -and $legacyPrefixes -notcontains $pkg) {
            Add-Finding 'HK-E1' 'P2' 'tag 前缀无对应项目（疑似拼写/历史遗留）' $pkg "最新 $($script:PackageTags[$pkg].Version)" '核对 tag 前缀；退役包加入白名单'
        }
    }
}

function Invoke-HKE2 {
    $cpm = Read-Text $CpmPath
    $versions = @{}
    foreach ($m in [regex]::Matches($cpm, '<PackageVersion\s+Include="([^"]+)"\s+Version="([^"]+)"')) {
        $versions[$m.Groups[1].Value] = $m.Groups[2].Value
    }
    $main = @('TKWF.Domain', 'TKWF.Utility', 'TKWF.Core', 'TKWF.Abstractions', 'TKWF.BackgroundJobs',
        'TKWF.BackgroundJobs.Quartz', 'TKWF.Utility.DataPort', 'TKWF.Utility.DataPort.Providers.MiniExcel',
        'TKWF.Domain.FreeSql', 'TKWF.Domain.Web', 'TKWF.Domain.Testing.xUnit')
    # Oracle R3：lockstep 组从 CPM 派生（新增框架包自动纳入），仅排除 Analyzer 双包与扩展包
    $derived = @($versions.Keys | Where-Object {
            $_ -like 'TKWF.*' -and $_ -notlike 'TKWF.CodeGeneration*' -and $_ -notlike 'TKWF.Ext.*' -and $_ -notlike 'TKWF.Federation.*'
        })
    if ($derived.Count -gt $main.Count) { $main = $derived }
    $mainVers = @($main | ForEach-Object { $versions[$_] } | Where-Object { $_ } | Select-Object -Unique)
    if ($mainVers.Count -gt 1) {
        Add-Finding 'HK-E2' 'P0' 'CPM 主框架正式号不统一' 'Directory.Packages.props' ("版本: " + ($mainVers -join ', ')) '11 个正式号必须 lockstep 同版'
    }
    $cg = $versions['TKWF.CodeGeneration']
    $cga = $versions['TKWF.CodeGeneration.Abstractions']
    if ($cg -and $cga -and $cg -ne $cga) {
        Add-Finding 'HK-E2' 'P0' 'Analyzer 双锁不同版' 'Directory.Packages.props' "CodeGeneration=$cg / Abstractions=$cga" '两包必须同版（同批 pack）'
    }
    if ($cg -and $cg -match '^(.+)-local$') {
        $ver = $Matches[1]
        $dep = $env:TKWFDeployPath
        if ($dep) {
            $feed = Join-Path $dep 'local-feed'
            $found = @()
            if (Test-Path -LiteralPath $feed) {
                $found = Get-ChildItem -LiteralPath $feed -Filter "TKWF.CodeGeneration.*.nupkg" -File -ErrorAction SilentlyContinue |
                    Where-Object { $_.Name -like "*$ver-local*" -or $_.Name -like "*$cg*" }
            }
            if (-not $found -or $found.Count -eq 0) {
                Add-Finding 'HK-E2' 'P1' 'Analyzer -local 包在本地 feed 缺失' "Directory.Packages.props" "锁 $cg；feed=$feed" 'NuGet restore 将 NU1102；请框架组重 pack 或改锁'
            }
        }
    }
    # 注释头漂移
    $hm = [regex]::Match($cpm, '当前\s*lockstep\s*=\s*\**\s*([0-9]+\.[0-9]+\.[0-9]+)')
    if ($hm.Success -and $mainVers.Count -eq 1 -and $hm.Groups[1].Value -ne $mainVers[0]) {
        Add-Finding 'HK-E2' 'P2' 'CPM 注释头"当前 lockstep"与实值不一致' 'Directory.Packages.props' "注释=$($hm.Groups[1].Value) / 实值=$($mainVers[0])" '更新注释，避免误导版本判定'
    }
}

function Invoke-HKE3 {
    $dep = $env:TKWFDeployPath
    $refs = if ($dep) { Join-Path $dep 'build\refs' } else { Join-Path $Repo 'build\refs' }
    if (-not (Test-Path -LiteralPath $refs)) { return }
    $legal = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($p in $script:Projects) { [void]$legal.Add($p.Name) }
    $testProjectNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    Get-ChildItem -LiteralPath $TestsRoot -Recurse -Filter *.csproj -File -ErrorAction SilentlyContinue | ForEach-Object { [void]$testProjectNames.Add($_.BaseName) }
    foreach ($n in $testProjectNames) { [void]$legal.Add($n) }

    $dlls = @(Get-ChildItem -LiteralPath $refs -Filter 'TKWF.Ext.*.dll' -File -ErrorAction SilentlyContinue) +
    @(Get-ChildItem -LiteralPath $refs -Filter 'TKWF.Federation.*.dll' -File -ErrorAction SilentlyContinue)
    foreach ($d in $dlls) {
        if (-not $legal.Contains($d.BaseName)) {
            Add-Finding 'HK-E3' 'P1' '部署 refs 孤儿 DLL（无对应项目）' (Get-RelPath $d.FullName) '' '运行 build\build-ext-deployment.ps1 -Cleanup'
        }
    }
    # 缺口：可打包项目在 refs 中无 DLL
    $dllNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($d in $dlls) { [void]$dllNames.Add($d.BaseName) }
    foreach ($p in $script:BuildableProjects) {
        if ($p.Text -notmatch '<IsPackable>\s*true') { continue }
        if (-not $dllNames.Contains($p.Name)) {
            Add-Finding 'HK-E3' 'P2' 'refs 缺该扩展 DLL（可能未构建）' $p.Name '' '构建后由 _PushToRefs 推送'
        }
    }
    if ($dlls.Count -gt 0) {
        $clusters = $dlls | Group-Object { $_.LastWriteTime.ToString('yyyy-MM-dd') } | Sort-Object Name
        if ($clusters.Count -gt 1) {
            $desc = ($clusters | ForEach-Object { "$($_.Name)=$($_.Count)" }) -join '; '
            Add-Finding 'HK-E3' 'INFO' 'refs 扩展 DLL 存在跨日批次（只读提示）' 'build/refs' $desc '如需一致性收口：build-ext-deployment.ps1 -Cleanup'
        }
    }
}

function Invoke-HKE4 {
    # 根 README 覆盖：每个在册包应有行（契约包 Tag 列可为 —；退役包不要求）
    $rows = @{}
    foreach ($line in [System.IO.File]::ReadAllLines($RootReadme)) {
        $r = [regex]::Match($line, '^\|\s*\*\*([A-Za-z0-9_.]+)\*\*\s*\|')
        if ($r.Success) { $rows[$r.Groups[1].Value] = $true }
    }
    foreach ($p in $script:BuildableProjects) {
        if (-not (Test-InScope $p.Dir)) { continue }
        if (-not $script:PackageTags.ContainsKey($p.Dir)) { continue }
        if (-not $rows.ContainsKey($p.Dir)) {
            Add-Finding 'HK-E4' 'P2' '有 tag 的包未出现在根 README 一览表' $p.Dir "最新 $($script:PackageTags[$p.Dir].Version)" '补行或确认并入主包展示'
        }
    }
}

function Invoke-HKF3 {
    $patterns = @(
        @{ Name = '私钥块'; Regex = '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----' },
        @{ Name = 'sk- 密钥'; Regex = '\bsk-[A-Za-z0-9]{20,}' },
        @{ Name = '明文口令/密钥赋值'; Regex = '(?i)\b(password|pwd|secret|apikey|api_key|api-key)\s*[:=]\s*"([^"$<+{]{12,})"' }
    )
    $placeholder = '(?i)^(password|passwd|123456|test|demo|example|changeme|default|placeholder|sample)'
    foreach ($p in $script:BuildableProjects) {
        foreach ($f in (Get-CsFiles $p.DirPath)) {
            $rel = Get-RelPath $f.FullName
            $txt = Read-Text $f.FullName
            foreach ($pat in $patterns) {
                foreach ($m in [regex]::Matches($txt, $pat.Regex)) {
                    $val = $m.Value
                    if ($val -match '(?i)your[-_]|changeme|example|placeholder|\*\*\*|xxx') { continue }
                    # 赋值字面量：排除明显占位/默认值（如 Password = "password"）
                    $lit = if ($m.Groups.Count -gt 2) { $m.Groups[2].Value } else { '' }
                    if ($lit -and $lit -match $placeholder) { continue }
                    if ($lit -and $lit -notmatch '[A-Za-z]' ) { continue }
                    if ($lit -and ($lit -match '^\s*$')) { continue }
                    Add-Finding 'HK-F3' 'P1' "疑似硬编码机密（$($pat.Name)）" $rel $val.Substring(0, [Math]::Min(80, $val.Length)) '改为配置/密钥提供器；样例值用明显占位符（人工确认后可豁免）'
                }
            }
        }
    }
}

function Invoke-HKG1 {
    $retired = @{ 'UserCenter' = 'docs/UserCenter'; 'SSO' = 'docs/SSO' }
    foreach ($name in $retired.Keys) {
        if (Test-Path -LiteralPath (Join-Path $FrameworkRoot $name)) {
            Add-Finding 'HK-G1' 'P2' '退役扩展源码目录仍存在' "_Framework/$name" '' '确认是否应删除/归档'
        }
        if ($script:SlnxProjects | Where-Object { $_ -match "[/\\]$([regex]::Escape($name))[/\\]" }) {
            Add-Finding 'HK-G1' 'P1' '退役扩展仍在 slnx' "_Framework/$name" '' '从 slnx 移除'
        }
        if (-not (Test-Path -LiteralPath (Join-Path $Repo $retired[$name]))) {
            Add-Finding 'HK-G1' 'P2' '退役扩展缺退役说明' $retired[$name] '' '保留退役/适配通知文档'
        }
    }
}

function Invoke-HKG2 {
    # 临时文件
    $temp = Get-ChildItem -LiteralPath $Repo -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[/\\](\.git|obj|bin|node_modules)[/\\]' -and $_.Name -match '\.(orig|bak|tmp|rej)$' }
    foreach ($t in $temp) {
        Add-Finding 'HK-G2' 'P2' '临时/备份文件' (Get-RelPath $t.FullName) '' '删除或加入 .gitignore' -Ratchet
    }

    # 同名嵌套目录（X/X）
    foreach ($root in @($DocsRoot, $FrameworkRoot, $TestsRoot)) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        foreach ($d in (Get-ChildItem -LiteralPath $root -Directory -Recurse -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -notmatch '[/\\](obj|bin|\.git)[/\\]' })) {
            $child = Join-Path $d.FullName $d.Name
            if (Test-Path -LiteralPath $child -PathType Container) {
                Add-Finding 'HK-G2' 'P2' '同名嵌套目录（结构异常）' (Get-RelPath $child) '' '合并/移除重复层' -Ratchet
            }
        }
    }

    # 空目录（仅 docs/_Framework 顶层两层，避免噪音）
    foreach ($d in (Get-ChildItem -LiteralPath $DocsRoot -Directory -ErrorAction SilentlyContinue)) {
        $files = Get-ChildItem -LiteralPath $d.FullName -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $files) {
            Add-Finding 'HK-G2' 'P2' '空目录' (Get-RelPath $d.FullName) '' '删除或补内容' -Ratchet
        }
    }
}

function Invoke-HKG3 {
    $exempt = @('模板', '转告', '框架实战教学', '健康自检')
    foreach ($d in (Get-ChildItem -LiteralPath $DocsRoot -Directory -ErrorAction SilentlyContinue)) {
        if ($exempt -contains $d.Name) { continue }
        if ($d.Name -in @('KeyStore', 'SSO', 'UserCenter')) { continue }
        $guide = Get-ChildItem -LiteralPath $d.FullName -Filter '*使用指南*.md' -File -ErrorAction SilentlyContinue
        if (-not $guide) {
            Add-Finding 'HK-G3' 'INFO' 'docs 目录无使用指南（规划/设计类可忽略）' (Get-RelPath $d.FullName) '' '确认目录定位；若为扩展主文档则补指南'
        }
    }
    $log = Join-Path $DocsRoot '变更记录.md'
    if (Test-Path -LiteralPath $log) {
        $size = (Get-Item -LiteralPath $log).Length
        if ($size -gt 250KB) {
            Add-Finding 'HK-G3' 'INFO' '变更记录体量过大，建议索引化/分段' 'docs/变更记录.md' ("$([Math]::Round($size / 1KB)) KB") '保留最近 N 条 + 归档文件，降低上下文成本'
        }
    }
}

function Invoke-HKG4 {
    # 机制自身时效（Oracle R4）：报告 >14 天未更新，或早于最新 tag → 提示机制未按节奏执行
    if (-not (Test-Path -LiteralPath $OutDir)) { return }
    $reports = @(Get-ChildItem -LiteralPath $OutDir -Filter '健康自检报告-*.md' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
    if ($reports.Count -eq 0) {
        Add-Finding 'HK-G4' 'P2' '尚无自检报告（机制未启动）' (Get-RelPath $OutDir) '' '运行 pwsh build/build-ext-healthcheck.ps1'
        return
    }
    $latest = $reports[0]
    $ageDays = [Math]::Floor(((Get-Date) - $latest.LastWriteTime).TotalDays)
    if ($ageDays -gt 14) {
        Add-Finding 'HK-G4' 'P2' '最近一次自检已超过 14 天' (Get-RelPath $latest.FullName) "距今 $ageDays 天" '按机制 §5.2 节奏执行（迭代收尾 L1）'
    }
    $latestTagDateRaw = (& git -C $Repo for-each-ref --sort=-creatordate --count=1 --format='%(creatordate:short)' refs/tags 2>$null)
    if ($latestTagDateRaw) {
        try {
            $tagDate = [datetime]::Parse([string]$latestTagDateRaw)
            if ($latest.LastWriteTime.Date -lt $tagDate.Date) {
                Add-Finding 'HK-G4' 'P2' '最新 tag 之后未执行自检' (Get-RelPath $latest.FullName) "报告 $($latest.LastWriteTime.ToString('yyyy-MM-dd')) < 最新 tag $($tagDate.ToString('yyyy-MM-dd'))" '打 tag 后应补跑 L1 并提交报告'
            }
        }
        catch { }
    }
}

function Invoke-HKF1 {
    if (-not $WithBuild) { return }
    $out = & dotnet build $SlnxPath -c Release --nologo -v q 2>&1
    $code = $LASTEXITCODE
    if ($code -ne 0) {
        $tail = ($out | Select-Object -Last 25) -join "`n"
        Add-Finding 'HK-F1' 'P0' 'L0 编译门失败' 'dotnet build TKWF.Extensions.slnx -c Release' "exit=$code`n$tail" '修复编译错误后复跑'
    }
}

function Invoke-HKF2 {
    if (-not $WithTests) { return }
    $out = & dotnet test $SlnxPath -c Release --no-build --nologo -v q 2>&1
    $code = $LASTEXITCODE
    if ($code -ne 0) {
        $tail = ($out | Select-Object -Last 40) -join "`n"
        Add-Finding 'HK-F2' 'P0' '测试失败' 'dotnet test TKWF.Extensions.slnx -c Release' "exit=$code`n$tail" '按测试纪律排查（受影响面）'
    }
}

# ── 执行 ────────────────────────────────────────────────────────
$started = Get-Date
$l0Checks = @('Invoke-HKA1', 'Invoke-HKA2', 'Invoke-HKA3', 'Invoke-HKC1', 'Invoke-HKC2', 'Invoke-HKC4', 'Invoke-HKE1', 'Invoke-HKG2')
$l1Checks = $l0Checks + @('Invoke-HKA4', 'Invoke-HKA5', 'Invoke-HKA6', 'Invoke-HKA7', 'Invoke-HKB1', 'Invoke-HKB2', 'Invoke-HKB3', 'Invoke-HKB4',
    'Invoke-HKC3', 'Invoke-HKD1', 'Invoke-HKD2', 'Invoke-HKD3', 'Invoke-HKE2', 'Invoke-HKE3', 'Invoke-HKE4', 'Invoke-HKF3', 'Invoke-HKG1', 'Invoke-HKG3', 'Invoke-HKG4')
$checks = if ($Mode -eq 'L0') { $l0Checks } else { $l1Checks }
if ($WithBuild) { $checks += 'Invoke-HKF1' }
if ($WithTests) { $checks += 'Invoke-HKF2' }

$checkResults = [ordered]@{}
foreach ($fn in $checks) {
    try {
        $before = $script:Findings.Count
        & $fn
        $checkResults[$fn] = $script:Findings.Count - $before
    }
    catch {
        $id = $fn -replace '^Invoke-', '' -replace 'HK([A-Z])', 'HK-$1'
        Add-Finding $id 'P2' '检查项执行异常（脚本缺陷）' $fn $_.Exception.Message '修复脚本判据'
        $checkResults[$fn] = 1
    }
}

# ── 去重（同一 Id+目标+标题+证据只保留一条；Get-MdFiles/多路径扫描可能重复收集）──
$uniqSeen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$uniqList = [System.Collections.Generic.List[object]]::new()
foreach ($f in $script:Findings) {
    $uk = "$($f.Key)|$($f.Title)|$($f.Detail)"
    if ($uniqSeen.Add($uk)) { $uniqList.Add($f) }
}
$script:Findings = $uniqList

# ── 棘轮处理 ────────────────────────────────────────────────────
$ratchetFindings = @($script:Findings | Where-Object { $_.Ratchet })
# 稳定键：不含行号（行号随编辑漂移，会导致误判"新增"）；键 = 检查项 + 文件 + 规则类别/属性
foreach ($f in $ratchetFindings) {
    $pathOnly = $f.Target -replace ':\d+$', ''
    if ($f.Id -eq 'HK-A6') { $f.Key = "$($f.Id)|$pathOnly|$($f.Detail)" }
    elseif ($f.Id -eq 'HK-A5' -and $f.Title -match 'Store') {
        # Store 用"扩展目录+类型名"作键（文件移动/行号漂移不产生误报）
        $m = [regex]::Match($f.Detail, '(?:class|interface)\s+(I?\w+Store)')
        $extDir = ($pathOnly -split '/')[1]
        if ($m.Success) { $f.Key = "HK-A5|$extDir|$($m.Groups[1].Value)" }
        else { $f.Key = "HK-A5|$pathOnly|$($f.Title)" }
    }
    else { $f.Key = "$($f.Id)|$pathOnly|$($f.Title)" }
}
$baseline = $null
if (Test-Path -LiteralPath $BaselinePath) {
    try { $baseline = (Read-Text $BaselinePath) | ConvertFrom-Json } catch { $baseline = $null }
}

$baselineKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
if ($baseline -and $baseline.items) {
    foreach ($prop in $baseline.items.PSObject.Properties) {
        foreach ($k in @($prop.Value)) { [void]$baselineKeys.Add([string]$k) }
    }
}
$newRatchet = 0
foreach ($f in $ratchetFindings) {
    if (-not $baseline) { continue }
    if (-not $baselineKeys.Contains($f.Key)) {
        $f.IsNew = $true
        $newRatchet++
        if ($f.Severity -eq 'P2') { $f.Severity = 'P1' }   # 存量债务不阻塞；新增即升级应修
    }
}
$resolvedKeys = @()
if ($baseline) {
    $currentKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($f in $ratchetFindings) { [void]$currentKeys.Add($f.Key) }
    $resolvedKeys = @($baselineKeys | Where-Object { -not $currentKeys.Contains($_) })
}

if ($UpdateBaseline) {
    # Oracle R1：不静默吸收"本轮新发现"——必须先修复，或显式 -ForceBaseline 并留痕
    $absorbedNew = @($ratchetFindings | Where-Object { $_.IsNew })
    if ($baseline -and $absorbedNew.Count -gt 0 -and -not $ForceBaseline) {
        Write-Host "[ERR] 本次有 $($absorbedNew.Count) 条新增存量债务未修复，拒绝写入基线（防止棘轮自毁/吸收新债）。" -ForegroundColor Red
        $absorbedNew | Select-Object -First 20 | ForEach-Object { Write-Host "  [NEW] $($_.Id) $($_.Target) — $($_.Title)" -ForegroundColor Yellow }
        Write-Host '  如确认为误报/历史等价迁移，使用 -ForceBaseline 显式吸收并留痕。' -ForegroundColor Yellow
        exit 3
    }
    $items = [ordered]@{}
    foreach ($group in ($ratchetFindings | Group-Object Id | Sort-Object Name)) {
        $items[$group.Name] = @($group.Group | ForEach-Object { $_.Key } | Sort-Object -Unique)
    }
    $payload = [ordered]@{
        generatedAt = (Get-Date -Format 'yyyy-MM-dd')
        note        = '存量债务棘轮基线（A5/A6/G2）。只允许收紧：修复后重跑 -UpdateBaseline；新增条目应修复而非入基线。键不含行号（稳定）。生成：build/build-ext-healthcheck.ps1 -UpdateBaseline'
        counts      = @{}
        items       = $items
    }
    foreach ($k in $items.Keys) { $payload.counts[$k] = $items[$k].Count }
    $payload | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $BaselinePath -Encoding UTF8
    Write-Host "[baseline] 已写入 $BaselinePath" -ForegroundColor Yellow
    Write-Host ("           条目: " + (($items.Keys | ForEach-Object { "$_=$($items[$_].Count)" }) -join ', '))
}

# ── 汇总 ────────────────────────────────────────────────────────
$sevOrder = @('P0', 'P1', 'P2', 'INFO')
$counts = @{}
foreach ($s in $sevOrder) { $counts[$s] = @($script:Findings | Where-Object { $_.Severity -eq $s }).Count }

$gitHead = (git -C $Repo rev-parse --short HEAD 2>$null)
$branch = (git -C $Repo rev-parse --abbrev-ref HEAD 2>$null)
$now = Get-Date
$reportDate = $now.ToString('yyyy-MM-dd HH:mm')
$duration = [Math]::Round(((Get-Date) - $started).TotalSeconds, 1)

# 控制台摘要
Write-Host ''
Write-Host '=== TKWF.Extensions 健康自检 ===' -ForegroundColor Cyan
Write-Host "模式 $Mode | 范围 $(if ($ScopeList) { $Scope } else { '全仓' }) | 分支 $branch @ $gitHead | 耗时 ${duration}s"
foreach ($s in $sevOrder) {
    $color = switch ($s) { 'P0' { 'Red' } 'P1' { 'Yellow' } 'P2' { 'Gray' } default { 'DarkGray' } }
    Write-Host ("{0,-4}: {1}" -f $s, $counts[$s]) -ForegroundColor $color
}
if ($baseline) {
    Write-Host "棘轮: 基线 $($ratchetFindings.Count - $newRatchet) 条现存 / 新增 $newRatchet / 已消除 $($resolvedKeys.Count)" -ForegroundColor $(if ($newRatchet -gt 0) { 'Yellow' } else { 'Green' })
}
Write-Host '--- 发现（按级别）---'
foreach ($f in ($script:Findings | Sort-Object Severity, Id, Target)) {
    $color = switch ($f.Severity) { 'P0' { 'Red' } 'P1' { 'Yellow' } 'P2' { 'Gray' } default { 'DarkGray' } }
    $newMark = if ($f.IsNew) { ' [NEW]' } else { '' }
    Write-Host ("[{0}] {1}{2} {3} — {4}" -f $f.Severity, $f.Id, $newMark, $f.Target, $f.Title) -ForegroundColor $color
    if ($f.Detail) { Write-Host ("        " + ($f.Detail -replace "`n", ' ').Substring(0, [Math]::Min(160, ($f.Detail -replace "`n", ' ').Length))) -ForegroundColor DarkGray }
}

# ── 写报告 ──────────────────────────────────────────────────────
if (-not $NoWrite) {
    if (-not (Test-Path -LiteralPath $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }
    # 上期闭环率（读 history 最后一行 = 上一次运行）
    $prevHist = $null
    $histPreview = Join-Path $OutDir 'healthcheck-history.jsonl'
    if (Test-Path -LiteralPath $histPreview) {
        try { $lastLine = Get-Content -LiteralPath $histPreview -Tail 1; if ($lastLine) { $prevHist = $lastLine | ConvertFrom-Json } } catch { }
    }
    $currOpen = @($script:Findings | Where-Object { $_.Severity -in @('P0', 'P1') } | ForEach-Object { "$($_.Id):$($_.Target)" })
    $closureText = ''
    if ($prevHist -and $null -ne $prevHist.open) {
        $prevOpen = @($prevHist.open)
        $closed = @($prevOpen | Where-Object { $currOpen -notcontains $_ }).Count
        $rate = if ($prevOpen.Count -gt 0) { [Math]::Round(100 * $closed / $prevOpen.Count) } else { 100 }
        $closureText = if ($prevOpen.Count -eq 0) { "上期无未决 P0/P1" } else { "上期未决 P0/P1 $($prevOpen.Count) → 已闭环 $closed（$rate%）" }
    }
    # 范围化运行不覆盖全量报告（Oracle：审计轨迹保护）
    $scopeSuffix = if ($ScopeList) { '-scoped-' + (($ScopeList | ForEach-Object { $_ -replace '[^A-Za-z0-9._-]', '_' }) -join '_') } else { '' }
    $reportPath = Join-Path $OutDir ("健康自检报告-" + $now.ToString('yyyy-MM-dd') + $scopeSuffix + ".md")

    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine('# 扩展模块健康自检报告')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("> 生成：$reportDate | 模式 **$Mode** | 范围 **$(if ($ScopeList) { $Scope } else { '全仓' })** | 分支 ``$branch`` @ ``$gitHead`` | 耗时 ${duration}s")
    [void]$sb.AppendLine('> 机制文档：`docs/扩展模块健康自检与优化机制.md`；本报告由 `build/build-ext-healthcheck.ps1` 生成（只读巡检）。')
    $refsForReport = if ($env:TKWFDeployPath) { Join-Path $env:TKWFDeployPath 'build\refs' } else { Join-Path $Repo 'build\refs' }
    $refsState = if (Test-Path -LiteralPath $refsForReport) { '存在' } else { '缺失（E3/D2 受限）' }
    $feedState = if ($env:TKWFDeployPath -and (Test-Path -LiteralPath (Join-Path $env:TKWFDeployPath 'local-feed'))) { '存在' } else { '缺失/未设置' }
    [void]$sb.AppendLine("> 环境：TKWFDeployPath=$(if ($env:TKWFDeployPath) { '已设置' } else { '未设置' }) | refs=$refsState | local-feed=$feedState")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('## 摘要')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('| 级别 | 数量 | 含义 |')
    [void]$sb.AppendLine('|------|------|------|')
    [void]$sb.AppendLine("| P0 | $($counts['P0']) | 阻塞（构建/发布/契约/安全） |")
    [void]$sb.AppendLine("| P1 | $($counts['P1']) | 应修（一致性/消费方可见失同步） |")
    [void]$sb.AppendLine("| P2 | $($counts['P2']) | 卫生（可排期） |")
    [void]$sb.AppendLine("| INFO | $($counts['INFO']) | 观察 |")
    [void]$sb.AppendLine()
    if ($baseline -and $ratchetFindings.Count -gt 0) {
        [void]$sb.AppendLine("> 注：P2 中 $($ratchetFindings.Count - $newRatchet) 条为**基线内存量债务**（棘轮管理，不阻塞；新增才升级为 P1）。")
        [void]$sb.AppendLine()
    }
    if ($baseline) {
        [void]$sb.AppendLine("**棘轮基线**：现存 $($ratchetFindings.Count - $newRatchet) 条 / **新增 $newRatchet** / 已消除 $($resolvedKeys.Count)。基线文件 ``build/healthcheck-baseline.json``。")
        if ($closureText) { [void]$sb.AppendLine("**上期闭环**：$closureText") }
        [void]$sb.AppendLine()
    }
    else {
        [void]$sb.AppendLine('**棘轮基线**：未建立（存量债务全量列出）。建立：`pwsh build/build-ext-healthcheck.ps1 -UpdateBaseline`。')
        [void]$sb.AppendLine()
    }

    [void]$sb.AppendLine('## 检查项结果')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('| ID | 检查项 | 发现数 |')
    [void]$sb.AppendLine('|----|--------|-------:|')
    $executedIds = @{}
    foreach ($fn in $checks) {
        $cid = ($fn -replace '^Invoke-', '') -replace '^HK([A-Z])(\d)$', 'HK-$1$2'
        $executedIds[$cid] = $true
    }
    foreach ($c in $script:CheckCatalog) {
        $n = @($script:Findings | Where-Object { $_.Id -eq $c.Id }).Count
        $mark = if (-not $executedIds.ContainsKey($c.Id)) { 'SKIP' } elseif ($n -eq 0) { 'OK' } else { "$n" }
        [void]$sb.AppendLine("| $($c.Id) | $($c.Name) | $mark |")
    }
    [void]$sb.AppendLine()

    [void]$sb.AppendLine('## 明细')
    [void]$sb.AppendLine()
    foreach ($c in $script:CheckCatalog) {
        $items = @($script:Findings | Where-Object { $_.Id -eq $c.Id } | Sort-Object Severity, Target)
        if ($items.Count -eq 0) { continue }
        [void]$sb.AppendLine("### $($c.Id) $($c.Name)（$($items.Count)）")
        [void]$sb.AppendLine()
        # 信噪比（Oracle）：基线内既有债务折叠为一行；只展开新增/非棘轮项
        $expanded = @($items | Where-Object { -not ($_.Ratchet -and -not $_.IsNew) })
        $collapsed = $items.Count - $expanded.Count
        if ($collapsed -gt 0) {
            [void]$sb.AppendLine("- 存量债务 $collapsed 条（棘轮基线内，明细见 ``build/healthcheck-baseline.json``）")
        }
        $shown = 0
        foreach ($f in $expanded) {
            if ($shown -ge 40) { [void]$sb.AppendLine("- …（其余 $($expanded.Count - 40) 条见 healthcheck-latest.json）"); break }
            $sevMark = if ($f.IsNew) { "$($f.Severity)/NEW" } else { $f.Severity }
            [void]$sb.AppendLine("- **[$sevMark]** ``$($f.Target)`` — $($f.Title)")
            if ($f.Detail) { [void]$sb.AppendLine("  - 证据：$($f.Detail -replace "`n", ' / ')") }
            if ($f.Hint) { [void]$sb.AppendLine("  - 处置：$($f.Hint)") }
            $shown++
        }
        [void]$sb.AppendLine()
    }

    if ($resolvedKeys.Count -gt 0) {
        [void]$sb.AppendLine('## 基线可收紧（已消除）')
        [void]$sb.AppendLine()
        foreach ($k in ($resolvedKeys | Sort-Object | Select-Object -First 50)) { [void]$sb.AppendLine("- ``$k``") }
        [void]$sb.AppendLine()
        [void]$sb.AppendLine('> 确认修复后执行 `pwsh build/build-ext-healthcheck.ps1 -UpdateBaseline` 收紧。')
        [void]$sb.AppendLine()
    }

    [void]$sb.AppendLine('## 整改清单（人工填写）')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('| ID | 级别 | 文件:行/目标 | 问题 | 动作 | 依赖 | 状态 |')
    [void]$sb.AppendLine('|----|------|--------------|------|------|------|------|')
    foreach ($f in ($script:Findings | Where-Object { $_.Severity -in @('P0', 'P1') } | Sort-Object Severity, Id | Select-Object -First 60)) {
        $target = $f.Target -replace '\|', '/'
        $title = $f.Title -replace '\|', '/'
        [void]$sb.AppendLine("| $($f.Id) | $($f.Severity) | ``$target`` | $title | $($f.Hint -replace '\|', '/') | — | 未修复 |")
    }
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('---')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('> 闭环流程与时机见机制文档 §5/§6。P0 立即修；P1 本迭代内修；P2 入待办池。')

    Set-Content -LiteralPath $reportPath -Value $sb.ToString() -Encoding UTF8

    $jsonPath = Join-Path $OutDir 'healthcheck-latest.json'
    $jsonPayload = [ordered]@{
        generatedAt = $now.ToString('o')
        mode        = $Mode
        scope       = $ScopeList
        branch      = $branch
        commit      = $gitHead
        counts      = $counts
        ratchet     = [ordered]@{ newCount = $newRatchet; resolvedCount = $resolvedKeys.Count; total = $ratchetFindings.Count }
        findings    = @($script:Findings | ForEach-Object {
                [ordered]@{ id = $_.Id; severity = $_.Severity; target = $_.Target; title = $_.Title; detail = $_.Detail; hint = $_.Hint; isNew = $_.IsNew; ratchet = $_.Ratchet }
            })
    }
    $jsonPayload | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

    # 趋势最小工件（Oracle）：每次运行一行，供闭环率/误报率/检查项健康度统计
    $historyPath = Join-Path $OutDir 'healthcheck-history.jsonl'
    $openP0P1 = @($script:Findings | Where-Object { $_.Severity -in @('P0', 'P1') } | ForEach-Object { "$($_.Id):$($_.Target)" })
    $histLine = [ordered]@{
        date           = $now.ToString('yyyy-MM-dd HH:mm')
        mode           = $Mode
        scope          = $ScopeList
        commit         = $gitHead
        p0             = $counts['P0']
        p1             = $counts['P1']
        p2             = $counts['P2']
        info           = $counts['INFO']
        ratchetNew     = $newRatchet
        ratchetResolved = $resolvedKeys.Count
        open           = $openP0P1
    } | ConvertTo-Json -Compress
    Add-Content -LiteralPath $historyPath -Value $histLine -Encoding utf8

    Write-Host ''
    Write-Host "报告: $(Get-RelPath $reportPath)" -ForegroundColor Green
    Write-Host "摘要: $(Get-RelPath $jsonPath)" -ForegroundColor Green
}

# 退出码：P0=2；棘轮新增=1；否则 0（-UpdateBaseline 成功恒 0；基线被拒绝=3）
if ($UpdateBaseline) { exit 0 }
if ($counts['P0'] -gt 0) { exit 2 }
if ($newRatchet -gt 0) { exit 1 }
exit 0
