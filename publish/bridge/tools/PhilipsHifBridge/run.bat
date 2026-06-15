@echo off
setlocal
title PhilipsHifBridge
set "DIR=%~dp0"
set "EXE=%DIR%bin\Release\PhilipsHifBridge.exe"
set "FATAL=%DIR%bin\Release\bridge-fatal.log"
set "LOG=%DIR%run.log"
set "BRIDGE_IP=%~1"

echo ==== PhilipsHifBridge run %date% %time% ==== > "%LOG%"
echo DIR=%DIR% >> "%LOG%"
echo EXE=%EXE% >> "%LOG%"
echo FATAL=%FATAL% >> "%LOG%"
if "%BRIDGE_IP%"=="" goto detect_ip
goto have_ip

:detect_ip
for /f "usebackq delims=" %%I in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -notlike '127.*' -and $_.PrefixOrigin -ne 'WellKnown' } | Sort-Object InterfaceMetric | Select-Object -First 1 -ExpandProperty IPAddress"`) do set "BRIDGE_IP=%%I"
if "%BRIDGE_IP%"=="" set "BRIDGE_IP=localhost"

:have_ip
echo BRIDGE_IP=%BRIDGE_IP%
echo BRIDGE_IP=%BRIDGE_IP% >> "%LOG%"
echo Checking runtime dependencies... >> "%LOG%"
if exist "%DIR%bin\Release\Philips.PlatformServices.dll" (
  echo FOUND Philips.PlatformServices.dll >> "%LOG%"
) else (
  echo MISSING Philips.PlatformServices.dll in bin\Release >> "%LOG%"
)
if exist "%FATAL%" del "%FATAL%"

if not exist "%EXE%" goto exe_missing

net session >nul 2>&1
if errorlevel 1 goto not_admin
goto add_firewall

:not_admin
echo WARNING: Not running as Administrator. Firewall rules may fail.
echo WARNING: Not running as Administrator. Firewall rules may fail. >> "%LOG%"
goto add_firewall

:add_firewall
netsh advfirewall firewall add rule name="PhilipsHifBridge NetTcp 9912" dir=in action=allow protocol=tcp localport=9912 >nul 2>&1
netsh advfirewall firewall add rule name="PhilipsHifBridge HTTP 5080" dir=in action=allow protocol=tcp localport=5080 >nul 2>&1

echo Starting bridge...
echo Starting bridge... >> "%LOG%"
"%EXE%" --tcp net.tcp://%BRIDGE_IP%:9912/ --http http://localhost:5080/
set "EXIT_CODE=%errorlevel%"

echo.
echo Bridge exited with code %EXIT_CODE%. See:
echo %LOG%
echo Bridge exited with code %EXIT_CODE%. >> "%LOG%"

if exist "%FATAL%" goto show_fatal
goto run_done

:show_fatal
echo.
echo ==== bridge-fatal.log ====
echo ==== bridge-fatal.log ==== >> "%LOG%"
type "%FATAL%"
type "%FATAL%" >> "%LOG%"

:run_done
pause
exit /b %EXIT_CODE%

:exe_missing
echo ERROR: %EXE% not found. Run build.bat first.
echo ERROR: %EXE% not found. Run build.bat first. >> "%LOG%"
echo.
pause
exit /b 1
