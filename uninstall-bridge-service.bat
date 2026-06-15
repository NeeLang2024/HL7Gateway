@echo off
setlocal
title PhilipsHifBridge Service Uninstall Launcher
set "ROOT=%~dp0"
set "SCRIPT=%ROOT%tools\PhilipsHifBridge\uninstall-service.bat"

if not exist "%SCRIPT%" (
    echo ERROR: Missing %SCRIPT%
    pause
    exit /b 1
)

call "%SCRIPT%" %*
exit /b %errorlevel%
