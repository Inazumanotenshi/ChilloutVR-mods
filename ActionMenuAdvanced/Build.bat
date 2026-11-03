@echo off
setlocal EnableExtensions
title AdvActionMenu bauen
cd /d "%~dp0"

rem ===== Einstellungen =====
if "%CVRPATH%"=="" set "CVRPATH=D:\SteamLibrary\steamapps\common\ChilloutVR"
set "CONFIG=Release"
if /i "%~1"=="debug" set "CONFIG=Debug"
set "MODS=%CVRPATH%\Mods"
set "DLL=ActionMenuAdvanced\bin\%CONFIG%\AdvActionMenu.dll"

echo.
echo  AdvActionMenu  ^|  %CONFIG%
echo  ChilloutVR: %CVRPATH%
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
dotnet --list-sdks 2>nul | findstr /r "." >nul
if not errorlevel 1 (
    echo [INFO] Baue mit dotnet ...
    dotnet build "ActionMenuAdvanced.sln" -c %CONFIG% -p:CopyToMods=false -nologo -v:minimal
    goto :built
)

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo [FEHLER] Weder dotnet noch Visual Studio gefunden
    goto :fail
)
for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do set "MSBUILD=%%i"
if not defined MSBUILD (
    echo [FEHLER] MSBuild nicht gefunden
    goto :fail
)
echo [INFO] Baue mit MSBuild ...
"%MSBUILD%" "ActionMenuAdvanced.sln" -restore -p:Configuration=%CONFIG% -p:CopyToMods=false -nologo -v:minimal

:built
if errorlevel 1 (
    echo.
    echo [FEHLER] Build fehlgeschlagen, siehe Meldungen oben
    goto :fail
)
if not exist "%DLL%" (
    echo [FEHLER] Fertige DLL nicht gefunden: "%DLL%"
    goto :fail
)

rem ===== Alte DLL unter dem frueheren Namen entfernen =====
rem (Dakys ActionMenu.dll bleibt, AdvActionMenu fragt beim Start selbst, welches Menue genutzt wird)
if exist "%MODS%\ActionMenuAdvanced.dll" (
    del "%MODS%\ActionMenuAdvanced.dll"
    echo [INFO] Alte ActionMenuAdvanced.dll aus Mods entfernt
)

rem ===== In den Mods-Ordner kopieren =====
copy /y "%DLL%" "%MODS%\AdvActionMenu.dll" >nul
if errorlevel 1 (
    echo [FEHLER] Kopieren nach "%MODS%" fehlgeschlagen
    goto :fail
)

echo.
echo [OK] AdvActionMenu.dll liegt jetzt in "%MODS%"
echo.
if /i not "%~2"=="nopause" pause
exit /b 0

:fail
echo.
if /i not "%~2"=="nopause" pause
exit /b 1
