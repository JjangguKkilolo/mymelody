[CmdletBinding()]
param([switch] $SkipBuild)

$ErrorActionPreference = 'Stop'
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$artifactRoot = Join-Path $workspaceRoot 'artifacts/UpdateSmoke'
$runRoot = Join-Path $artifactRoot ('run-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
$dotnetExe = Join-Path $workspaceRoot '.tooling/dotnet/dotnet.exe'
if (!(Test-Path -LiteralPath $dotnetExe)) { $dotnetExe = 'dotnet' }
$previousRoot = $env:MYMELODY_SMOKE_ROOT
$previousFeed = $env:MYMELODY_SMOKE_FEED
$previousDotnetRoot = $env:DOTNET_ROOT
$previousLookup = $env:DOTNET_MULTILEVEL_LOOKUP

function Wait-SmokeResult([string] $resultDirectory) {
    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    while ([DateTime]::UtcNow -lt $deadline) {
        $failure = Join-Path $resultDirectory 'failure.txt'
        if (Test-Path -LiteralPath $failure) { throw (Get-Content -LiteralPath $failure -Raw) }
        $success = Join-Path $resultDirectory 'success.json'
        if (Test-Path -LiteralPath $success) {
            Get-Content -LiteralPath $success -Raw
            return
        }
        Start-Sleep -Milliseconds 500
    }
    throw "Smoke test timed out. Inspect $resultDirectory"
}

Push-Location -LiteralPath $workspaceRoot
try {
    if (!$SkipBuild) {
        & $dotnetExe tool restore
        if ($LASTEXITCODE -ne 0) { throw 'Tool restore failed.' }
        foreach ($version in @('1.0.0', '1.0.1')) {
            $build = Join-Path $artifactRoot "build-$version"
            $release = Join-Path $artifactRoot "release-$version"
            & $dotnetExe publish tests/MyMelody.UpdateSmoke/MyMelody.UpdateSmoke.csproj -c Release -r win-x64 --self-contained true "-p:Version=$version" -o $build --nologo
            if ($LASTEXITCODE -ne 0) { throw "Publish failed: $version" }
            & $dotnetExe tool run vpk -- pack --packId MyMelodyUpdateSmoke --packVersion $version --packDir $build --mainExe MyMelodyUpdateSmoke.exe --packTitle 'MyMelody Update Smoke Test' --channel win --runtime win-x64 --shortcuts None --outputDir $release --delta None
            if ($LASTEXITCODE -ne 0) { throw "Pack failed: $version" }
        }
    }
    New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
    $env:MYMELODY_SMOKE_FEED = Join-Path $artifactRoot 'release-1.0.1'
    $env:DOTNET_ROOT = Join-Path $runRoot 'no-external-dotnet'
    $env:DOTNET_MULTILEVEL_LOOKUP = '0'

    $portable = Join-Path $runRoot 'portable-app'
    Expand-Archive -LiteralPath (Join-Path $artifactRoot 'release-1.0.0/MyMelodyUpdateSmoke-win-Portable.zip') -DestinationPath $portable
    $env:MYMELODY_SMOKE_ROOT = Join-Path $runRoot 'portable-result'
    New-Item -ItemType Directory -Path $env:MYMELODY_SMOKE_ROOT -Force | Out-Null
    $portableProcess = Start-Process -FilePath (Join-Path $portable 'current/MyMelodyUpdateSmoke.exe') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runRoot 'portable-stdout.txt') -RedirectStandardError (Join-Path $runRoot 'portable-stderr.txt')
    Wait-SmokeResult $env:MYMELODY_SMOKE_ROOT

    # The setup phase has no smoke root, so any installer-triggered first launch immediately exits.
    $env:MYMELODY_SMOKE_ROOT = $null
    $install = [IO.Path]::GetFullPath((Join-Path $runRoot 'installed-app'))
    if (!$install.StartsWith($runRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe smoke install location.' }
    $installer = Start-Process -FilePath (Join-Path $artifactRoot 'release-1.0.0/MyMelodyUpdateSmoke-win-Setup.exe') -ArgumentList @('--silent', '--installto', ('"' + $install + '"'), '--log', ('"' + (Join-Path $runRoot 'setup.log') + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($installer.ExitCode -ne 0) { throw "Installer failed with exit code $($installer.ExitCode)" }
    $env:MYMELODY_SMOKE_ROOT = Join-Path $runRoot 'installed-result'
    New-Item -ItemType Directory -Path $env:MYMELODY_SMOKE_ROOT -Force | Out-Null
    $installedProcess = Start-Process -FilePath (Join-Path $install 'current/MyMelodyUpdateSmoke.exe') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runRoot 'installed-stdout.txt') -RedirectStandardError (Join-Path $runRoot 'installed-stderr.txt')
    Wait-SmokeResult $env:MYMELODY_SMOKE_ROOT

    $results = [ordered]@{
        passed = $true
        portable = Get-Content -LiteralPath (Join-Path $runRoot 'portable-result/success.json') -Raw | ConvertFrom-Json
        installed = Get-Content -LiteralPath (Join-Path $runRoot 'installed-result/success.json') -Raw | ConvertFrom-Json
        sdkFreeMachineTested = $false
        bundledRuntimeWithExternalLookupDisabled = $true
    }
    $results | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runRoot 'verification.json') -Encoding utf8
    # Remove only the isolated test installation and its registry entry, keeping evidence/data outside it.
    $env:MYMELODY_SMOKE_ROOT = $null
    $uninstaller = Start-Process -FilePath (Join-Path $install 'Update.exe') -ArgumentList @('uninstall', '--silent') -WindowStyle Hidden -PassThru -Wait
    if ($uninstaller.ExitCode -ne 0) { Write-Warning "Smoke installer cleanup returned $($uninstaller.ExitCode). Test evidence is saved." }
    Write-Output "Both real update smoke tests passed: $runRoot"
} finally {
    $env:MYMELODY_SMOKE_ROOT = $previousRoot
    $env:MYMELODY_SMOKE_FEED = $previousFeed
    $env:DOTNET_ROOT = $previousDotnetRoot
    $env:DOTNET_MULTILEVEL_LOOKUP = $previousLookup
    Pop-Location
}
