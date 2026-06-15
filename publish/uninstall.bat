@echo off
setlocal EnableExtensions
title HL7Gateway Uninstall (Service + WebApi)

set "DIR=%~dp0"
set "LOG=%DIR%uninstall.log"
set "SVC=HL7GatewayService"
set "WEB=HL7GatewayWebApi"
set "FAILED=0"

echo ==== HL7Gateway uninstall %date% %time% ==== > "%LOG%"
echo DIR=%DIR% >> "%LOG%"

net session >nul 2>&1
if %errorLevel% neq 0 (
    echo Need administrator. Requesting elevation...
    echo Need administrator >> "%LOG%"
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath 'cmd.exe' -ArgumentList '/k','\"\"%~f0\"\"' -Verb RunAs"
    exit /b 0
)

echo.
echo =====================================
echo  Uninstall middleware only
echo  (bridge: bridge\uninstall-bridge-service.bat)
echo  Log: %LOG%
echo =====================================
echo.

echo [1/2] Remove %WEB% ...
echo [1/2] Remove %WEB% >> "%LOG%"
call :RemoveService %WEB% HL7Gateway.WebApi.exe
if %errorLevel% neq 0 set "FAILED=1"

echo [2/2] Remove %SVC% ...
echo [2/2] Remove %SVC% >> "%LOG%"
call :RemoveService %SVC% HL7Gateway.Service.exe
if %errorLevel% neq 0 set "FAILED=1"

echo Remove firewall rules ...
netsh advfirewall firewall delete rule name="HL7Gateway Web (5002)" >> "%LOG%" 2>&1
netsh advfirewall firewall delete rule name="HL7Gateway MLLP (2575)" >> "%LOG%" 2>&1

sc query %WEB% >> "%LOG%" 2>&1
sc query %SVC% >> "%LOG%" 2>&1

sc query %WEB% >nul 2>&1
if %errorLevel% equ 0 set "FAILED=1"
sc query %SVC% >nul 2>&1
if %errorLevel% equ 0 set "FAILED=1"

echo.
if "%FAILED%"=="1" (
    echo =====================================
    echo  UNINSTALL FAILED - services still exist
    echo  Log: %LOG%
    echo.
    echo  Run force-clean.bat
    echo  Or reboot and run uninstall.bat again
    echo =====================================
    echo UNINSTALL FAILED >> "%LOG%"
    pause
    exit /b 1
)

echo =====================================
echo  Middleware services removed OK.
echo  Delete folder manually if needed.
echo  Bridge: bridge\uninstall-bridge-service.bat
echo  Log: %LOG%
echo =====================================
echo Uninstall finished OK >> "%LOG%"
pause
exit /b 0

:RemoveService
set "SNAME=%~1"
set "EXENAME=%~2"
set "WAITSEC=0"
sc query %SNAME% >nul 2>&1
if %errorLevel% neq 0 exit /b 0
echo   kill %EXENAME% ...
echo   kill %EXENAME% >> "%LOG%"
taskkill /F /IM %EXENAME% >> "%LOG%" 2>&1
timeout /t 2 /nobreak >nul
echo   stop %SNAME% ...
echo   stop %SNAME% >> "%LOG%"
net stop %SNAME% >> "%LOG%" 2>&1
timeout /t 3 /nobreak >nul
echo   delete %SNAME% ...
echo   delete %SNAME% >> "%LOG%"
sc delete %SNAME% >> "%LOG%" 2>&1
:WaitServiceGone
sc query %SNAME% >nul 2>&1
if %errorLevel% neq 0 exit /b 0
set /a WAITSEC+=3
if %WAITSEC% geq 90 goto remove_timeout
echo   waiting for %SNAME% to disappear (%WAITSEC%s) ...
echo   waiting %WAITSEC%s >> "%LOG%"
taskkill /F /IM %EXENAME% >> "%LOG%" 2>&1
timeout /t 3 /nobreak >nul
goto WaitServiceGone

:remove_timeout
echo ERROR: %SNAME% still exists after 90s
echo ERROR: remove timeout %SNAME% >> "%LOG%"
sc query %SNAME% >> "%LOG%" 2>&1
exit /b 1
