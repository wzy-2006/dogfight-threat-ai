@echo off
chcp 65001 >nul
title 空战 - 客户端编译验证
cd /d "%~dp0"

echo.
echo   空战 · 客户端编译验证
echo   ============================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
  echo   [x] 需要 .NET 运行时才能调用 Roslyn 编译器。
  echo       装了 Unity / 团结引擎的话通常已经自带，若报错请先安装 .NET 6+ 运行时。
  echo.
  pause
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0verify-all.ps1" %*
set EXITCODE=%errorlevel%

echo.
if "%EXITCODE%"=="0" (
  echo   验证通过。
) else (
  echo   验证失败，退出码 %EXITCODE%
)
echo.
pause
exit /b %EXITCODE%
