@echo off
setlocal
title PhilipsHifBridge Build Launcher
set "ROOT=%~dp0"
set "SCRIPT=%ROOT%tools\PhilipsHifBridge\build.bat"
set "LOG=%ROOT%bridge-build-launcher.log"

echo ==== PhilipsHifBridge build launcher %date% %time% ==== > "%LOG%"
echo ROOT=%ROOT% >> "%LOG%"
echo SCRIPT=%SCRIPT% >> "%LOG%"

if not exist "%SCRIPT%" goto missing_script

pushd "%ROOT%tools\PhilipsHifBridge"
call "%SCRIPT%"
set "EXIT_CODE=%errorlevel%"
popd

echo.
echo Build launcher exited with code %EXIT_CODE%.
echo Build launcher exited with code %EXIT_CODE%. >> "%LOG%"
echo Inner build log:
echo %ROOT%tools\PhilipsHifBridge\build.log
if not defined HL7GATEWAY_NO_PAUSE pause
exit /b %EXIT_CODE%

:missing_script
echo ERROR: Missing %SCRIPT%
echo ERROR: Missing %SCRIPT% >> "%LOG%"
echo.
echo Please unzip the whole PhilipsHifBridgeSource.zip before running this file.
if not defined HL7GATEWAY_NO_PAUSE pause
exit /b 1
