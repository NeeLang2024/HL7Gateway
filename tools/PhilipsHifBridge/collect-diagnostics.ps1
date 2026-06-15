param(
    [Parameter(Mandatory = $true)][string]$LogPath,
    [Parameter(Mandatory = $true)][string]$BaseDir,
    [string]$ServiceName = "PhilipsHifBridge"
)

function Append-Log([string]$Title) {
    Add-Content -Path $LogPath -Value ""
    Add-Content -Path $LogPath -Value "===== $Title ====="
}

Append-Log "Environment"
Add-Content -Path $LogPath -Value ("Time: " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))
Add-Content -Path $LogPath -Value ("User: " + [Environment]::UserName)
Add-Content -Path $LogPath -Value ("Machine: " + $env:COMPUTERNAME)
Add-Content -Path $LogPath -Value ("OS: " + [Environment]::OSVersion)

Append-Log "sc qc $ServiceName"
& sc.exe qc $ServiceName 2>&1 | Out-String | Add-Content -Path $LogPath

Append-Log "sc query $ServiceName"
& sc.exe query $ServiceName 2>&1 | Out-String | Add-Content -Path $LogPath

Append-Log "ServiceControlManager events (last 30 min)"
try {
    $since = (Get-Date).AddMinutes(-30)
    Get-WinEvent -FilterHashtable @{
        LogName = 'System'
        ProviderName = 'Service Control Manager'
        StartTime = $since
    } -ErrorAction Stop |
        Where-Object { $_.Message -match $ServiceName } |
        Select-Object -First 15 |
        ForEach-Object { Add-Content -Path $LogPath -Value ("[" + $_.TimeCreated + "] " + $_.Message) }
} catch {
    Add-Content -Path $LogPath -Value ("Event log query failed: " + $_.Exception.Message)
}

Append-Log "Application events for $ServiceName (last 30 min)"
try {
    $since = (Get-Date).AddMinutes(-30)
    Get-WinEvent -FilterHashtable @{
        LogName = 'Application'
        StartTime = $since
    } -ErrorAction Stop |
        Where-Object { $_.Message -match $ServiceName -or $_.ProviderName -match 'PhilipsHifBridge' } |
        Select-Object -First 15 |
        ForEach-Object { Add-Content -Path $LogPath -Value ("[" + $_.TimeCreated + "] " + $_.ProviderName + ": " + $_.Message) }
} catch {
    Add-Content -Path $LogPath -Value ("Application event log query failed: " + $_.Exception.Message)
}

Append-Log "Ports 5080 / 9912"
& netstat.exe -ano 2>&1 | Select-String -Pattern ':5080|:9912' | ForEach-Object { Add-Content -Path $LogPath -Value $_.Line }

Append-Log "HTTP http://localhost:5080/status"
try {
    $r = Invoke-WebRequest -UseBasicParsing -TimeoutSec 5 -Uri 'http://localhost:5080/status'
    Add-Content -Path $LogPath -Value ("HTTP " + $r.StatusCode + ": " + $r.Content)
} catch {
    Add-Content -Path $LogPath -Value ("HTTP FAIL: " + $_.Exception.Message)
}

$config = Join-Path $BaseDir 'bridge-service.config'
$fatal = Join-Path $BaseDir 'bridge-fatal.log'
$start = Join-Path $BaseDir 'service-start.log'
$bridge = Join-Path $BaseDir 'bridge.log'

foreach ($pair in @(
    @{ Title = 'bridge-service.config'; Path = $config },
    @{ Title = 'service-start.log'; Path = $start },
    @{ Title = 'bridge-fatal.log'; Path = $fatal }
)) {
    Append-Log $pair.Title
    if (Test-Path $pair.Path) {
        Get-Content -Path $pair.Path -ErrorAction SilentlyContinue | Add-Content -Path $LogPath
    } else {
        Add-Content -Path $LogPath -Value "(file not found: $($pair.Path))"
    }
}

Append-Log "bridge.log (last 80 lines)"
if (Test-Path $bridge) {
    Get-Content -Path $bridge -Tail 80 -ErrorAction SilentlyContinue | Add-Content -Path $LogPath
} else {
    Add-Content -Path $LogPath -Value "(file not found: $bridge)"
}

Append-Log "End diagnostics"
