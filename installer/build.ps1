[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$Version = '2.0.0',
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $repoRoot 'artifacts'
$publishRoot = Join-Path $artifacts 'publish'
$trackerOut = Join-Path $publishRoot 'tracker'
$dashboardOut = Join-Path $publishRoot 'dashboard'

function Get-Dotnet10 {
    $candidates = @()
    $local = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
    if (Test-Path $local) {
        $candidates += $local
    }

    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command) {
        $candidates += $command.Source
    }

    foreach ($candidate in $candidates) {
        $sdks = & $candidate --list-sdks 2>$null
        if ($sdks -match '(?m)^10\.') {
            return $candidate
        }
    }

    throw 'The .NET 10 SDK is required. Install it from https://dotnet.microsoft.com/download/dotnet/10.0.'
}

$dotnet = Get-Dotnet10
Write-Host "Using .NET host: $dotnet"

if (-not $SkipPublish) {
    Write-Host 'Publishing the tracker and dashboard (self-contained)...'
    Remove-Item $publishRoot -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $trackerOut, $dashboardOut -Force | Out-Null

    & $dotnet publish (Join-Path $repoRoot 'src\InternetUptimeTracker\InternetUptimeTracker.csproj') `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -o $trackerOut `
        -p:DebugType=None `
        -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) {
        throw 'The tracker publish step failed.'
    }

    & $dotnet publish (Join-Path $repoRoot 'src\InternetUptimeTracker.Dashboard\InternetUptimeTracker.Dashboard.csproj') `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -o $dashboardOut `
        -p:DebugType=None `
        -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) {
        throw 'The dashboard publish step failed.'
    }
}

$trackerExe = Join-Path $trackerOut 'InternetUptimeTracker.exe'
$dashboardExe = Join-Path $dashboardOut 'InternetUptimeTracker.Dashboard.exe'
$dashboardIndex = Join-Path $dashboardOut 'wwwroot\index.html'

foreach ($required in @($trackerExe, $dashboardExe, $dashboardIndex)) {
    if (-not (Test-Path $required)) {
        throw "Expected publish output is missing: $required"
    }
}

Write-Host 'Building the installer...'
$wixProject = Join-Path $PSScriptRoot 'InternetUptimeTracker.Installer.wixproj'
$installerBin = Join-Path $PSScriptRoot "bin\$Configuration"

& $dotnet build $wixProject `
    -c $Configuration `
    "-p:TrackerPublishDir=$trackerOut" `
    "-p:DashboardPublishDir=$dashboardOut" `
    "-p:ProductVersion=$Version" `
    "-p:OutputPath=$installerBin"
if ($LASTEXITCODE -ne 0) {
    throw 'The installer build failed.'
}

$msi = Get-ChildItem -Path $installerBin -Filter '*.msi' -Recurse -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $msi) {
    throw "The installer build did not produce an MSI in $installerBin."
}

New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$finalMsi = Join-Path $artifacts "InternetUptimeTrackerSetup-$Version.msi"
Copy-Item $msi.FullName $finalMsi -Force

$sizeMb = [math]::Round((Get-Item $finalMsi).Length / 1MB, 1)
Write-Host ''
Write-Host "Installer ready: $finalMsi ($sizeMb MB)"
Write-Host 'Right-click the MSI and choose Install, or double-click it and approve the UAC prompt.'
