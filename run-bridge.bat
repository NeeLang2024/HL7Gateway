@echo off
setlocal
title PhilipsHifBridge Run Launcher
set "ROOT=%~dp0"
set "SCRIPT=%ROOT%tools\PhilipsHifBridge\run.bat"
set "LOG=%ROOT%bridge-run-launcher.log"
set "BRIDGE_IP=%~1"

echo ==== PhilipsHifBridge run launcher %date% %time% ==== > "%LOG%"
echo ROOT=%ROOT% >> "%LOG%"
echo SCRIPT=%SCRIPT% >> "%LOG%"
echo BRIDGE_IP=%BRIDGE_IP% >> "%LOG%"

if not exist "%SCRIPT%" goto missing_script

pushd "%ROOT%tools\PhilipsHifBridge"
call "%SCRIPT%" %BRIDGE_IP%
set "EXIT_CODE=%errorlevel%"
popd

echo.
echo Run launcher exited with code %EXIT_CODE%.
echo Run launcher exited with code %EXIT_CODE%. >> "%LOG%"
echo Inner run log:
echo %ROOT%tools\PhilipsHifBridge\run.log
echo Fatal log if any:
echo %ROOT%tools\PhilipsHifBridge\bin\Release\bridge-fatal.log
pause
exit /b %EXIT_CODE%

:missing_script
echo ERROR: Missing %SCRIPT%
echo ERROR: Missing %SCRIPT% >> "%LOG%"
echo.
echo Please unzip the whole PhilipsHifBridgeSource.zip before running this file.
pause
exit /b 1
