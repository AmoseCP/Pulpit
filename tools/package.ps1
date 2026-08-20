# Pulpit 打包:一次产出两个分发版本(先跑全部测试,再发布单文件 exe)。
#   publish\Pulpit-<版本>-portable.zip   —— 解压即用
#   publish\Pulpit-Setup-<版本>.exe      —— 安装版(按用户安装,无需管理员)
#
# 需要:.NET 8 SDK;Inno Setup 6(仅安装版需要,缺了会跳过并提示,Portable 照常产出)。
#   winget install --id JRSoftware.InnoSetup -e

$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent

# 版本号的唯一事实来源:csproj 的 <Version>
$csproj = "$repo\src\Pulpit.App\Pulpit.App.csproj"
$version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "Pulpit.App.csproj 里没有 <Version>" }
Write-Host "=== Pulpit $version 打包 ===" -ForegroundColor Cyan

Write-Host "--- 单元测试(不过不发布)---"
dotnet test "$repo\tests\Pulpit.Core.Tests" -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "测试未通过,停止打包" }

Write-Host "--- 发布单文件 exe ---"
dotnet publish "$repo\src\Pulpit.App\Pulpit.App.csproj" -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded `
    -o "$repo\publish\win-x64" --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "发布失败" }

Write-Host "--- Portable 版 ---"
$staging = "$repo\publish\portable-staging"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force "$staging" | Out-Null
Copy-Item "$repo\publish\win-x64\Pulpit.exe" $staging
Copy-Item "$repo\docs\快速上手卡.md" $staging
@"
Pulpit $version - Portable 版
==============================

双击 Pulpit.exe 即可使用,无需安装、无需 .NET 或任何运行库。
系统要求:Windows 10 x64(1607)或更新。

- 配置、经文库与日志在:%LOCALAPPDATA%\Pulpit\
  (首次运行自动创建;换电脑时把这个文件夹一并拷走即可保留设置)
- 操作说明见:快速上手卡.md

【首次双击提示「Windows 已保护你的电脑」怎么办】
这是 SmartScreen 对"从网络下载的未签名程序"的例行提示,不是病毒警告。
处理:点「更多信息」→「仍要运行」;或先右键 zip 文件 → 属性 →
勾选「解除锁定」→ 确定,再解压。U 盘拷贝的一般不会出现此提示。
"@ | Set-Content "$staging\说明.txt" -Encoding utf8
$zip = "$repo\publish\Pulpit-$version-portable.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$staging\*" -DestinationPath $zip
Remove-Item $staging -Recurse -Force

Write-Host "--- 安装版(Inno Setup)---"
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($iscc) {
    & $iscc /Q "/DMyAppVersion=$version" "$repo\installer\pulpit.iss"
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup 编译失败" }
} else {
    Write-Warning "未找到 Inno Setup 6,跳过安装版。安装:winget install --id JRSoftware.InnoSetup -e"
}

Write-Host ""
Write-Host "=== 产物 ===" -ForegroundColor Green
Get-ChildItem "$repo\publish" -File | Where-Object { $_.Name -match "portable|Setup" } |
    Select-Object Name, @{n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize
