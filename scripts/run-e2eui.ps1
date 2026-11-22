# Runs the browser tier (E2EUI) against the RELEASE PUBLISH - the same `dotnet publish -c Release`
# output the workflow deploys (framework + libraries trimmed; the app assembly is not IsTrimmable,
# see PoTraffic.Client.csproj) - so a publish-only fault shows up here, not in production.
#
#   pwsh scripts/run-e2eui.ps1 [-Tier E2EUI|E2EAPI|All] [-Port 5150] [-Filter <xunit filter>] [-Headed]
#
# 1. dotnet publish -c Release (client baked as "Test"; same trimming as the deploy)
# 2. Azurite in Docker on 21000-21002 (its own container, removed afterwards)
# 3. the published app (ASPNETCORE_ENVIRONMENT=Test: mock traffic, guest sign-in, nothing billed)
# 4. dotnet test tests/PoTraffic.E2EUI (and/or tests/PoTraffic.E2EAPI with -Tier) against it,
#    then stops everything it started.
param(
    [ValidateSet('E2EUI', 'E2EAPI', 'All')] [string] $Tier = 'E2EUI',
    [int] $Port = 5150,
    [string] $Filter = '',
    [switch] $Headed
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path ([IO.Path]::GetTempPath()) "potraffic-e2eui-$Port"
$container = "potraffic-e2eui-azurite-$Port"
$baseUrl = "http://localhost:$Port"
$app = $null
$code = 1
$envNames = 'ASPNETCORE_ENVIRONMENT', 'ASPNETCORE_URLS', 'ConnectionStrings__TableStorage', 'KeyVault__Uri', 'E2E_BASE_URL', 'E2E_HEADED'

try {
    Write-Host "Publishing the Release build to $publish"
    if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
    dotnet publish (Join-Path $root 'src/PoTraffic.Api/PoTraffic.Api.csproj') -c Release -o $publish `
        -p:WasmApplicationEnvironmentName=Test --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

    docker info *> $null
    if ($LASTEXITCODE -ne 0) { throw 'Docker is not running (Azurite needs it).' }
    docker rm -f $container *> $null
    docker run -d --rm --name $container -p 21000:10000 -p 21001:10001 -p 21002:10002 `
        mcr.microsoft.com/azure-storage/azurite:latest azurite --blobHost 0.0.0.0 --queueHost 0.0.0.0 `
        --tableHost 0.0.0.0 --skipApiVersionCheck --loose | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Azurite did not start' }

    # Azurite's public, well-known development account key (not a secret).
    $devKey = 'Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw=='
    $env:ASPNETCORE_ENVIRONMENT = 'Test'
    $env:ASPNETCORE_URLS = $baseUrl
    $env:KeyVault__Uri = ''
    $env:ConnectionStrings__TableStorage = "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=$devKey;TableEndpoint=http://127.0.0.1:21002/devstoreaccount1;"
    $app = Start-Process dotnet -ArgumentList (Join-Path $publish 'PoTraffic.Api.dll') -WorkingDirectory $publish `
        -PassThru -NoNewWindow -RedirectStandardOutput (Join-Path $publish 'app.out.log') -RedirectStandardError (Join-Path $publish 'app.err.log')

    $up = $false
    foreach ($i in 1..60) {
        try { if ((Invoke-WebRequest "$baseUrl/health/ready" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200) { $up = $true; break } } catch { }
        Start-Sleep -Seconds 1
    }
    if (-not $up) { throw "The published app did not become ready on $baseUrl (see $publish/app.*.log)" }
    Write-Host "Published app is up on $baseUrl"

    $env:E2E_BASE_URL = $baseUrl
    $env:E2E_HEADED = if ($Headed) { '1' } else { '0' }
    $projects = switch ($Tier) { 'E2EUI' { @('PoTraffic.E2EUI') } 'E2EAPI' { @('PoTraffic.E2EAPI') } default { @('PoTraffic.E2EAPI', 'PoTraffic.E2EUI') } }
    $code = 0
    foreach ($project in $projects) {
        $testArgs = @('test', (Join-Path $root "tests/$project"), '-c', 'Release', '--nologo')
        if ($Filter) { $testArgs += @('--filter', $Filter) }
        dotnet @testArgs
        if ($LASTEXITCODE -ne 0) { $code = $LASTEXITCODE }
    }
}
finally {
    if ($app -and -not $app.HasExited) { Stop-Process -Id $app.Id -Force }
    docker rm -f $container *> $null
    foreach ($name in $envNames) { Remove-Item "Env:$name" -ErrorAction SilentlyContinue }
}
exit $code
