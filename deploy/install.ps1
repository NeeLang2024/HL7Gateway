<#
.HL7 集成网关 - Windows 安装/卸载脚本
用法:
  .\install.ps1 Install   发布 + 安装 + 启动
  .\install.ps1 Uninstall 停止 + 卸载 + 清理
  .\install.ps1 Start     启动服务
  .\install.ps1 Stop      停止服务
  .\install.ps1 Status    查看状态
#>

param([string]$Command = "Status")

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Resolve-Path "$ScriptDir\.."
$SvcName = "HL7GatewayService"
$WebName = "HL7GatewayWebApi"
$BridgeName = "PhilipsHifBridge"
$SvcDir  = "$Root\src\HL7Gateway.Service"
$WebDir  = "$Root\src\HL7Gateway.WebApi"
$BridgeDir = "$Root\tools\PhilipsHifBridge"
$PublishRoot = "$Root\publish"

function Write-Info  { Write-Host "[HL7GW]" -ForegroundColor Cyan -NoNewline; Write-Host " $args" }
function Write-Ok    { Write-Host "  ✓ " -ForegroundColor Green -NoNewline; Write-Host $args }
function Write-Err   { Write-Host "  ✗ " -ForegroundColor Red -NoNewline; Write-Host $args }

function Assert-Admin {
  $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
  $principal = New-Object Security.Principal.WindowsPrincipal $identity
  if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Err "需要管理员权限，请以管理员身份运行 PowerShell"
    exit 1
  }
}

function Build-Publish {
  Write-Info "发布后端..."
  dotnet publish "$SvcDir" -c Release -o "$PublishRoot\service" --nologo 2>&1 | Out-Null
  dotnet publish "$WebDir" -c Release -o "$PublishRoot\web" --nologo 2>&1 | Out-Null

  Write-Info "构建前端..."
  Push-Location "$Root\frontend"
  npm run build 2>&1 | Out-Null
  if (Test-Path "$PublishRoot\web\wwwroot\dist") { Remove-Item "$PublishRoot\web\wwwroot\dist" -Recurse -Force }
  Copy-Item "dist" "$PublishRoot\web\wwwroot\dist" -Recurse
  Pop-Location

  Write-Info "整理 Philips HIF/PPIS 桥接件..."
  if ($IsWindows -and (Test-Path "$BridgeDir\build.bat")) {
    Write-Info "构建 PhilipsHifBridge..."
    Push-Location $BridgeDir
    $env:HL7GATEWAY_NO_PAUSE = "1"
    cmd.exe /c build.bat
    $buildExit = $LASTEXITCODE
    Remove-Item Env:\HL7GATEWAY_NO_PAUSE -ErrorAction SilentlyContinue
    Pop-Location
    if ($buildExit -ne 0) {
      throw "PhilipsHifBridge build failed. See $BridgeDir\build.log"
    }
  } else {
    Write-Info "当前环境无法构建 .NET Framework 桥接件，发布包将包含源码和构建脚本"
  }

  $bridgePublish = "$PublishRoot\bridge"
  if (Test-Path $bridgePublish) { Remove-Item $bridgePublish -Recurse -Force }
  New-Item -ItemType Directory -Force -Path $bridgePublish | Out-Null
  Copy-Item "$Root\build-bridge.bat" $bridgePublish -Force
  Copy-Item "$Root\run-bridge.bat" $bridgePublish -Force
  Copy-Item "$Root\install-bridge-service.bat" $bridgePublish -Force
  Copy-Item "$Root\uninstall-bridge-service.bat" $bridgePublish -Force
  Copy-Item $BridgeDir "$bridgePublish\tools\PhilipsHifBridge" -Recurse -Force
  Get-ChildItem "$bridgePublish\tools\PhilipsHifBridge\README.md" -ErrorAction SilentlyContinue | Remove-Item -Force
  $dllList = "$Root\scripts\bridge-dll-new.list"
  if ((Test-Path $dllList) -and (Test-Path "$Root\dll_NEW")) {
    $dst = "$bridgePublish\dll_NEW"
    if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $dst | Out-Null
    Get-Content $dllList | ForEach-Object {
      $name = $_.Trim()
      if ($name -and -not $name.StartsWith("#")) {
        $src = "$Root\dll_NEW\$name"
        if (Test-Path $src) { Copy-Item $src $dst -Force }
      }
    }
  }
  Write-Ok "发布完成: $PublishRoot"
}

function Install-Service {
  Assert-Admin

  if (-not (Test-Path "$PublishRoot\service\HL7Gateway.Service.exe")) {
    Write-Info "未找到发布文件，先发布..."
    Build-Publish
  }
  if (-not (Test-Path "$PublishRoot\web\HL7Gateway.WebApi.exe")) {
    Write-Info "未找到发布文件，先发布..."
    Build-Publish
  }

  # 创建数据目录
  $dataDir = "$PublishRoot\data"
  New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
  $dbPath = "$dataDir\hl7gateway.db"

  # 修正 appsettings.json 中的 SQLite 路径为绝对路径（Windows Service 工作目录不是 exe 目录）
  foreach ($cfg in @("$PublishRoot\service\appsettings.json", "$PublishRoot\web\appsettings.json")) {
    if (Test-Path $cfg) {
      $json = Get-Content $cfg -Raw | ConvertFrom-Json
      $json.ConnectionStrings.Sqlite = "Data Source=$dbPath"
      $json | ConvertTo-Json -Depth 10 | Set-Content $cfg -Encoding UTF8
      Write-Ok "已修正 $cfg → $dbPath"
    }
  }

  $bridgeIp = ""
  try {
    $bridgeIp = (Get-NetIPAddress -AddressFamily IPv4 |
      Where-Object { $_.IPAddress -notlike "127.*" -and $_.PrefixOrigin -ne "WellKnown" } |
      Sort-Object InterfaceMetric |
      Select-Object -First 1 -ExpandProperty IPAddress)
  } catch {
    $bridgeIp = "localhost"
  }
  if ([string]::IsNullOrWhiteSpace($bridgeIp)) { $bridgeIp = "localhost" }

  # 1. 安装 Philips HIF/PPIS Bridge
  if (Test-Path "$PublishRoot\bridge\install-bridge-service.bat") {
    Write-Info "安装服务: $BridgeName ..."
    Push-Location "$PublishRoot\bridge"
    $env:HL7GATEWAY_NO_PAUSE = "1"
    cmd.exe /c install-bridge-service.bat $bridgeIp "http://localhost:5080/"
    $bridgeInstallExit = $LASTEXITCODE
    Remove-Item Env:\HL7GATEWAY_NO_PAUSE -ErrorAction SilentlyContinue
    Pop-Location
    if ($bridgeInstallExit -eq 0) {
      Write-Ok "服务 $BridgeName 已安装并启动"
    } else {
      Write-Err "服务 $BridgeName 安装失败，请查看 $PublishRoot\bridge\bridge-service-install-launcher.log"
    }
  }

  # 2. 安装 Service
  Write-Info "安装服务: $SvcName ..."
  sc.exe create $SvcName binPath="$PublishRoot\service\HL7Gateway.Service.exe" start=auto DisplayName="HL7 集成网关服务"
  sc.exe description $SvcName "HL7 设备数据接收、解析、存储及 ADT 转发服务"
  sc.exe failure $SvcName reset=86400 actions=restart/5000/restart/10000/restart/30000
  sc.exe start $SvcName
  Write-Ok "服务 $SvcName 已安装并启动"

  # 3. 安装 WebApi
  Write-Info "安装服务: $WebName ..."
  sc.exe create $WebName binPath="$PublishRoot\web\HL7Gateway.WebApi.exe" start=auto DisplayName="HL7 网关 Web 管理"
  sc.exe description $WebName "HL7 网关 Web API、管理界面及 WSI 通知服务"
  sc.exe failure $WebName reset=86400 actions=restart/5000/restart/10000/restart/30000
  sc.exe start $WebName
  Write-Ok "服务 $WebName 已安装并启动"

  Write-Ok "安装完成"
  Write-Info "管理界面: http://localhost:5002"
  Write-Info "MLLP 端口: 2575"
  Write-Info "Philips 桥接件: net.tcp://$bridgeIp`:9912/"
}

function Uninstall-Service {
  Assert-Admin

  # 1. 停止并删除 Service
  Write-Info "停止并卸载: $BridgeName ..."
  sc.exe stop $BridgeName 2>$null | Out-Null
  Start-Sleep -Seconds 2
  sc.exe delete $BridgeName 2>$null | Out-Null
  Write-Ok "已卸载 $BridgeName"

  Write-Info "停止并卸载: $SvcName ..."
  sc.exe stop $SvcName 2>$null | Out-Null
  Start-Sleep -Seconds 2
  sc.exe delete $SvcName 2>$null | Out-Null
  Write-Ok "已卸载 $SvcName"

  # 2. 停止并删除 WebApi
  Write-Info "停止并卸载: $WebName ..."
  sc.exe stop $WebName 2>$null | Out-Null
  Start-Sleep -Seconds 2
  sc.exe delete $WebName 2>$null | Out-Null
  Write-Ok "已卸载 $WebName"

  # 清理发布目录（可选）
  $confirm = Read-Host "是否删除发布文件 ($PublishRoot)？(y/N)"
  if ($confirm -eq "y") {
    if (Test-Path $PublishRoot) { Remove-Item $PublishRoot -Recurse -Force }
    Write-Ok "已删除发布文件"
  }

  Write-Ok "卸载完成"
}

function Start-Services {
  Assert-Admin
  Write-Info "启动服务..."
  sc.exe start $BridgeName 2>$null | Out-Null
  sc.exe start $SvcName 2>$null | Out-Null
  sc.exe start $WebName 2>$null | Out-Null
  Write-Ok "服务已启动"
}

function Stop-Services {
  Assert-Admin
  Write-Info "停止服务..."
  sc.exe stop $SvcName 2>$null | Out-Null
  sc.exe stop $WebName 2>$null | Out-Null
  sc.exe stop $BridgeName 2>$null | Out-Null
  Write-Ok "服务已停止"
}

function Show-Status {
  Write-Info "服务状态:"
  $services = @($SvcName, $WebName, $BridgeName)
  foreach ($name in $services) {
    $svc = Get-Service -Name $name -ErrorAction SilentlyContinue
    if ($svc) {
      $icon = if ($svc.Status -eq "Running") { "✓" } else { "✗" }
      Write-Host "  $icon $name ($($svc.Status))"
    } else {
      Write-Err "$name 未安装"
    }
  }
}

switch ($Command) {
  "Install"   { Install-Service }
  "Uninstall" { Uninstall-Service }
  "Start"     { Start-Services }
  "Stop"      { Stop-Services }
  "Status"    { Show-Status }
  default {
    Write-Host @"
HL7 集成网关 - Windows 服务安装脚本

用法: .\install.ps1 <command>

命令:
  Install     发布 + 安装 + 启动服务
  Uninstall   停止 + 卸载服务
  Start       启动服务
  Stop        停止服务
  Status      查看服务状态
"@
  }
}
