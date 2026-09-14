[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Exe,
    [string] $OutputDirectory = 'artifacts/ui-smoke',
    [ValidateRange(1, 60)]
    [int] $TimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$exePath = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($Exe)) { $Exe } else { Join-Path $workspaceRoot $Exe }))
$outputBase = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $workspaceRoot $OutputDirectory }))
# Every invocation has a fresh result/data folder, so a prior success cannot hide a failing run.
$resultDirectory = Join-Path $outputBase ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N'))
if (!(Test-Path -LiteralPath $exePath -PathType Leaf)) { throw "UI smoke executable was not found: $exePath" }
New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
$previousDotnetRoot = $env:DOTNET_ROOT
$localRuntime = Join-Path $workspaceRoot '.tooling/dotnet'
$process = $null
try {
    # A framework-dependent developer build may use the workspace SDK. Self-contained builds resolve their adjacent runtime.
    if (Test-Path -LiteralPath (Join-Path $localRuntime 'dotnet.exe')) { $env:DOTNET_ROOT = $localRuntime }
    $process = Start-Process -FilePath $exePath -ArgumentList @('--ui-smoke', ('"' + $resultDirectory + '"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $resultDirectory 'stdout.txt') -RedirectStandardError (Join-Path $resultDirectory 'stderr.txt')
    if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
        # Terminate only the dedicated child launched by this invocation; never another running practice app.
        $process.Kill($true)
        throw "UI smoke exceeded $TimeoutSeconds seconds. Evidence: $resultDirectory"
    }
    $failurePath = Join-Path $resultDirectory 'failure.txt'
    if (Test-Path -LiteralPath $failurePath) {
        throw "UI smoke failed: $(Get-Content -LiteralPath $failurePath -Raw)"
    }
    if ($process.ExitCode -ne 0) {
        throw "UI smoke exited with code $($process.ExitCode). Evidence: $resultDirectory"
    }
    $resultPath = Join-Path $resultDirectory 'result.json'
    if (!(Test-Path -LiteralPath $resultPath)) {
        throw "UI smoke produced no result.json. Evidence: $resultDirectory"
    }
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if ($result.success -ne $true) { throw "UI smoke result did not indicate success: $resultPath" }
    [pscustomobject]@{ Success = $true; ResultDirectory = $resultDirectory; Checks = $result.checks }
} finally {
    if ($null -ne $process) { $process.Dispose() }
    $env:DOTNET_ROOT = $previousDotnetRoot
}
