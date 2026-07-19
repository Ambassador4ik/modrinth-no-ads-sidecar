@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install Background Shortcut.ps1"
set "exit_code=%errorlevel%"
echo.
if not "%exit_code%"=="0" echo Installation failed with error %exit_code%.
pause
exit /b %exit_code%
