# 用途：真机不可得时的「降级答辩」一键复跑演示——Windows 侧（含 Git Bash / WSL / MSYS）的那一支。
#       与 demo.sh 逐段同名、同读数：同样按三档各跑一次冒烟、同样把 docs/NOTES.md 的逐屏表
#       一行一行读进来、同样逐档出一份 narration-<档位>.txt、同样在某一屏引用的段找不到时
#       非零退出并点名是哪一屏哪一段。输出段名（wine 前缀 / 注意 / 报告 / 报告没落盘 / 讲稿 /
#       出处 / 讲什么 / 引用段 / 结论口径 / 证据出处 / 解一次 / 手工等价 / 发的时候记的 /
#       当前源码算的 / 找不到逐屏表的出处 / 演示未收口 / 三档逐屏对账完成）与 demo.sh 对齐，
#       由 ApifoxHelper.Tests/WineCheckScriptsTests.DemoPs1_NamesTheSameSectionsAsDemoSh 判——
#       改一处就要改另一处：两份各讲各的讲稿，答辩现场念到的数字会对不上。
#
# 与 demo.sh 的两处**机制**差异（段名相同，做的事相同，在这一台上怎么做到不同，如实写在开头）：
#   1. 缩放档：demo.sh 在 Linux 容器里靠 `wine reg add ... LogPixels` 换档（虚拟屏，改档=改注册表）。
#      Windows 10/11 上那一档由系统显示设置决定，LogPixels 写了也不生效——本脚本不谎称自己改了缩放：
#      找得到 wine（WSL / Git Bash 里装了 wine）时照 demo.sh 的办法改档；找不到 wine 时按当前缩放各跑一次，
#      并在每一档的段头明写「本机未改缩放」，实际读数一律看报告里那一行「屏幕 DPI …」。
#   2. Xvfb：那是 Linux 的假显示器，Windows 上有真桌面，不叫 xvfb-run。
#
# 前置：Windows PowerShell 5.1 或 pwsh 7+；一份发布出来的冒烟器（默认 /tmp/ah-smoke-pub/ApifoxHelper.Smoke.exe，
#       也可指到 publish.ps1 的产物）。跑 wine 那一支时另需 wine（依赖与前置同 .winecheck/README.md）。
# 可覆盖的环境变量：WINEPREFIX / WINE_BIN / DISPLAY / SMOKE_EXE / REPO / NOTES / OUT / TIERS / PUBLISH。
param(
    [string]$SmokeExe = ${env:SMOKE_EXE},
    [string]$Notes = ${env:NOTES},
    [string]$Out = ${env:OUT},
    [string]$Tiers = ${env:TIERS},
    [string]$WineBin = ${env:WINE_BIN},
    [string]$Repo = ${env:REPO},
    [string]$WinePrefix = ${env:WINEPREFIX},
    [string]$Display = ${env:DISPLAY},
    [string]$Publish = ${env:PUBLISH}
)
$ErrorActionPreference = 'Stop'

$homeDir = if (${env:HOME}) { ${env:HOME} } elseif (${env:USERPROFILE}) { ${env:USERPROFILE} } else { (Get-Location).Path }
if (-not $Repo) { $Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path }
if (-not $Notes) { $Notes = Join-Path $Repo 'docs/NOTES.md' }
if (-not $SmokeExe) { $SmokeExe = '/tmp/ah-smoke-pub/ApifoxHelper.Smoke.exe' }
if (-not $Out) { $Out = '/tmp/ah-demo' }
if (-not $Tiers) { $Tiers = '96 120 192' }
if (-not $Publish) { $Publish = '0' }
if (-not $WinePrefix) { $WinePrefix = Join-Path $homeDir '.wine-ah' }
if (-not $Display) { $Display = ':99' }
${env:WINEPREFIX} = $WinePrefix
${env:DISPLAY} = $Display
if (-not ${env:WINEDEBUG}) { ${env:WINEDEBUG} = '-all' }
if (-not $WineBin) { $WineBin = (Get-Command wine -ErrorAction SilentlyContinue).Source }
$useWine = -not [string]::IsNullOrWhiteSpace($WineBin)
New-Item -ItemType Directory -Force -Path $Out | Out-Null

if (-not (Test-Path -LiteralPath $Notes)) { Write-Host "找不到逐屏表的出处：$Notes"; exit 1 }

# 第一百六十六轮实测：误用默认前缀时每一扇窗口都构造不出来（20 通过 / 115 失败，
# 全是「Error creating window handle」）。那是环境差异，把它读成产品回归会带偏一整场答辩。
Write-Host "wine 前缀：$WinePrefix"
if ($WinePrefix -like '*.wine') {
    Write-Host '  注意：这是默认前缀，通常没有可用的桌面环境，报告里会整片「Error creating window handle」。'
}

$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
function Write-Lines([string]$path, [string[]]$lines) {
    # 与 demo.sh 同一份字节口径：UTF-8 无 BOM、每行一个换行。讲稿是给人念的，不该带出一层编码噪声。
    [System.IO.File]::AppendAllText($path, (($lines -join "`n") + "`n"), $Utf8NoBom)
}

function Get-Sha1Hex([byte[]]$bytes) {
    $sha = [System.Security.Cryptography.SHA1]::Create()
    try { (($sha.ComputeHash($bytes) | ForEach-Object { $_.ToString('x2') }) -join '') }
    finally { $sha.Dispose() }
}

# 念出来的数必须出自当前源码。算式与 demo.sh 的 source_fingerprint 是同一条：排掉 bin 与 obj、
# 排掉测试工程（它不编进这份产物，改了它不该要求重发冒烟器）、按路径排序、逐文件 sha1、
# 再对「<sha1>  <路径>」拼起来的那份清单整体 sha1。跨平台时路径写法不同，判出的就是「不属于当前代码」——
# 那正是这一条要的保守方向：宁可要求重发一次，也不把上一版的读数念成当前的。
function Get-SourceFingerprint {
    $tests = (Join-Path $Repo 'ApifoxHelper.Tests').Replace('\', '/')
    $files = @(Get-ChildItem -LiteralPath $Repo -Recurse -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        Where-Object { -not $_.FullName.Replace('\', '/').StartsWith($tests) } |
        Sort-Object { $_.FullName } |
        ForEach-Object { $_.FullName.Replace('\', '/') })
    $lines = @($files | ForEach-Object { (Get-Sha1Hex ([System.IO.File]::ReadAllBytes($_))) + '  ' + $_ })
    return (Get-Sha1Hex ([System.Text.Encoding]::UTF8.GetBytes(($lines -join "`n") + "`n")))
}

$SmokeDir = Split-Path -Parent $SmokeExe
$Stamp = Join-Path $SmokeDir 'smoke-source.sha'
$PublishCmd = "dotnet build $Repo/ApifoxHelper.Smoke/ApifoxHelper.Smoke.csproj -c Release -r win-x64 -t:Rebuild; dotnet publish $Repo/ApifoxHelper.Smoke/ApifoxHelper.Smoke.csproj -c Release -r win-x64 --self-contained -o $SmokeDir; 然后把 Get-SourceFingerprint 的结果写进 $Stamp"

if ($Publish -eq '1') {
    Write-Host '重发冒烟器（PUBLISH=1，强制重建）：先 build -t:Rebuild，再 publish，最后记源码指纹。'
    & dotnet build "$Repo/ApifoxHelper.Smoke/ApifoxHelper.Smoke.csproj" -c Release -r win-x64 -t:Rebuild
    if ($LASTEXITCODE -ne 0) { exit 1 }
    & dotnet publish "$Repo/ApifoxHelper.Smoke/ApifoxHelper.Smoke.csproj" -c Release -r win-x64 --self-contained -o "$SmokeDir"
    if ($LASTEXITCODE -ne 0) { exit 1 }
    try { Set-Content -LiteralPath $Stamp -Value (Get-SourceFingerprint) }
    catch { Write-Host "源码指纹写不进 $Stamp（产物目录不可写？）"; exit 1 }
}
elseif (-not (Test-Path -LiteralPath $SmokeExe)) {
    Write-Host "冒烟器不在 $SmokeExe。设 PUBLISH=1 让本脚本发，或按 .winecheck/README.md 先发布。"
    exit 1
}
elseif (-not (Test-Path -LiteralPath $Stamp)) {
    Write-Host "产物旁边没有源码指纹（$Stamp）——无法判断这一档出自哪一版源码，别拿它念当前结论。"
    Write-Host '解一次：PUBLISH=1 powershell -File .winecheck/demo.ps1'
    exit 1
}
elseif ((Get-SourceFingerprint) -ne (Get-Content -LiteralPath $Stamp -Raw).Trim()) {
    Write-Host '冒烟器不是当前源码发出来的（源码内容指纹与产物旁边那份记的不一致）。'
    Write-Host "  发的时候记的：$((Get-Content -LiteralPath $Stamp -Raw).Trim())"
    Write-Host "  当前源码算的：$(Get-SourceFingerprint)"
    Write-Host '这一档的通过数属于上一次那版代码。解法（脚本自己就会做这三步）：'
    Write-Host '  PUBLISH=1 powershell -File .winecheck/demo.ps1'
    Write-Host "  手工等价：$PublishCmd"
    exit 1
}

# 逐屏表 → 五列（# / 屏 / 讲什么 / 证据出处 / 结论口径）。取法与 demo.sh 同一条：只认
# 「## 降级答辩脚本」这一节里以「| 数字」开头的行，遇到下一个 ## 收尾。
# 列数不对的行直接判错：少一列的行「看着是一行」，静默跳过的话这一屏就不会出现在讲稿里。
$rows = @()
$inTable = $false
foreach ($line in (Get-Content -LiteralPath $Notes)) {
    if ($line -like '## 降级答辩脚本*') { $inTable = $true; continue }
    if ($inTable -and $line -like '## *') { break }
    if (-not ($inTable -and $line -match '^\| \d')) { continue }
    $cells = @($line.Trim().Trim('|').Split('|') | ForEach-Object { $_.Trim() })
    if ($cells.Count -ne 5) {
        Write-Host "ERR 逐屏表有一行不是 5 列（实际 $($cells.Count) 列）：$line"
        exit 1
    }
    $rows += ,$cells
}

if ($rows.Count -eq 0) {
    Write-Host "$Notes 里没有「## 降级答辩脚本」的逐屏表（讲稿没有屏单可依据）。"
    exit 1
}

# 某一段在该档报告里的通过 / 失败条数：段标题带 [RunXxx]，报告里没有这一段就是 0 0。
function Get-SegmentStat([string]$reportPath, [string]$id) {
    $pass = 0
    $fail = 0
    $on = $false
    foreach ($line in (Get-Content -LiteralPath $reportPath)) {
        if ($line -like '== *') { $on = $line.Contains("[$id]"); continue }
        if (-not $on) { continue }
        if ($line.StartsWith('PASS ')) { $pass++ }
        elseif ($line.StartsWith('FAIL')) { $fail++ }
    }
    return @($pass, $fail)
}

$totalFail = 0
$totalMissing = 0
foreach ($tier in @($Tiers -split ' ' | Where-Object { $_ })) {
    $report = Join-Path $Out "report-$tier.txt"
    $narration = Join-Path $Out "narration-$tier.txt"
    $console = Join-Path $Out "console-$tier.log"
    $percent = [int](100 * [int]$tier / 96)
    if ($useWine) {
        Write-Host "== 档位 LogPixels=$tier（≈${percent}%）=="
    }
    else {
        Write-Host "== 档位 LogPixels=$tier（≈${percent}%，本机未改缩放，实际那一档看报告里的「屏幕 DPI」）=="
    }
    if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report -Force }
    # 改 LogPixels 与跑冒烟必须落在同一个活显示里（第一百八十四轮实测修）：注册表写入与 GUI 进程共用
    # 同一个常驻 wineserver，在死显示里先写注册表会把它的显示状态带坏，之后每一扇窗口都构造不出来。
    # 那不是产品回归，是环境差异：把它读成回归会带偏一整场答辩（同 README 记的前缀误用那一族）。
    if ($useWine) {
        & $WineBin reg add 'HKCU\Control Panel\Desktop' /v LogPixels /t REG_DWORD /d $tier -f *> $null
        if ($LASTEXITCODE -ne 0) {
            Write-Host '  这一档的 LogPixels 写不进去（注册表写入失败），这一档没量成。'
            $totalMissing++
            continue
        }
        & $WineBin $SmokeExe "--report=$report" *> $console
    }
    else {
        & $SmokeExe "--report=$report" *> $console
    }
    $runRc = $LASTEXITCODE
    if (-not (Test-Path -LiteralPath $report)) {
        Write-Host "  报告没落盘：$report（冒烟器退出码 $runRc，见 $console）"
        $totalMissing++
        continue
    }

    $reportLines = @(Get-Content -LiteralPath $report)
    $failLines = @($reportLines | Where-Object { $_.StartsWith('FAIL ') }).Count
    $passLines = @($reportLines | Where-Object { $_.StartsWith('PASS ') }).Count
    Write-Host "  报告：$passLines 通过 / $failLines 失败（退出码 $runRc）→ $report"

    if ($useWine) {
        $caliber = '以上全部是 Linux 容器内 wine 10.0 + Xvfb 模拟器证据，不是 Windows 真机结果。'
    }
    else {
        $caliber = '以上读数出自本机跑冒烟器（Windows 或 WSL / Git Bash 里的 wine），三档 DPI 的截图归档与人的观感复核仍是缺的，勾不了 G-D5。'
    }
    Write-Lines $narration @(
        "降级答辩讲稿 · LogPixels=$tier（≈${percent}%）",
        "出处：$Notes 的「降级答辩脚本」逐屏表；数字来自 $report。",
        $caliber,
        ''
    )

    $tierFail = 0
    $tierMissing = 0
    foreach ($cells in $rows) {
        $num = $cells[0]
        $screen = $cells[1]
        $what = $cells[2]
        $evidence = $cells[3]
        $verdict = $cells[4]
        if (-not $screen) { continue }
        $segs = @([regex]::Matches($evidence, 'Run[A-Za-z0-9]+') | ForEach-Object { $_.Value } | Sort-Object -Unique)
        Write-Lines $narration @("$num. $screen", "   讲什么：$what")
        if ($segs.Count -eq 0) {
            # 这一屏没有冒烟段可指：只有源码与编译证据，讲的时候不演示界面、不拿别的屏冒充。
            Write-Lines $narration @('   证据出处：这一屏没有 wine 冒烟段——只有源码与编译证据，说「代码与用例在，真实端到端未跑」')
        }
        else {
            foreach ($seg in $segs) {
                $stat = Get-SegmentStat $report $seg
                if ($stat[0] -eq 0 -and $stat[1] -eq 0) {
                    Write-Lines $narration @("   引用段：$seg —— 报告里没有这一段（逐屏表与冒烟器对不上，先修这一处再答辩）")
                    $tierMissing++
                }
                else {
                    Write-Lines $narration @("   引用段：$seg —— 通过 $($stat[0]) / 失败 $($stat[1])")
                    $tierFail += [int]$stat[1]
                }
            }
        }
        Write-Lines $narration @("   结论口径：$verdict", '')
    }
    Write-Host "  讲稿：$narration（本档引用段合计失败 $tierFail 条、对不上 $tierMissing 处）"
    $totalFail += $tierFail
    $totalMissing += $tierMissing
}

# 把缩放改回 100%，别把下一个用同一前缀的人留在 200% 档。这一步同样要在活显示里写（原因见上面那段）。
if ($useWine) {
    & $WineBin reg add 'HKCU\Control Panel\Desktop' /v LogPixels /t REG_DWORD /d 96 -f *> $null
}

Write-Host @'
录制口径（答辩现场，全文见 .winecheck/README.md「演示与录制口径」）：
  1. 三档各录一段，起点是脚本打出的「档位 LogPixels=…」那一行；跑 wine 那一支时屏幕固定 1600×1000（Xvfb）；
  2. 讲序就是 narration-<档位>.txt 的行序（第 1 屏到最后一屏），每屏念「屏 / 讲什么 / 通过几条」；
  3. 结论口径写「源码 + 编译证据」的那几屏不演示界面，只说「代码与用例在，真实端到端未跑」；
  4. 结尾固定一句：把本脚本打出的那一句口径原样念一遍（跑 wine 那一支是「Linux 容器内 wine 模拟器证据，
     不是 Windows 真机结果」；本机那一支是「出自本机跑冒烟器，三档 DPI 截图归档与观感复核仍缺」）；
     真机 0 台，G-D5 未达成（环境供给缺口），到期日 2026-11-20。
'@

if ($totalFail -gt 0 -or $totalMissing -gt 0) {
    Write-Host "演示未收口：三档累计引用段失败 $totalFail 条、逐屏表与报告对不上 $totalMissing 处——按讲稿逐条查到底层再答辩，不带着失败行念「wine 量到」。"
    exit 1
}
Write-Host "三档逐屏对账完成：每一屏引用的段都在报告里、累计 0 失败。讲稿见 $Out/narration-*.txt。"
