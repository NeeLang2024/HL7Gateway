@echo off
setlocal EnableExtensions
title PhilipsHifBridge Diagnose

set "ROOT=%~dp0"
set "BASE=%ROOT%tools\PhilipsHifBridge\bin\Release"
set "LOG=%ROOT%bridge-diagnose.log"
set "INSTALL_LOG=%ROOT%tools\PhilipsHifBridge\install-service.log"
set "DIAG=%ROOT%tools\PhilipsHifBridge\collect-diagnostics.ps1"

echo ==== diagnose %date% %time% ==== > "%LOG%"

echo.
echo Full report: %LOG%
echo.

if exist "%DIAG%" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%DIAG%" -LogPath "%LOG%" -BaseDir "%BASE%"
) else (
    echo collect-diagnostics.ps1 missing >> "%LOG%"
)

if exist "%INSTALL_LOG%" (
    echo. >> "%LOG%"
    echo ===== install-service.log ===== >> "%LOG%"
    type "%INSTALL_LOG%" >> "%LOG%"
)

type "%LOG%"
echo.
echo Saved: %LOG%
pause
