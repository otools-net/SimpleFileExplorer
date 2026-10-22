<#
.SYNOPSIS
    Builds the portable packages of SFE locally and smoke-tests them.
.DESCRIPTION
    Windows: a self-contained zip (artifacts/SFE-<version>-win-x64.zip). Unzip it anywhere and run Sfe.Api.exe;
    settings and data stay in that folder.
    Linux: an AppImage (artifacts/SFE-<version>-x86_64.AppImage). The .NET part is published here, the image is
    packed and tested in WSL (build/linux/make-appimage.sh, build/linux/smoke-test.sh). On a Linux machine, use
    build/build.sh instead.
    Needs the .NET 10 SDK and Node.js (npm builds the web client); for Linux also WSL with curl (appimagetool is
    downloaded once into artifacts/tools).
.EXAMPLE
    build/build.ps1
.EXAMPLE
    build/build.ps1 -Target windows -SkipTests
.EXAMPLE
    build/build.ps1 -NoTests
#>
param(
    [ValidateSet('all', 'windows', 'linux')] [string] $Target = 'all',
    # Defaults to the version in Directory.Build.props, e.g. 1.2.0. Also the version the app reports.
    [string] $Version,
    # Skip the unit/integration tests (dotnet test, and npm test of the web client) before packaging.
    [switch] $SkipTests,
    # Skip starting the packages to check them.
    [switch] $SkipSmokeTest,
    # No tests at all: both -SkipTests and -SkipSmokeTest.
    [switch] $NoTests,
    # WSL distribution for the Linux steps; the default one when empty.
    [string] $WslDistro
)

$ErrorActionPreference = 'Stop'
if ($NoTests) { $SkipTests = $true; $SkipSmokeTest = $true }
if (-not (Get-Command npm -ErrorAction SilentlyContinue)) { throw 'npm not found: building the web client (src/Sfe.Web) needs Node.js on the PATH' }
$repo = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $repo 'artifacts'
$project = Join-Path $repo 'src\Sfe.Api\Sfe.Api.csproj'

function Invoke-Native([string] $what, [scriptblock] $command) {
    Write-Host "==> $what" -ForegroundColor Cyan
    # Judged by the exit code: Windows PowerShell turns any stderr line of a native command into a terminating error.
    $ErrorActionPreference = 'Continue'
    # To the console, not into the caller's return value.
    # (An empty stderr line comes back as the text "System.Management.Automation.RemoteException".)
    & $command 2>&1 | ForEach-Object { "$_" -replace '^System\.Management\.Automation\.RemoteException$', '' } | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)" }
}

function Invoke-Wsl([string[]] $arguments) {
    $distro = @()
    if ($WslDistro) { $distro = @('-d', $WslDistro) }
    & wsl.exe @distro -- @arguments
}

function ConvertTo-WslPath([string] $path) {
    $converted = Invoke-Wsl @('wslpath', '-a', ($path -replace '\\', '/'))
    if ($LASTEXITCODE -ne 0 -or -not $converted) { throw "Cannot convert '$path' to a WSL path. Is WSL installed?" }
    return "$converted".Trim()
}

function Publish-Portable([string] $rid) {
    $out = Join-Path $artifacts "publish\$rid"
    if (Test-Path $out) { Remove-Item -Recurse -Force $out }
    Invoke-Native "Publish $rid" {
        dotnet publish $project -c Release -r $rid -p:SfePortable=true "-p:Version=$Version" -o $out --nologo
    }
    return $out
}

if (-not $Version) {
    $Version = "$(dotnet msbuild $project -getProperty:Version)".Trim()
    if ($LASTEXITCODE -ne 0) { throw "Cannot read the version from Directory.Build.props (exit code $LASTEXITCODE)" }
}
# Part of the file names: a semantic version (1.2.0, 1.3.0-beta.1), nothing else.
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw "Invalid version '$Version': expected major.minor.patch[-prerelease]" }
Write-Host "SFE portable build $Version ($Target)"
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

if (-not $SkipTests) {
    Invoke-Native 'Tests' { dotnet test (Join-Path $repo 'Sfe.slnx') -c Release --nologo }
    Invoke-Native 'Web client tests' { npm --prefix (Join-Path $repo 'src/Sfe.Web') test }
}

$results = @()

if ($Target -in 'all', 'windows') {
    $publish = Publish-Portable 'win-x64'
    $zip = Join-Path $artifacts "SFE-$Version-win-x64.zip"
    if (Test-Path $zip) { Remove-Item -Force $zip }
    Write-Host '==> Zip win-x64' -ForegroundColor Cyan
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    # The zip holds an "SFE" folder rather than loose files.
    $staging = Join-Path $artifacts 'package\SFE'
    if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
    New-Item -ItemType Directory -Force -Path (Split-Path $staging) | Out-Null
    Copy-Item -Recurse $publish $staging
    [IO.Compression.ZipFile]::CreateFromDirectory($staging, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
    $results += $zip

    if (-not $SkipSmokeTest) {
        Write-Host '==> Smoke test win-x64' -ForegroundColor Cyan
        & (Join-Path $PSScriptRoot 'windows\smoke-test.ps1') -Zip $zip
    }
}

if ($Target -in 'all', 'linux') {
    $publish = Publish-Portable 'linux-x64'
    $appImage = Join-Path $artifacts "SFE-$Version-x86_64.AppImage"
    $script = ConvertTo-WslPath (Join-Path $PSScriptRoot 'linux\make-appimage.sh')
    Invoke-Native 'AppImage (WSL)' {
        Invoke-Wsl @('bash', $script, (ConvertTo-WslPath $publish), (ConvertTo-WslPath $appImage), $Version)
    }
    $results += $appImage

    if (-not $SkipSmokeTest) {
        $script = ConvertTo-WslPath (Join-Path $PSScriptRoot 'linux\smoke-test.sh')
        Invoke-Native 'Smoke test AppImage (WSL)' { Invoke-Wsl @('bash', $script, (ConvertTo-WslPath $appImage)) }
    }
}

Write-Host ''
Write-Host 'Packages:' -ForegroundColor Green
$results | ForEach-Object { Write-Host "  $_" }
