@echo off
setlocal
title PhilipsHifBridge Service Uninstaller

set "SERVICE_NAME=PhilipsHifBridge"
set "LOG=%~dp0uninstall-service.log"

echo ==== PhilipsHifBridge service uninstall %date% %time% ==== > "%LOG%"

net session >nul 2>&1
if %errorLevel% neq 0 (
    echo ERROR: Please run as Administrator.
    echo ERROR: Please run as Administrator. >> "%LOG%"
    pause
    exit /b 1
)

echo [1/2] Stopping service...
net stop %SERVICE_NAME% >> "%LOG%" 2>&1
timeout /t 2 /nobreak >nul

echo [2/2] Deleting service...
sc delete %SERVICE_NAME% >> "%LOG%" 2>&1

echo.
echo PhilipsHifBridge service removed.
echo Log: %LOG%
if not defined HL7GATEWAY_NO_PAUSE pause
exit /b 0
