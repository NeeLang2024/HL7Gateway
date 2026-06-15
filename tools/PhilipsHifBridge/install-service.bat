@echo off
setlocal EnableExtensions
title PhilipsHifBridge Service Installer

set "DIR=%~dp0"
set "EXE=%DIR%bin\Release\PhilipsHifBridge.exe"
set "CFG=%DIR%bin\Release\bridge-service.config"
set "LOG=%DIR%install-service.log"
set "DIAG=%DIR%collect-diagnostics.ps1"
set "SERVICE_NAME=PhilipsHifBridge"
set "BRIDGE_IP=%~1"
set "HTTP_PREFIX=%~2"

if "%HTTP_PREFIX%"=="" set "HTTP_PREFIX=http://localhost:5080/"

echo ==== PhilipsHifBridge service install %date% %time% ==== > "%LOG%"
echo DIR=%DIR% >> "%LOG%"
echo EXE=%EXE% >> "%LOG%"
echo CFG=%CFG% >> "%LOG%"
echo BRIDGE_IP=%BRIDGE_IP% >> "%LOG%"
echo HTTP_PREFIX=%HTTP_PREFIX% >> "%LOG%"
echo USER=%USERNAME% COMPUTER=%COMPUTERNAME% >> "%LOG%"

net session >nul 2>&1
if %errorLevel% neq 0 goto not_admin

if not exist "%EXE%" goto missing_exe

if "%BRIDGE_IP%"=="" (
    for /f "usebackq delims=" %%I in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -notlike '127.*' -and $_.PrefixOrigin -ne 'WellKnown' } | Sort-Object InterfaceMetric | Select-Object -First 1 -ExpandProperty IPAddress"`) do set "BRIDGE_IP=%%I"
)

if "%BRIDGE_IP%"=="" goto missing_ip

set "TCP_BASE=net.tcp://%BRIDGE_IP%:9912/"
echo TCP_BASE=%TCP_BASE% >> "%LOG%"

echo Bridge IP: %BRIDGE_IP%
echo TCP:       %TCP_BASE%
echo HTTP:      %HTTP_PREFIX%
echo Full log:  %LOG%
echo.

echo [1/6] Write bridge-service.config ...
(
echo tcp=%TCP_BASE%
echo http=%HTTP_PREFIX%
echo bridgeIp=%BRIDGE_IP%
) > "%CFG%"
echo --- bridge-service.config --- >> "%LOG%"
type "%CFG%" >> "%LOG%"

echo [2/6] Stop old service ...
taskkill /F /IM PhilipsHifBridge.exe >> "%LOG%" 2>&1
sc query %SERVICE_NAME% >nul 2>&1
if %errorLevel% equ 0 (
    echo --- net stop --- >> "%LOG%"
    net stop %SERVICE_NAME% >> "%LOG%" 2>&1
    timeout /t 3 /nobreak >nul
    echo --- sc delete --- >> "%LOG%"
    sc delete %SERVICE_NAME% >> "%LOG%" 2>&1
    timeout /t 5 /nobreak >nul
)

echo [3/6] Check ports ...
echo --- netstat 5080/9912 --- >> "%LOG%"
netstat -ano | findstr ":5080" >> "%LOG%" 2>&1
netstat -ano | findstr ":9912" >> "%LOG%" 2>&1
for /f "tokens=5" %%P in ('netstat -ano ^| findstr ":5080" ^| findstr "LISTENING"') do (
    echo WARNING: port 5080 in use PID %%P
    echo WARNING: port 5080 in use PID %%P >> "%LOG%"
)
for /f "tokens=5" %%P in ('netstat -ano ^| findstr ":9912" ^| findstr "LISTENING"') do (
    echo WARNING: port 9912 in use PID %%P
    echo WARNING: port 9912 in use PID %%P >> "%LOG%"
)

echo [4/6] Firewall + urlacl ...
netsh advfirewall firewall delete rule name="Philips HIF Bridge (9912)" >> "%LOG%" 2>&1
netsh advfirewall firewall delete rule name="PhilipsHifBridge HTTP (5080)" >> "%LOG%" 2>&1
netsh advfirewall firewall add rule name="Philips HIF Bridge (9912)" dir=in action=allow protocol=tcp localport=9912 >> "%LOG%" 2>&1
netsh advfirewall firewall add rule name="PhilipsHifBridge HTTP (5080)" dir=in action=allow protocol=tcp localport=5080 >> "%LOG%" 2>&1
netsh http add urlacl url=http://localhost:5080/ user=Everyone >> "%LOG%" 2>&1

echo [5/6] Create service ...
echo --- sc create --- >> "%LOG%"
echo binPath= "\"%EXE%\" --service" >> "%LOG%"
sc create %SERVICE_NAME% binPath= "\"%EXE%\" --service" start= auto DisplayName= "Philips HIF PPIS Bridge" >> "%LOG%" 2>&1
if %errorLevel% neq 0 goto create_failed
sc description %SERVICE_NAME% "Philips HIF/PPIS bridge for PIC iX inbound ADT" >> "%LOG%" 2>&1
sc failure %SERVICE_NAME% reset= 86400 actions= restart/5000/restart/10000/restart/30000 >> "%LOG%" 2>&1
echo --- sc qc after create --- >> "%LOG%"
sc qc %SERVICE_NAME% >> "%LOG%" 2>&1

echo [6/6] Start service ...
echo --- net start --- >> "%LOG%"
net start %SERVICE_NAME% >> "%LOG%" 2>&1
if %errorLevel% neq 0 goto start_failed
timeout /t 5 /nobreak >nul
echo --- sc query after start --- >> "%LOG%"
sc query %SERVICE_NAME% >> "%LOG%" 2>&1
echo --- HTTP probe --- >> "%LOG%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { $r=Invoke-WebRequest -UseBasicParsing -TimeoutSec 8 'http://localhost:5080/status'; Write-Output ('HTTP OK: ' + $r.Content); exit 0 } catch { Write-Output ('HTTP FAIL: ' + $_.Exception.Message); exit 1 }" >> "%LOG%" 2>&1
if %errorLevel% neq 0 goto http_failed

echo.
echo =====================================
echo  PhilipsHifBridge installed OK
echo  Log: %LOG%
echo =====================================
echo INSTALL OK >> "%LOG%"
if not defined HL7GATEWAY_NO_PAUSE pause
exit /b 0

:not_admin
echo ERROR: Please run as Administrator. >> "%LOG%"
echo ERROR: Please run as Administrator.
call :CollectDiagnostics
if not defined HL7GATEWAY_NO_PAUSE pause
exit /b 1

:missing_exe
echo ERROR: Missing %EXE% >> "%LOG%"
echo ERROR: Missing %EXE%. Run build-bridge.bat first.
call :CollectDiagnostics
if not defined HL7GATEWAY_NO_PAUSE pause
exit /b 1

:missing_ip
echo ERROR: Bridge IP required. Usage: install-bridge-service.bat 192.168.x.x >> "%LOG%"
echo ERROR: Bridge IP is required.
call :CollectDiagnostics
if not defined HL7GATEWAY_NO_PAUSE pause
exit /b 1

:create_failed
echo ERROR: sc create failed >> "%LOG%"
echo ERROR: Failed to create service.
call :CollectDiagnostics
if not defined HL7GATEWAY_NO_PAUSE pause
exit /b 1

:start_failed
echo ERROR: net start failed >> "%LOG%"
echo ERROR: Failed to start service.
echo.
echo Detailed logs written to:
echo   %LOG%
echo   %DIR%bin\Release\service-start.log
echo   %DIR%bin\Release\bridge-fatal.log
call :CollectDiagnostics
if not defined HL7GATEWAY_NO_PAUSE pause
exit /b 1

:http_failed
echo ERROR: HTTP status check failed after service start >> "%LOG%"
echo ERROR: Service started but http://localhost:5080/status unreachable.
call :CollectDiagnostics
if not defined HL7GATEWAY_NO_PAUSE pause
exit /b 1

:CollectDiagnostics
echo. >> "%LOG%"
echo ===== COLLECT DIAGNOSTICS %date% %time% ===== >> "%LOG%"
if exist "%DIAG%" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%DIAG%" -LogPath "%LOG%" -BaseDir "%DIR%bin\Release" >> "%LOG%" 2>&1
) else (
    echo collect-diagnostics.ps1 not found >> "%LOG%"
    sc qc %SERVICE_NAME% >> "%LOG%" 2>&1
    sc query %SERVICE_NAME% >> "%LOG%" 2>&1
    if exist "%DIR%bin\Release\service-start.log" type "%DIR%bin\Release\service-start.log" >> "%LOG%"
    if exist "%DIR%bin\Release\bridge-fatal.log" type "%DIR%bin\Release\bridge-fatal.log" >> "%LOG%"
)
echo.
echo --- Diagnostics appended to %LOG% ---
exit /b 0
