# start-dev.ps1 — Stop a stale PoTraffic host, start PoTraffic's own Azurite, then launch the API.
# Run from repo root: ./SCRIPTS/start-dev.ps1
#
# Sibling projects on this machine use the same defaults (5000/5001 for a dev host, 10002 for
# Azurite). This script only ever stops PoTraffic's own processes; when 5001 belongs to another
# app it moves PoTraffic to the next free port pair instead of fighting for it.

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Join-Path $PSScriptRoot '..'
$AzuriteContainer = 'PoTraffic'
$AzuritePort = 10012   # matches docker-compose.yml and appsettings.Development.json

Write-Host "`n=== PoTraffic Dev Startup ===" -ForegroundColor Cyan

function Get-ListenerProcess([int]$Port) {
    Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique |
        ForEach-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue }
}

function Test-IsPoTrafficHost($Proc) {
    if ($Proc.Name -eq 'PoTraffic.API') { return $true }
    if ($Proc.Name -notlike '*dotnet*') { return $false }
    $cmd = (Get-CimInstance Win32_Process -Filter "ProcessId=$($Proc.Id)").CommandLine
    return $cmd -like '*PoTraffic.API*'
}

# 1. Stop a stale PoTraffic host on any port it may have used; leave other apps alone.
foreach ($port in @(5000, 5001, 5010, 5011, 5020, 5021)) {
    foreach ($proc in @(Get-ListenerProcess $port)) {
        if (Test-IsPoTrafficHost $proc) {
            Write-Host "  Stopping stale PoTraffic host PID $($proc.Id) on port $port" -ForegroundColor Yellow
            Stop-Process -Id $proc.Id -Force
        }
    }
}

# 2. Pick the first HTTPS/HTTP port pair no other app holds.
$https = $null; $http = $null
foreach ($pair in @(@(5001, 5000), @(5011, 5010), @(5021, 5020))) {
    if (-not (Get-ListenerProcess $pair[0]) -and -not (Get-ListenerProcess $pair[1])) {
        $https = $pair[0]; $http = $pair[1]; break
    }
    $owner = @(Get-ListenerProcess $pair[0]) + @(Get-ListenerProcess $pair[1]) | Select-Object -First 1
    Write-Host "  Port $($pair[0])/$($pair[1]) is held by $($owner.Name) (PID $($owner.Id)); trying the next pair." -ForegroundColor Yellow
}
if (-not $https) { Write-Error "No free port pair among 5001/5000, 5011/5010, 5021/5020." }

# 3. Start PoTraffic's own Azurite. A same-named container not created by compose (e.g. a
#    manual `docker run`) blocks `compose up`, so replace it.
$label = docker inspect $AzuriteContainer --format '{{ index .Config.Labels "com.docker.compose.project" }}' 2>$null
if ($LASTEXITCODE -eq 0 -and -not $label) {
    Write-Host "  Replacing hand-started '$AzuriteContainer' container with the compose one." -ForegroundColor Yellow
    docker rm -f $AzuriteContainer | Out-Null
}
Write-Host "`nStarting Azurite (docker compose up -d)..." -ForegroundColor Cyan
Push-Location $RepoRoot
try { docker compose up -d } finally { Pop-Location }
if ($LASTEXITCODE -ne 0) {
    Write-Error "docker compose up failed. Is Docker Desktop running, and is port $AzuritePort free?"
}

# 4. Wait for OUR container's health check — a bare TCP probe would also pass against another
#    project's Azurite.
Write-Host "Waiting for the '$AzuriteContainer' Azurite container to report healthy..." -ForegroundColor Yellow
$maxRetries = 30
for ($i = 1; $i -le $maxRetries; $i++) {
    $health = docker inspect $AzuriteContainer --format '{{.State.Health.Status}}' 2>$null
    if ($health -eq 'healthy') {
        Write-Host "  Azurite is ready on port $AzuritePort (attempt $i)." -ForegroundColor Green
        break
    }
    if ($i -eq $maxRetries) {
        Write-Error "Azurite container '$AzuriteContainer' not healthy after $maxRetries attempts (last status: $health)."
    }
    Start-Sleep -Seconds 1
}

# 5. Launch API (Development, HTTPS). --urls overrides the launch profile's 5001 binding.
Write-Host "`nLaunching PoTraffic API on https://localhost:$https ..." -ForegroundColor Green
Set-Location (Join-Path $RepoRoot 'src' 'PoTraffic.API')
dotnet run --launch-profile https -- --urls "https://localhost:$https;http://localhost:$http"
