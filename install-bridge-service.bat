@echo off
setlocal EnableExtensions
title PhilipsHifBridge Service Install Launcher
rem 安装目录可能含空格或英文括号 ( ). 本脚本一律用 goto 流程, 不在 (...) 括号块里展开带路径的变量,
rem 也不依赖延迟变量展开(EnableDelayedExpansion), 退出码用普通 %errorlevel% 捕获, 避免跨环境失效.
set "ROOT=%~dp0"
set "SCRIPT=%ROOT%tools\PhilipsHifBridge\install-service.bat"
set "BUILD_SCRIPT=%ROOT%tools\PhilipsHifBridge\build.bat"
set "EXE=%ROOT%tools\PhilipsHifBridge\bin\Release\PhilipsHifBridge.exe"
set "LOG=%ROOT%bridge-service-install-launcher.log"

echo ==== PhilipsHifBridge service install launcher %date% %time% ==== > "%LOG%"
echo ROOT=%ROOT% >> "%LOG%"
echo SCRIPT=%SCRIPT% >> "%LOG%"
echo EXE=%EXE% >> "%LOG%"

if not exist "%SCRIPT%" goto no_script
if exist "%EXE%" goto do_install
if not exist "%BUILD_SCRIPT%" goto no_build_script

echo PhilipsHifBridge.exe not found, building first...
echo PhilipsHifBridge.exe not found, building first... >> "%LOG%"
pushd "%ROOT%tools\PhilipsHifBridge"
call "%BUILD_SCRIPT%"
set "BUILD_EXIT=%errorlevel%"
popd
echo Build exit code: %BUILD_EXIT% >> "%LOG%"
if not "%BUILD_EXIT%"=="0" goto build_failed
if not exist "%EXE%" goto build_failed

:do_install
call "%SCRIPT%" %*
set "SVC_EXIT=%errorlevel%"
echo install-service exit code: %SVC_EXIT% >> "%LOG%"
exit /b %SVC_EXIT%

:no_script
echo ERROR: Missing "%SCRIPT%"
echo ERROR: Missing "%SCRIPT%" >> "%LOG%"
if not defined HL7GATEWAY_NO_PAUSE pause
exit /b 1

:no_build_script
echo ERROR: Missing "%BUILD_SCRIPT%"
echo ERROR: Missing "%BUILD_SCRIPT%" >> "%LOG%"
if not defined HL7GATEWAY_NO_PAUSE pause
exit /b 1

:build_failed
echo ERROR: Bridge build failed. See:
echo "%ROOT%tools\PhilipsHifBridge\build.log"
echo ERROR: Bridge build failed. See bridge build.log >> "%LOG%"
if not defined HL7GATEWAY_NO_PAUSE pause
exit /b 1
