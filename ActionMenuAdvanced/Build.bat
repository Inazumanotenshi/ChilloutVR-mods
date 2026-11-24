@echo off
setlocal EnableExtensions
title Build AdvActionMenu
cd /d "%~dp0"

rem ======================================================================
rem  Settings
rem ======================================================================
if "%CVRPATH%"=="" set "CVRPATH=D:\SteamLibrary\steamapps\common\ChilloutVR"
rem Unity installation ("Managed" folder with UnityEngine.dll)
if "%UNITY_MANAGED%"=="" set "UNITY_MANAGED=D:\Unity\Versions\2022.3.58f1\Editor\Data\Managed"

set "CONFIG=Release"
if /i "%~1"=="debug" set "CONFIG=Debug"
set "MODS=%CVRPATH%\Mods"
set "DLL=ActionMenuAdvanced\bin\%CONFIG%\AdvActionMenu.dll"
set "UNITY_OUT=Editor\AdvActionMenu"

echo.
echo  AdvActionMenu  ^|  %CONFIG%
echo  ChilloutVR: %CVRPATH%
echo  Unity:      %UNITY_MANAGED%
echo.

if not exist "%MODS%" (
    echo [ERROR] Mods folder not found: "%MODS%"
    echo          Change the path at the top of Build.bat or set the CVRPATH environment variable
    goto :fail
)

rem ===== Is ChilloutVR still running? Then the DLL is locked =====
tasklist /FI "IMAGENAME eq ChilloutVR.exe" 2>nul | find /i "ChilloutVR.exe" >nul
if not errorlevel 1 (
    echo [ERROR] ChilloutVR is still running. Close it first, otherwise the DLL can't be replaced
    goto :fail
)

rem ===== Find a build tool: dotnet first, then MSBuild from Visual Studio =====
set "BUILDER="
dotnet --list-sdks 2>nul | findstr /r "." >nul
if not errorlevel 1 set "BUILDER=dotnet"
if defined BUILDER goto :havebuilder
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" goto :havebuilder
for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do set "MSBUILD=%%i"
if defined MSBUILD set "BUILDER=msbuild"
:havebuilder
if not defined BUILDER (
    echo [ERROR] Neither dotnet nor Visual Studio found
    goto :fail
)

rem ======================================================================
rem  1) Mod
rem ======================================================================
echo [INFO] Building the mod with %BUILDER% ...
call :build "ActionMenuAdvanced\ActionMenuAdvanced.csproj"
if errorlevel 1 (
    echo.
    echo [ERROR] Building the mod failed, see the messages above
    goto :fail
)
if not exist "%DLL%" (
    echo [ERROR] Built DLL not found: "%DLL%"
    goto :fail
)

rem Remove the old DLL with the previous name
rem (daky's ActionMenu.dll stays, AdvActionMenu asks on start which menu to use)
if exist "%MODS%\ActionMenuAdvanced.dll" (
    del "%MODS%\ActionMenuAdvanced.dll"
    echo [INFO] Removed old ActionMenuAdvanced.dll from Mods
)

copy /y "%DLL%" "%MODS%\AdvActionMenu.dll" >nul
if errorlevel 1 (
    echo [ERROR] Copying to "%MODS%" failed
    goto :fail
)
echo [OK] AdvActionMenu.dll is now in "%MODS%"
echo.

rem ======================================================================
rem  2) Unity generator (only if Unity is found)
rem ======================================================================
if not exist "%UNITY_MANAGED%\UnityEngine\UnityEngine.CoreModule.dll" (
    echo [NOTE] Unity not found at "%UNITY_MANAGED%", skipping the Unity generator
    goto :done
)

echo [INFO] Building the Unity generator ...
call :build "Unity\AdvActionMenu.Unity.Editor\AdvActionMenu.Unity.Editor.csproj" "-p:UnityManaged=%UNITY_MANAGED%"
if errorlevel 1 (
    echo.
    echo [ERROR] Building the Unity generator failed, see the messages above
    goto :fail
)
echo [OK] Unity generator is in "%~dp0%UNITY_OUT%"
echo      (drag the AdvActionMenu folder into Assets as it is)

:done
echo.
echo [OK] Done
echo.
if /i not "%~2"=="nopause" pause
exit /b 0

:fail
echo.
if /i not "%~2"=="nopause" pause
exit /b 1

rem ======================================================================
rem  Helpers
rem ======================================================================
:build
if "%BUILDER%"=="dotnet" (
    dotnet build %1 -c %CONFIG% -p:CopyToMods=false %~2 -nologo -v:minimal
) else (
    "%MSBUILD%" %1 -restore -p:Configuration=%CONFIG% -p:CopyToMods=false %~2 -nologo -v:minimal
)
exit /b %errorlevel%

