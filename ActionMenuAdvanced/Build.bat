@echo off
setlocal EnableExtensions
title AdvActionMenu bauen
cd /d "%~dp0"

rem ======================================================================
rem  Einstellungen
rem ======================================================================
if "%CVRPATH%"=="" set "CVRPATH=D:\SteamLibrary\steamapps\common\ChilloutVR"
rem Unity-Installation (Ordner "Managed" mit UnityEngine.dll)
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
    echo [FEHLER] Mods-Ordner nicht gefunden: "%MODS%"
    echo          Pfad oben in der Build.bat oder Umgebungsvariable CVRPATH anpassen
    goto :fail
)

rem ===== Laeuft ChilloutVR noch? Dann ist die DLL gesperrt =====
tasklist /FI "IMAGENAME eq ChilloutVR.exe" 2>nul | find /i "ChilloutVR.exe" >nul
if not errorlevel 1 (
    echo [FEHLER] ChilloutVR laeuft noch. Bitte erst beenden, sonst kann die DLL nicht ersetzt werden
    goto :fail
)

rem ===== Build-Werkzeug suchen: erst dotnet, dann MSBuild aus Visual Studio =====
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
    echo [FEHLER] Weder dotnet noch Visual Studio gefunden
    goto :fail
)

rem ======================================================================
rem  1) Mod
rem ======================================================================
echo [INFO] Baue die Mod mit %BUILDER% ...
call :build "ActionMenuAdvanced\ActionMenuAdvanced.csproj"
if errorlevel 1 (
    echo.
    echo [FEHLER] Build der Mod fehlgeschlagen, siehe Meldungen oben
    goto :fail
)
if not exist "%DLL%" (
    echo [FEHLER] Fertige DLL nicht gefunden: "%DLL%"
    goto :fail
)

rem Alte DLL unter dem frueheren Namen entfernen
rem (Dakys ActionMenu.dll bleibt, AdvActionMenu fragt beim Start selbst, welches Menue genutzt wird)
if exist "%MODS%\ActionMenuAdvanced.dll" (
    del "%MODS%\ActionMenuAdvanced.dll"
    echo [INFO] Alte ActionMenuAdvanced.dll aus Mods entfernt
)

copy /y "%DLL%" "%MODS%\AdvActionMenu.dll" >nul
if errorlevel 1 (
    echo [FEHLER] Kopieren nach "%MODS%" fehlgeschlagen
    goto :fail
)
echo [OK] AdvActionMenu.dll liegt jetzt in "%MODS%"
echo.

rem ======================================================================
rem  2) Unity-Generator (nur wenn Unity gefunden wird)
rem ======================================================================
if not exist "%UNITY_MANAGED%\UnityEngine\UnityEngine.CoreModule.dll" (
    echo [HINWEIS] Unity nicht gefunden unter "%UNITY_MANAGED%", Unity-Generator wird uebersprungen
    goto :done
)

echo [INFO] Baue den Unity-Generator ...
call :build "Unity\AdvActionMenu.Unity.Editor\AdvActionMenu.Unity.Editor.csproj" "-p:UnityManaged=%UNITY_MANAGED%"
if errorlevel 1 (
    echo.
    echo [FEHLER] Build des Unity-Generators fehlgeschlagen, siehe Meldungen oben
    goto :fail
)
echo [OK] Unity-Generator liegt in "%~dp0%UNITY_OUT%"
echo      (Ordner AdvActionMenu so wie er ist in Assets ziehen)

:done
echo.
echo [OK] Fertig
echo.
if /i not "%~2"=="nopause" pause
exit /b 0

:fail
echo.
if /i not "%~2"=="nopause" pause
exit /b 1

rem ======================================================================
rem  Hilfsroutinen
rem ======================================================================
:build
if "%BUILDER%"=="dotnet" (
    dotnet build %1 -c %CONFIG% -p:CopyToMods=false %~2 -nologo -v:minimal
) else (
    "%MSBUILD%" %1 -restore -p:Configuration=%CONFIG% -p:CopyToMods=false %~2 -nologo -v:minimal
)
exit /b %errorlevel%

