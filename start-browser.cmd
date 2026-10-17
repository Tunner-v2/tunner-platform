@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\dev\tunner-browser.ps1" start %*
if errorlevel 1 (echo Startup failed. Review the message above. & pause & exit /b 1)
echo Services keep running after this launcher closes. Use stop-browser.cmd to stop them.
pause
