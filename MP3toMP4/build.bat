@echo off
setlocal
cd /d "%~dp0"

rem ChilloutVR install path (same default as in MP3toMP4.csproj)
if "%CVRPATH%"=="" set "CVRPATH=D:\SteamLibrary\steamapps\common\ChilloutVR"
set "MODDLL=%CVRPATH%\Mods\MP3toMP4.dll"

echo Building MP3toMP4...
echo.
dotnet build -c Release -p:CVRPATH="%CVRPATH%"
if errorlevel 1 goto failed

if not exist "%MODDLL%" goto missing

echo.
echo ==================================================
echo  Done! MP3toMP4.dll is in:
echo  %CVRPATH%\Mods
echo ==================================================
goto end

:missing
echo.
echo Build finished, but MP3toMP4.dll was not found in:
echo %CVRPATH%\Mods
echo Please check the ChilloutVR path at the top of build.bat.
goto end

:failed
echo.
echo ==================================================
echo  Build FAILED, see the errors above.
echo  Tip: if ChilloutVR is running, close it first,
echo  because the game locks the mod DLL.
echo ==================================================

:end
echo.
pause
