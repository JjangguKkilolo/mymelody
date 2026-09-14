[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')]
    [string] $Version,
    [string] $OutputDirectory = 'artifacts/releases',
    [string] $ReleaseNotes = 'docs/release-notes.md',
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputPath = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $workspaceRoot $OutputDirectory }))
# A fresh staging directory prevents removed sprites or old binaries leaking into a repeated local build.
$publishPath = Join-Path $workspaceRoot ("artifacts/publish/$Version-" + [guid]::NewGuid().ToString('N'))
$localDotnet = Join-Path $workspaceRoot '.tooling/dotnet/dotnet.exe'
$dotnetExe = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$notesPath = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($ReleaseNotes)) { $ReleaseNotes } else { Join-Path $workspaceRoot $ReleaseNotes }))
$iconPath = Join-Path $workspaceRoot 'src/MyMelody.App/Assets/Brand/app.ico'
if (!(Test-Path -LiteralPath $iconPath)) { throw 'The application icon is missing.' }

Push-Location -LiteralPath $workspaceRoot
try {
    if (!$SkipTests) {
        $testProjects = Get-ChildItem -LiteralPath (Join-Path $workspaceRoot 'tests') -Recurse -Filter '*.csproj' |
            Where-Object { (Get-Content -LiteralPath $_.FullName -Raw) -match '<IsTestProject>true</IsTestProject>' }
        foreach ($testProject in $testProjects) {
            & $dotnetExe test $testProject.FullName -c Release --nologo
            if ($LASTEXITCODE -ne 0) { throw "Tests failed: $($testProject.Name)" }
        }
    }
    & $dotnetExe tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Could not restore pinned Velopack packaging tool.' }
    & $dotnetExe publish src/MyMelody.App/MyMelody.App.csproj -c Release -r win-x64 --self-contained true -p:Version=$Version -p:PublishSingleFile=false -o $publishPath --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }

    foreach ($runtimeFile in @('MyMelodyPractice.exe', 'coreclr.dll', 'PresentationFramework.dll', 'Microsoft.Data.Sqlite.dll')) {
        if (!(Test-Path -LiteralPath (Join-Path $publishPath $runtimeFile))) { throw "Self-contained application file is missing: $runtimeFile" }
    }
    $sourceAssets = Join-Path $workspaceRoot 'src/MyMelody.App/Assets'
    $publishedAssets = Join-Path $publishPath 'Assets'
    $manifestPath = Join-Path $publishedAssets 'Characters/assets-manifest.json'
    if (!(Test-Path -LiteralPath $manifestPath)) { throw 'Character asset manifest is missing.' }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 2 -or !$manifest.assets -or @($manifest.assets).Count -eq 0) {
        throw 'Expected character asset manifest schema version 2 with sprite sheets.'
    }
    $seenIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($asset in $manifest.assets) {
        if ($asset.id -notmatch '^[a-z][a-z0-9-]*$' -or !$seenIds.Add([string]$asset.id) -or $asset.filename -cne "$($asset.id).png") {
            throw "Invalid or duplicate character asset ID or file name: $($asset.id)"
        }
        if ($asset.columns -ne 2 -or $asset.rows -ne 3 -or $asset.stages -ne 3 -or $asset.framesPerStage -ne 2 -or
            $asset.frameWidth -le 0 -or $asset.frameWidth -ne $asset.frameHeight -or
            $asset.width -ne $asset.frameWidth * $asset.columns -or $asset.height -ne $asset.frameHeight * $asset.rows) {
            throw "Expected a 2-column, 3-row sprite sheet with square cells: $($asset.id)"
        }
        if (!(Test-Path -LiteralPath (Join-Path $publishedAssets "Characters/$($asset.filename)"))) {
            throw "Character sprite sheet is missing: $($asset.filename)"
        }
    }
    Get-ChildItem -LiteralPath $sourceAssets -Recurse -File | Where-Object { $_.Extension -in @('.png', '.json') } | ForEach-Object {
        $relativeAsset = [IO.Path]::GetRelativePath($sourceAssets, $_.FullName)
        $publishedAsset = Join-Path $publishedAssets $relativeAsset
        if (!(Test-Path -LiteralPath $publishedAsset) -or
            (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $publishedAsset -Algorithm SHA256).Hash) {
            throw "Published asset is missing or differs from source: $relativeAsset"
        }
    }

    # Run the exact self-contained payload before creating any releasable installer or update package.
    & (Join-Path $PSScriptRoot 'verify-ui.ps1') -Exe (Join-Path $publishPath 'MyMelodyPractice.exe') -OutputDirectory "artifacts/package-ui/$Version"

    if (!(Test-Path -LiteralPath $notesPath)) {
        $notesPath = Join-Path $workspaceRoot "artifacts/release-notes-$Version.md"
        "# 마이멜로디 연습 친구 $Version`n`nMIDI 연습 기록, 캐릭터 성장과 컬렉션, 백업 및 GitHub 업데이트를 제공합니다." | Set-Content -LiteralPath $notesPath -Encoding utf8
    }
    New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
    & $dotnetExe tool run vpk -- pack --packId MyMelodyPractice --packVersion $Version --packDir $publishPath --mainExe MyMelodyPractice.exe --packTitle '마이멜로디 연습 친구' --packAuthors 'JjangguKkilolo' --channel win --runtime win-x64 --outputDir $outputPath --releaseNotes $notesPath --icon $iconPath --delta None
    if ($LASTEXITCODE -ne 0) { throw 'Velopack packaging failed.' }

    # The package CLI uses channel-qualified file names for its Windows artifacts.
    if (!(Get-ChildItem -LiteralPath $outputPath -Filter '*Setup.exe')) { throw 'Setup.exe is missing.' }
    if (!(Get-ChildItem -LiteralPath $outputPath -Filter '*Portable.zip')) { throw 'Portable.zip is missing.' }
    if (!(Test-Path -LiteralPath (Join-Path $outputPath 'releases.win.json'))) { throw 'Update feed is missing.' }
    if (!(Get-ChildItem -LiteralPath $outputPath -Filter "*-$Version-full.nupkg")) { throw 'Full update package is missing.' }
    Copy-Item -LiteralPath $notesPath -Destination (Join-Path $outputPath 'release-notes.md') -Force
    $hashes = Get-ChildItem -LiteralPath $outputPath -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | ForEach-Object {
        $hash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
        "$($hash.Hash.ToLowerInvariant())  $($_.Name)"
    }
    $hashes | Set-Content -LiteralPath (Join-Path $outputPath 'SHA256SUMS.txt') -Encoding utf8
    Write-Output "Release $Version is ready in $outputPath"
} finally {
    Pop-Location
}
