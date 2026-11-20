@echo off
chcp 65001 >nul
cd /d "%~dp0"

rem Rebuild GenshinAuto.exe (self-contained single file) into .\dist
rem Requires .NET 10 SDK. Run from this folder.

echo ============================================================
echo   Building GenshinAuto ... (source must stay UTF-8 with BOM)
echo ============================================================
echo.

dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=none ^
  -o dist

if errorlevel 1 (
    echo.
    echo BUILD FAILED
    pause
    exit /b 1
)

echo.
echo BUILD OK: %~dp0dist\GenshinAuto.exe
pause
