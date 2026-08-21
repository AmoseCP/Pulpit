@echo off
setlocal
REM 本文件是 UTF-8 编码。默认 GBK 代码页会把多字节注释误读成命令
REM（曾把第 17 行半截注释当命令执行），所以先切 UTF-8 代码页再往下解析。
chcp 65001 >nul

REM Pulpit 单文件发布（M6）。需要 .NET 8 SDK，只能在 Windows x64 上跑。
REM 产物：publish\win-x64\Pulpit.exe —— 在未装 .NET 运行时的干净 Windows 上双击可用。

set OUT=publish\win-x64

echo === 清理 %OUT% ===
if exist "%OUT%" rmdir /s /q "%OUT%"

echo === 跑单元测试（M1/M2/M5 的 Core 验收）===
dotnet test tests\Pulpit.Core.Tests\Pulpit.Core.Tests.csproj -c Release || goto :failed

echo === 发布 ===
REM IncludeNativeLibrariesForSelfExtract：SQLitePCLRaw 带的 e_sqlite3.dll 是原生库，
REM 单文件打包时必须允许自解压，否则运行时找不到它。
dotnet publish src\Pulpit.App\Pulpit.App.csproj ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:DebugType=embedded ^
  -o "%OUT%" || goto :failed

echo.
echo === 完成 ===
dir /b "%OUT%"
echo.
echo 产物在 %OUT%\Pulpit.exe
echo 经文库已嵌在 exe 里，首次运行会解出到 %%LOCALAPPDATA%%\Pulpit\bible_cuv.db
goto :eof

:failed
echo.
echo *** 发布失败，见上面的错误 ***
exit /b 1
