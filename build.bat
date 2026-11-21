@echo off
chcp 65001 >nul
cd /d "%~dp0"

rem 重新构建到 .\dist（文件夹部署，不是单文件）
rem 需要 .NET 10 SDK。源码必须保持 UTF-8 带 BOM。

echo ============================================================
echo   Building BetterGI-AutoStart ...
echo ============================================================
echo.

dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=false ^
  -p:DebugType=none ^
  -o dist

if errorlevel 1 (
    echo.
    echo BUILD FAILED
    pause
    exit /b 1
)

echo.
echo BUILD OK: %~dp0dist\BetterGIAutoStart.exe
echo.
echo 下一步建议：
echo   dist\BetterGIAutoStart.exe --self-test
echo   dist\BetterGIAutoStart.exe --config
pause