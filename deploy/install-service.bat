@echo off
title HL7 Gateway 安装/卸载
echo HL7 集成网关 - 服务安装脚本
echo.
echo 推荐使用 PowerShell 脚本（功能更完整）:
echo   powershell -ExecutionPolicy Bypass .\install.ps1 Install
echo.
echo 快速命令（仅 Service，不含 WebApi）:
echo.
set SERVICE_NAME=HL7GatewayService
set PUBLISH_DIR=%~dp0..\src\HL7Gateway.Service\bin\Release\net9.0\publish
set EXE_PATH=%PUBLISH_DIR%\HL7Gateway.Service.exe

if "%1"=="install" goto install
if "%1"=="uninstall" goto uninstall
if "%1"=="start" goto start
if "%1"=="stop" goto stop
if "%1"=="restart" goto restart

echo 用法: %0 install^|uninstall^|start^|stop^|restart
echo.
echo    install   - 安装并启动 Service
echo    uninstall - 停止并卸载 Service
echo    start     - 启动 Service
echo    stop      - 停止 Service
echo    restart   - 重启 Service
goto end

:install
echo 安装服务 %SERVICE_NAME% ...
sc create %SERVICE_NAME% binPath="%EXE_PATH%" start=auto DisplayName="HL7 集成网关服务"
sc description %SERVICE_NAME% "HL7 设备数据接收、解析、存储及 ADT 转发服务"
sc failure %SERVICE_NAME% reset=86400 actions=restart/5000/restart/10000/restart/30000
sc start %SERVICE_NAME%
echo 服务安装完成。
goto end

:uninstall
echo 停止服务 %SERVICE_NAME% ...
sc stop %SERVICE_NAME% >nul 2>&1
timeout /t 3 /nobreak >nul
echo 卸载服务 %SERVICE_NAME% ...
sc delete %SERVICE_NAME%
echo 服务卸载完成。
goto end

:start
sc start %SERVICE_NAME%
goto end

:stop
sc stop %SERVICE_NAME%
goto end

:restart
sc stop %SERVICE_NAME% >nul 2>&1
timeout /t 3 /nobreak >nul
sc start %SERVICE_NAME%
goto end

:end
pause
