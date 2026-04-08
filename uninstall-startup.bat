@echo off
setlocal

call "%~dp0install-startup.bat" uninstall
exit /b %errorlevel%