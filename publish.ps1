<#
.SYNOPSIS
发布 Apifox 助手桌面版。

.DESCRIPTION
默认生成自包含的单个 ApifoxHelper.exe：目标电脑不需要安装 .NET，双击即可使用。
加 -FrameworkDependent 生成体积小的版本，目标电脑需要安装 .NET 8（或更高）桌面运行时与 ASP.NET Core 运行时。
两种版本都需要 Microsoft Edge WebView2 运行时（Windows 10/11 通常已自带）。

.EXAMPLE
.\publish.ps1
.\publish.ps1 -FrameworkDependent -Output D:\release
#>
param(
    [string]$Output = (Join-Path $PSScriptRoot 'dist'),
    [switch]$FrameworkDependent
)
$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'ApifoxHelper.csproj'
$selfContained = -not $FrameworkDependent
$target = Join-Path $Output $(if ($selfContained) { 'win-x64' } else { 'win-x64-framework-dependent' })

$publishArgs = @(
    'publish', $project,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', $(if ($selfContained) { 'true' } else { 'false' }),
    '-p:PublishSingleFile=true',
    # WebView2Loader.dll 等本机库打进单文件，启动时自动解压。
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=none',
    '-p:AllowedReferenceRelatedFileExtensions=none',
    '-o', $target
)
if ($selfContained) { $publishArgs += '-p:EnableCompressionInSingleFile=true' }

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "发布失败（退出码 $LASTEXITCODE）" }

$exe = Join-Path $target 'ApifoxHelper.exe'
Write-Host ''
Write-Host "已生成：$exe（$([math]::Round((Get-Item $exe).Length / 1MB, 1)) MB）"
Get-ChildItem $target | Where-Object Name -ne 'ApifoxHelper.exe' | ForEach-Object { Write-Host "附带文件：$($_.Name)" }
