@echo off
setlocal EnableExtensions
title HL7Gateway Force Clean Services

set "DIR=%~dp0"
set "LOG=%DIR%force-clean.log"

echo ==== force-clean %date% %time% ==== > "%LOG%"

net session >nul 2>&1
if %errorLevel% neq 0 (
    echo Need administrator. Requesting elevation...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath 'cmd.exe' -ArgumentList '/k','\"\"%~f0\"\"' -Verb RunAs"
    exit /b 0
)

echo.
echo Force stop processes and remove services ...
echo Log: %LOG%
echo.

call :ForceRemove HL7GatewayWebApi HL7Gateway.WebApi.exe
call :ForceRemove HL7GatewayService HL7Gateway.Service.exe
call :ForceRemove PhilipsHifBridge PhilipsHifBridge.exe

set "FAILED=0"
sc query HL7GatewayWebApi >nul 2>&1
if %errorLevel% equ 0 set "FAILED=1"
sc query HL7GatewayService >nul 2>&1
if %errorLevel% equ 0 set "FAILED=1"

echo.
if "%FAILED%"=="1" (
    echo =====================================
    echo  STILL NOT GONE - reboot required
    echo  After reboot, run uninstall.bat or install.bat
    echo  Log: %LOG%
    echo =====================================
    echo STILL NOT GONE >> "%LOG%"
) else (
    echo =====================================
    echo  All HL7Gateway services cleared.
    echo  Log: %LOG%
    echo =====================================
    echo SUCCESS >> "%LOG%"
)
pause
exit /b 0

:ForceRemove
set "SNAME=%~1"
set "EXENAME=%~2"
set "WAITSEC=0"
echo === %SNAME% ===
echo === %SNAME% === >> "%LOG%"
taskkill /F /IM %EXENAME% >> "%LOG%" 2>&1
net stop %SNAME% >> "%LOG%" 2>&1
sc stop %SNAME% >> "%LOG%" 2>&1
timeout /t 2 /nobreak >nul
sc delete %SNAME% >> "%LOG%" 2>&1
:WaitGone
sc query %SNAME% >nul 2>&1
if %errorLevel% neq 0 (
    echo   %SNAME% removed
    echo   %SNAME% removed >> "%LOG%"
    exit /b 0
)
set /a WAITSEC+=5
if %WAITSEC% geq 120 goto StillThere
echo   waiting %WAITSEC%s ...
taskkill /F /IM %EXENAME% >> "%LOG%" 2>&1
timeout /t 5 /nobreak >nul
goto WaitGone
:StillThere
echo   %SNAME% STILL EXISTS (marked for deletion?)
echo   %SNAME% STILL EXISTS >> "%LOG%"
sc query %SNAME% >> "%LOG%" 2>&1
exit /b 0
