@echo off
setlocal EnableExtensions

set "APP_DIR=%~dp0"
if "%APP_DIR:~-1%"=="\" set "APP_DIR=%APP_DIR:~0,-1%"

set "RUN_KEY=HKCU\Software\Microsoft\Windows\CurrentVersion\Run"
set "RUN_VALUE_NAME=scancat"

call :resolve_app
if errorlevel 1 exit /b 1

if /I "%~1"=="install" goto install
if /I "%~1"=="uninstall" goto uninstall

reg query "%RUN_KEY%" /v "%RUN_VALUE_NAME%" >nul 2>nul
if not errorlevel 1 goto uninstall
goto install

:resolve_app
set "APP_EXE="

if exist "%APP_DIR%\scancat.exe" set "APP_EXE=%APP_DIR%\scancat.exe"
if not defined APP_EXE if exist "%APP_DIR%\scanshit.exe" set "APP_EXE=%APP_DIR%\scanshit.exe"

if not defined APP_EXE (
    echo Could not find scancat.exe or scanshit.exe next to this script.
    echo Place this script in the same folder as the published app.
    exit /b 1
)

exit /b 0

:install
reg add "%RUN_KEY%" /v "%RUN_VALUE_NAME%" /t REG_SZ /d "\"%APP_EXE%\"" /f >nul
if errorlevel 1 (
    echo Failed to create the startup registry entry.
    exit /b 1
)

echo Installed startup registry entry:
echo %RUN_KEY%\%RUN_VALUE_NAME%
echo.
echo App path:
echo %APP_EXE%
echo.
echo Run this script again to uninstall, or run install-startup.bat uninstall.
exit /b 0

:uninstall
reg delete "%RUN_KEY%" /v "%RUN_VALUE_NAME%" /f >nul 2>nul
reg query "%RUN_KEY%" /v "%RUN_VALUE_NAME%" >nul 2>nul
if not errorlevel 1 (
    echo Failed to remove the startup registry entry.
    exit /b 1
)

echo Removed startup registry entry.
exit /b 0