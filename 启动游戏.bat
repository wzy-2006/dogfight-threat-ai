@echo off
setlocal enabledelayedexpansion

rem ============================================================
rem  启动游戏 —— 用团结引擎 / Unity 打开本工程的 Unity 客户端
rem
rem  直接双击本文件即可。
rem  脚本只用相对路径，所以仓库放在哪里都能用。
rem
rem  只检测不启动（排查用）：先 set DOGFIGHT_DRY_RUN=1
rem ============================================================

set "PROJECT=%~dp0src\client\DogfightThreatAI"

if not exist "%PROJECT%\ProjectSettings\ProjectVersion.txt" goto :noproj

rem ---------------------------------------------------------- 查找编辑器
set "EDITOR="

call :try "C:\Program Files\Tuanjie 2022.3.62t16\Editor\Tuanjie.exe"
call :try "C:\Program Files\Tuanjie 2022.3.62t16\Editor\Unity.exe"

if not defined EDITOR call :scan "C:\Program Files" Tuanjie
if not defined EDITOR call :scan "C:\Program Files (x86)" Tuanjie
if not defined EDITOR call :scan "D:\" Tuanjie
if not defined EDITOR call :scan "E:\" Tuanjie

if not defined EDITOR call :scan "C:\Program Files" Unity
if not defined EDITOR call :scan "C:\Program Files (x86)" Unity
if not defined EDITOR call :scan "D:\" Unity
if not defined EDITOR call :scan "E:\" Unity

if not defined EDITOR goto :noeditor

rem ------------------------------------------------------------- 启动
echo.
echo   ============================================
echo      空战 · 实时威胁评估与动态决策  V1
echo   ============================================
echo.
echo   编辑器 : %EDITOR%
echo   工程   : %PROJECT%
echo.

if defined DOGFIGHT_DRY_RUN (
    echo   [干跑] 已找到编辑器，不启动。
    exit /b 0
)

echo   正在打开工程，请稍候…
echo.
echo   ------------------------------------------------------------
echo   打开之后还要做两步：
echo.
echo     1. 菜单栏点「空战」->「③ 一键搭建（资源 + 场景）」
echo        （只在第一次需要做，它会配好素材和场景）
echo.
echo     2. 按左上角的  Play  按钮，开始游戏
echo.
echo   ------------------------------------------------------------
echo   首次打开要导入 148 个素材，大约 1~3 分钟，属正常现象。
echo.
echo   W/S 油门 · A/D 转向 · 空格 机炮 · Shift 导弹 · F2 看 AI 威胁网格
echo.

start "" "%EDITOR%" -projectPath "%PROJECT%"
exit /b 0

rem ------------------------------------------------------------- 子过程
:try
if defined EDITOR exit /b 0
if exist %1 set "EDITOR=%~1"
exit /b 0

:scan
if defined EDITOR exit /b 0
for /d %%D in ("%~1\%~2*") do (
    if defined EDITOR goto :eof
    if exist "%%~fD\Editor\Tuanjie.exe" set "EDITOR=%%~fD\Editor\Tuanjie.exe"
    if exist "%%~fD\Editor\Unity.exe"   set "EDITOR=%%~fD\Editor\Unity.exe"
)
exit /b 0

rem ------------------------------------------------------------- 出错
:noproj
echo.
echo   [x] 没有找到 Unity 工程：
echo       %PROJECT%
echo.
echo   请确认本 .bat 文件放在仓库根目录（和 src 文件夹同级）。
echo.
pause
exit /b 1

:noeditor
echo.
echo   [x] 没有自动找到 Unity / 团结引擎编辑器。
echo.
echo   请手动打开：
echo     1. 开始菜单 -^> 「Tuanjie 1.10.4」-^> Tuanjie
echo     2. 在项目列表里点「添加 / 打开」，选择这个文件夹：
echo        %PROJECT%
echo.
pause
exit /b 1
