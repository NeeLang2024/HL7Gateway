@echo off
setlocal EnableExtensions
title PhilipsHifBridge Service Install Launcher

set "ROOT=%~dp0"
set "SCRIPT=%ROOT%tools\PhilipsHifBridge\install-service.bat"
set "BUILD_SCRIPT=%ROOT%tools\PhilipsHifBridge\build.bat"
set "BUILD_LOG=%ROOT%tools\PhilipsHifBridge\build.log"
set "EXE=%ROOT%tools\PhilipsHifBridge\bin\Release\PhilipsHifBridge.exe"
set "LOG=%ROOT%bridge-service-install-launcher.log"

echo ==== launcher %date% %time% ==== > "%LOG%"
echo ROOT=%ROOT% >> "%LOG%"
echo SCRIPT=%SCRIPT% >> "%LOG%"
echo EXE=%EXE% >> "%LOG%"
echo ARGS=%* >> "%LOG%"

if not exist "%SCRIPT%" goto no_script
if exist "%EXE%" goto do_install
if not exist "%BUILD_SCRIPT%" goto no_build_script

echo EXE not found, building first...
echo EXE not found, building first... >> "%LOG%"
pushd "%ROOT%tools\PhilipsHifBridge"
call "%BUILD_SCRIPT%"
set "BUILD_EXIT=%errorlevel%"
popd
echo Build exit code: %BUILD_EXIT% >> "%LOG%"
if exist "%BUILD_LOG%" (
    echo --- build.log tail --- >> "%LOG%"
    powershell -NoProfile -Command "Get-Content -Path '%BUILD_LOG%' -Tail 40" >> "%LOG%" 2>&1
)
if not "%BUILD_EXIT%"=="0" goto build_failed
if not exist "%EXE%" goto build_failed

:do_install
call "%SCRIPT%" %*
set "SVC_EXIT=%errorlevel%"
echo install-service exit code: %SVC_EXIT% >> "%LOG%"
if not "%SVC_EXIT%"=="0" (
    echo INSTALL FAILED - see:
    echo   %ROOT%tools\PhilipsHifBridge\install-service.log
    echo   %ROOT%tools\PhilipsHifBridge\bin\Release\service-start.log
    echo   %ROOT%tools\PhilipsHifBridge\bin\Release\bridge-fatal.log
)
if not defined HL7GATEWAY_NO_PAUSE pause
exit /b %SVC_EXIT%

:no_script
echo ERROR: Missing "%SCRIPT%" >> "%LOG%"
echo ERROR: Missing "%SCRIPT%"
if not defined HL7GATEWAY_NO_PAUSE pause
exit /b 1

:no_build_script
echo ERROR: Missing "%BUILD_SCRIPT%" >> "%LOG%"
if not defined HL7GATEWAY_NO_PAUSE pause
exit /b 1

:build_failed
echo ERROR: Build failed >> "%LOG%"
echo ERROR: Build failed. See %BUILD_LOG%
if exist "%BUILD_LOG%" type "%BUILD_LOG%"
if not defined HL7GATEWAY_NO_PAUSE pause
exit /b 1
