@echo off
setlocal
"%~dp0ModrinthNoAdsSidecar.exe" %*
set "exit_code=%errorlevel%"
if not "%exit_code%"=="0" (
	echo.
	echo The sidecar stopped with error %exit_code%.
	pause
)
exit /b %exit_code%
