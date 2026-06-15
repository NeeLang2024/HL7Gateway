@echo off
setlocal EnableExtensions
title HL7Gateway Install (Service + WebApi)

set "DIR=%~dp0"
set "LOG=%DIR%install.log"
set "SVC=HL7GatewayService"
set "WEB=HL7GatewayWebApi"
set "SVC_EXE=%DIR%Service\HL7Gateway.Service.exe"
set "WEB_EXE=%DIR%WebApi\HL7Gateway.WebApi.exe"

echo ==== HL7Gateway install %date% %time% ==== > "%LOG%"
echo DIR=%DIR% >> "%LOG%"
echo SVC_EXE=%SVC_EXE% >> "%LOG%"
echo WEB_EXE=%WEB_EXE% >> "%LOG%"

net session >nul 2>&1
if %errorLevel% neq 0 (
    echo Need administrator. Requesting elevation...
    echo Need administrator >> "%LOG%"
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath 'cmd.exe' -ArgumentList '/k','\"\"%~f0\"\"' -Verb RunAs"
    exit /b 0
)

if not exist "%SVC_EXE%" goto missing_svc
if not exist "%WEB_EXE%" goto missing_web

echo.
echo =====================================
echo  HL7Gateway middleware install
echo  Service + WebApi (not bridge)
echo  Log: %LOG%
echo =====================================
echo.

echo [1/4] Install %SVC% ...
echo [1/4] Install %SVC% >> "%LOG%"
call :RemoveService %SVC% HL7Gateway.Service.exe
if %errorLevel% neq 0 goto remove_failed
sc create %SVC% binPath= "\"%SVC_EXE%\"" start= auto DisplayName= "HL7Gateway Service (MLLP/ADT)" >> "%LOG%" 2>&1
if %errorLevel% neq 0 goto create_failed
sc description %SVC% "HL7 Gateway - MLLP and ADT message processing" >> "%LOG%" 2>&1
sc failure %SVC% reset= 86400 actions= restart/5000/restart/10000/restart/30000 >> "%LOG%" 2>&1

echo [2/4] Install %WEB% ...
echo [2/4] Install %WEB% >> "%LOG%"
call :RemoveService %WEB% HL7Gateway.WebApi.exe
if %errorLevel% neq 0 goto remove_failed
sc create %WEB% binPath= "\"%WEB_EXE%\"" start= auto DisplayName= "HL7Gateway WebApi (Management UI)" >> "%LOG%" 2>&1
if %errorLevel% neq 0 goto create_failed
sc description %WEB% "HL7 Gateway - Web management interface" >> "%LOG%" 2>&1
sc failure %WEB% reset= 86400 actions= restart/5000/restart/10000/restart/30000 >> "%LOG%" 2>&1

echo [3/4] Firewall rules ...
echo [3/4] Firewall >> "%LOG%"
netsh advfirewall firewall delete rule name="HL7Gateway Web (5002)" >> "%LOG%" 2>&1
netsh advfirewall firewall delete rule name="HL7Gateway MLLP (2575)" >> "%LOG%" 2>&1
netsh advfirewall firewall add rule name="HL7Gateway Web (5002)" dir=in action=allow protocol=tcp localport=5002 >> "%LOG%" 2>&1
netsh advfirewall firewall add rule name="HL7Gateway MLLP (2575)" dir=in action=allow protocol=tcp localport=2575 >> "%LOG%" 2>&1

echo [4/4] Start services (WebApi first, then Service) ...
echo [4/4] Start services >> "%LOG%"
net start %WEB% >> "%LOG%" 2>&1
timeout /t 5 /nobreak >nul
sc query %WEB% >> "%LOG%" 2>&1
net start %SVC% >> "%LOG%" 2>&1
timeout /t 2 /nobreak >nul
sc query %SVC% >> "%LOG%" 2>&1

echo.
echo =====================================
echo  Install finished.
echo  Web UI: http://localhost:5002
echo  Login: admin / admin123
echo.
echo  Bridge (separate):
echo    cd bridge
echo    install-bridge-service.bat
echo.
echo  Log: %LOG%
echo =====================================
echo Install finished >> "%LOG%"
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
echo ERROR: %SNAME% still exists after delete (marked for deletion?)
echo ERROR: remove timeout %SNAME% >> "%LOG%"
sc query %SNAME% >> "%LOG%" 2>&1
echo.
echo Try: force-clean.bat
echo Or reboot, then run install.bat again.
exit /b 1

:missing_svc
echo ERROR: missing %SVC_EXE%
echo ERROR: missing %SVC_EXE% >> "%LOG%"
pause
exit /b 1

:missing_web
echo ERROR: missing %WEB_EXE%
echo ERROR: missing %WEB_EXE% >> "%LOG%"
pause
exit /b 1

:remove_failed
echo ERROR: could not remove old service. See log: %LOG%
echo ERROR: remove failed >> "%LOG%"
pause
exit /b 1

:create_failed
echo ERROR: sc create failed. See log: %LOG%
echo ERROR: sc create failed >> "%LOG%"
sc query %SVC% >> "%LOG%" 2>&1
sc query %WEB% >> "%LOG%" 2>&1
echo.
echo If error 1072 (marked for deletion), run force-clean.bat or reboot.
pause
exit /b 1
