<#
.SYNOPSIS
    Extracts a portable Windows build and checks that it really works against a small temporary library: the setup
    of the first administrator (with a setup code), web client, browsing, an index scan into SQLite, search, and the
    data folder next to the executable.
.EXAMPLE
    build/windows/smoke-test.ps1 -Zip artifacts/SFE-1.0.0-win-x64.zip
#>
param(
    [Parameter(Mandatory)] [string] $Zip,
    [int] $Port = 5092
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$base = "http://127.0.0.1:$Port"

$work = Join-Path ([IO.Path]::GetTempPath()) ("sfe-smoke-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $work | Out-Null
$process = $null
$setupCode = 'smoke-test-setup-code'
# Keeps the sign-in cookie; requests changing something carry the header the API asks for.
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$session.Headers['X-SFE-Request'] = '1'

function Get-Text([string] $path) {
    (Invoke-WebRequest -UseBasicParsing -Uri "$base$path" -TimeoutSec 30 -WebSession $session).Content
}

function Post-Json([string] $path, [string] $body) {
    (Invoke-WebRequest -UseBasicParsing -Method Post -Uri "$base$path" -ContentType 'application/json' -Body $body -TimeoutSec 30 -WebSession $session).Content
}

function Fail([string] $message) {
    Write-Host "SMOKE TEST FAILED: $message" -ForegroundColor Red
    Write-Host '---- application log ----'
    Get-Content (Join-Path $work 'app.log'), (Join-Path $work 'app.err.log') -ErrorAction SilentlyContinue | Write-Host
    throw "Smoke test failed: $message"
}

try {
    [IO.Compression.ZipFile]::ExtractToDirectory((Resolve-Path $Zip).Path, (Join-Path $work 'app'))
    $exe = Get-ChildItem -Path (Join-Path $work 'app') -Recurse -Filter 'Sfe.Api.exe' | Select-Object -First 1
    if (-not $exe) { Fail 'Sfe.Api.exe is not in the zip' }
    $appDir = $exe.DirectoryName

    $library = Join-Path $work 'library'
    New-Item -ItemType Directory -Path (Join-Path $library 'Holidays 2024'), (Join-Path $library "M$([char]0xFA)sica") | Out-Null
    Set-Content -Path (Join-Path $library 'Holidays 2024\smoketest-beach.jpg') -Value 'smoke'
    Set-Content -Path (Join-Path $library "M$([char]0xFA)sica\smoketest-song.mp3") -Value 'smoke'
    Set-Content -Path (Join-Path $library 'readme.txt') -Value 'smoke'

    Write-Host "Starting $($exe.FullName) on $base"
    # Started from another folder on purpose: nothing may depend on the working directory.
    $process = Start-Process -FilePath $exe.FullName -WorkingDirectory $work -PassThru -NoNewWindow `
        -ArgumentList '--urls', $base, "--Sfe:Root=`"$library`"", "--Sfe:Setup:Code=$setupCode" `
        -RedirectStandardOutput (Join-Path $work 'app.log') -RedirectStandardError (Join-Path $work 'app.err.log')

    $started = $false
    for ($i = 0; $i -lt 60 -and -not $started; $i++) {
        if ($process.HasExited) { Fail "the application exited during startup (exit code $($process.ExitCode))" }
        try { $setup = Get-Text '/api/setup' | ConvertFrom-Json; $started = $true } catch { Start-Sleep -Seconds 1 }
    }
    if (-not $started) { Fail "no answer from $base/api/setup after 60 s" }
    if (-not $setup.required -or -not $setup.codeConfigured) { Fail "a new install should ask for its administrator with the setup code: $($setup | ConvertTo-Json -Compress)" }
    try { Get-Text '/api/config' | Out-Null; Fail 'the API answers before the setup' }
    catch { if ($_.Exception.Response.StatusCode.value__ -ne 503) { Fail "before the setup the API should answer 503, not: $($_.Exception.Message)" } }
    Write-Host '  ok  setup required'

    Post-Json '/api/setup' "{`"code`":`"$setupCode`",`"userName`":`"admin`",`"password`":`"smoke-test-password`"}" | Out-Null
    $config = Get-Text '/api/config' | ConvertFrom-Json
    if ($config.user.userName -ne 'admin') { Fail "the setup did not sign the administrator in: $($config.user | ConvertTo-Json -Compress)" }
    Write-Host '  ok  setup and /api/config'
    # The package is named after the version the application reports (SFE-<version>-win-x64.zip).
    if ((Split-Path -Leaf $Zip) -match '^SFE-(.+)-win-x64\.zip$' -and $config.version -ne $Matches[1]) {
        Fail "the application reports version '$($config.version)', the package is named '$($Matches[1])'"
    }
    Write-Host "  ok  version $($config.version)"

    $index = Get-Text '/'
    if ($index -notmatch '<sfe-root') { Fail 'the web client (/) is not served' }
    if ($index -notmatch 'main-[A-Za-z0-9]+\.js') { Fail 'the web client has no script' }
    Get-Text "/$($Matches[0])" | Out-Null
    Write-Host '  ok  web client'

    if ((Get-Text '/api/browse?path=') -notmatch 'Holidays 2024') { Fail "browsing the root doesn't list 'Holidays 2024'" }
    if ((Get-Text '/api/browse?path=M%C3%BAsica') -notmatch 'smoketest-song') { Fail 'browsing a Unicode folder fails' }
    Write-Host '  ok  browse'

    Post-Json '/api/scan' '{"path":""}' | Out-Null
    $status = ''
    for ($i = 0; $i -lt 60; $i++) {
        $status = Get-Text '/api/scan'
        if ($status -match '"state":"completed"') { break }
        if ($status -match '"state":"(failed|cancelled)"') { Fail "scan ended badly: $status" }
        Start-Sleep -Seconds 1
    }
    if ($status -notmatch '"state":"completed"') { Fail "scan did not complete: $status" }
    Write-Host '  ok  scan'

    if ((Get-Text '/api/search?q=smoketest') -notmatch 'smoketest-beach') { Fail "search doesn't find the indexed file" }
    Write-Host '  ok  search'

    if (-not (Test-Path (Join-Path $appDir 'data\sfe.db'))) { Fail 'the SQLite index is not in the data folder next to the executable' }
    if (-not (Test-Path (Join-Path $appDir 'data\keys'))) { Fail 'the keys of the sign-in cookies are not in the data folder next to the executable' }
    Write-Host "  ok  data folder ($appDir\data)"

    Write-Host "Smoke test passed: $Zip" -ForegroundColor Green
}
finally {
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit(10000) | Out-Null
    }
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
