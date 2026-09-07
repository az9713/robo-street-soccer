param(
    [string]$RepositoryRoot,
    [string]$Destination,
    [string]$EvidenceDirectory,
    [string]$BuildDirectory,
    [string]$ReleaseReceipt
)

$ErrorActionPreference = "Stop"

function Assert-ContainedPath {
    param([string]$Child, [string]$Root, [string]$Label)
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $childFull = [IO.Path]::GetFullPath($Child)
    if ($childFull -eq $rootFull.TrimEnd('\') -or -not $childFull.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label must remain inside '$Root'."
    }
}

function Get-RelativePathCompat {
    param([string]$Base, [string]$Target)
    $baseFull = [IO.Path]::GetFullPath($Base).TrimEnd('\') + '\'
    $targetFull = [IO.Path]::GetFullPath($Target)
    if ($targetFull.StartsWith($baseFull, [StringComparison]::OrdinalIgnoreCase)) {
        return $targetFull.Substring($baseFull.Length)
    }
    $baseUri = New-Object System.Uri($baseFull)
    $targetUri = New-Object System.Uri($targetFull)
    return [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString())
}

function Assert-FinalEvidence {
    param([string]$Root)
    $evidenceRoot = (Resolve-Path -LiteralPath $Root).Path
    $buildFile = Join-Path $evidenceRoot 'match-windows-build.json'
    if (-not (Test-Path -LiteralPath $buildFile -PathType Leaf)) { throw "Required build receipt match-windows-build.json is missing." }
    try { $buildEvidence = Get-Content -LiteralPath $buildFile -Raw | ConvertFrom-Json }
    catch { throw "Build receipt is not valid JSON." }
    if ($buildEvidence.result -ne 'Succeeded' -or $buildEvidence.errors -ne 0) { throw "Build receipt is not a successful zero-error build." }
    if ($buildEvidence.sha256 -notmatch '^[0-9a-fA-F]{64}$') { throw "Build receipt must contain a 64-character sha256 field." }
    if ($buildEvidence.assemblySha256 -notmatch '^[0-9a-fA-F]{64}$') { throw "Build receipt must contain a 64-character assemblySha256 field." }
    $acceptanceFiles = @(Get-ChildItem -LiteralPath $evidenceRoot -File -Filter 'match-acceptance-*.json')
    if ($acceptanceFiles.Count -ne 3) { throw "Expected exactly three final acceptance receipts at 1x, 0.5x and 0.25x; found $($acceptanceFiles.Count)." }
    $requiredSpeeds = @(1.0, 0.5, 0.25)
    foreach ($acceptanceFile in $acceptanceFiles) {
        try { $acceptance = Get-Content -LiteralPath $acceptanceFile.FullName -Raw | ConvertFrom-Json }
        catch { throw "Acceptance receipt '$($acceptanceFile.Name)' is not valid JSON." }
        if ($acceptance.passed -ne $true) { throw "Acceptance receipt '$($acceptanceFile.Name)' is not marked passed=true." }
        if (-not ($requiredSpeeds | Where-Object { [Math]::Abs(([double]$acceptance.practiceSpeed) - $_) -lt 0.0001 })) { throw "Acceptance receipt '$($acceptanceFile.Name)' has an unexpected practice speed." }
    }
    $speeds = @($acceptanceFiles | ForEach-Object { [double](Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json).practiceSpeed } | Sort-Object -Unique)
    if (($speeds -join ',') -ne '0.25,0.5,1') { throw "Acceptance receipts must cover exactly 1x, 0.5x and 0.25x." }
    return @($buildFile) + $acceptanceFiles
}

function Copy-RelativeFile {
    param([string]$RelativePath, [string]$SourceRoot, [string]$TargetRoot)
    $source = Join-Path $SourceRoot $RelativePath
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Allowlisted source file is missing: $RelativePath" }
    $target = Join-Path $TargetRoot $RelativePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $target
}

function Copy-RelativeDirectory {
    param([string]$RelativePath, [string]$SourceRoot, [string]$TargetRoot)
    $source = Join-Path $SourceRoot $RelativePath
    if (-not (Test-Path -LiteralPath $source -PathType Container)) { throw "Allowlisted source directory is missing: $RelativePath" }
    $targetParent = Join-Path $TargetRoot (Split-Path -Parent $RelativePath)
    New-Item -ItemType Directory -Path $targetParent -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $targetParent -Recurse
}

function Assert-PublicPng {
    param([string]$Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 20000 -or $bytes.Length -lt 24) { throw "Public screenshot is missing or appears blank: $Path" }
    $signature = '89504E470D0A1A0A'
    $actualSignature = ([BitConverter]::ToString($bytes[0..7]) -replace '-', '')
    if ($actualSignature -ne $signature) { throw "Public screenshot is not a PNG: $Path" }
    $width = [Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($bytes, 16))
    $height = [Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($bytes, 20))
    if (($width -ne 1280 -or $height -ne 720) -and ($width -ne 1920 -or $height -ne 1080)) {
        throw "Public screenshot has unexpected dimensions ${width}x${height}: $Path"
    }
}

function Assert-BuildHash {
    param([string]$EvidenceRoot, [string]$BuildRoot)
    if ([string]::IsNullOrWhiteSpace($BuildRoot)) { throw "BuildDirectory is required for source-export hash verification." }
    $build = (Resolve-Path -LiteralPath $BuildRoot).Path
    $executables = @(Get-ChildItem -LiteralPath $build -File -Filter '*.exe' | Where-Object { $_.Name -notmatch 'CrashHandler' })
    if ($executables.Count -ne 1) { throw "Expected exactly one game executable in '$build'; found $($executables.Count)." }
    $buildReceipt = Get-Content -LiteralPath (Join-Path $EvidenceRoot 'match-windows-build.json') -Raw | ConvertFrom-Json
    $actualHash = (Get-FileHash -LiteralPath $executables[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($buildReceipt.sha256.ToLowerInvariant() -ne $actualHash) { throw 'Build receipt sha256 does not match the supplied executable.' }
    $dataDirectory = Join-Path $build ($executables[0].BaseName + '_Data')
    $assemblyPath = Join-Path $dataDirectory 'Managed/Assembly-CSharp.dll'
    if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) { throw 'Managed/Assembly-CSharp.dll is missing beside the executable.' }
    $actualAssemblyHash = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($buildReceipt.assemblySha256.ToLowerInvariant() -ne $actualAssemblyHash) { throw 'Build receipt assemblySha256 does not match the supplied Assembly-CSharp.dll.' }
}

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path }
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { throw "RepositoryRoot is required." }
$repo = (Resolve-Path -LiteralPath $RepositoryRoot).Path
if ([string]::IsNullOrWhiteSpace($Destination)) { $Destination = Join-Path $repo 'PublicRelease/source' }
if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) { $EvidenceDirectory = Join-Path $repo 'Evidence' }
$evidenceFiles = @(Assert-FinalEvidence -Root $EvidenceDirectory)
$destinationFull = [IO.Path]::GetFullPath($Destination)
$publicRoot = Join-Path $repo 'PublicRelease'
Assert-BuildHash -EvidenceRoot (Resolve-Path -LiteralPath $EvidenceDirectory).Path -BuildRoot $BuildDirectory
$releaseReceiptPath = $null
if ([string]::IsNullOrWhiteSpace($ReleaseReceipt)) {
    $releaseCandidates = @(Get-ChildItem -LiteralPath (Join-Path $repo 'Releases') -File -Filter 'RoboStreetSoccer-*-Windows.json' -ErrorAction SilentlyContinue)
    if ($releaseCandidates.Count -ne 1) { throw "ReleaseReceipt is required, or Releases must contain exactly one RoboStreetSoccer-*-Windows.json receipt." }
    $releaseReceiptPath = $releaseCandidates[0].FullName
} else {
    $releaseReceiptPath = (Resolve-Path -LiteralPath $ReleaseReceipt).Path
}
if (-not (Test-Path -LiteralPath $releaseReceiptPath -PathType Leaf)) { throw "Release receipt is missing: $ReleaseReceipt" }
Assert-ContainedPath -Child $destinationFull -Root $publicRoot -Label 'Source export destination'
if (Test-Path -LiteralPath $destinationFull) { throw "Destination already exists; choose a new PublicRelease/source directory." }
New-Item -ItemType Directory -Path $destinationFull -Force | Out-Null

$rootFiles = @('.gitignore', 'README.md', 'RELEASE-NOTES.md', 'RELEASE-GUIDE.md', 'SPEC.md', 'PROJECT-BRIEF.md', 'DEVELOPMENT-LOG.md', 'PROVENANCE.md', 'M1-RESULTS.md')
$setupFiles = @('setup/release_package.ps1', 'setup/release_verify.ps1', 'setup/release_export_source.ps1', 'setup/release-public-allowlist.txt', 'setup/release-test/run-release-tests.ps1')
$evidenceImageFiles = @(
    'Evidence/match-captures/match-default-four-robot-play-1.00.png',
    'Evidence/match-captures/match-receive-1.00.png',
    'Evidence/match-captures/match-shot-contact-1.00.png',
    'Evidence/match-captures/match-tackle-contact-1.00.png'
)
$evidenceDocumentFiles = @(
    'Evidence/MotionReview/REPORT.md',
    'Evidence/MotionReview/PASS-RECEIVE-GEOMETRY.md'
)
$sourceFiles = @(
    'SourceAssets/Robot/RoboPlayer.blend',
    'SourceAssets/Robot/RoboPlayerSoccer.blend',
    'SourceAssets/Robot/soccer_motion.py',
    'SourceAssets/Robot/sanitize_texture_paths.py',
    'SourceAssets/Robot/textures/Image_0.jpg',
    'SourceAssets/Robot/textures/Image_1.jpg',
    'SourceAssets/Robot/textures/Image_2.jpg',
    'SourceAssets/Robot/textures/Image_3.jpg'
)
try {
    foreach ($file in $rootFiles + $setupFiles + $sourceFiles) { Copy-RelativeFile -RelativePath $file -SourceRoot $repo -TargetRoot $destinationFull }
    foreach ($directory in @('UnityProject/Assets', 'UnityProject/Packages', 'UnityProject/ProjectSettings', 'UnityProject/.vscode')) {
        Copy-RelativeDirectory -RelativePath $directory -SourceRoot $repo -TargetRoot $destinationFull
    }
    $generatedCompilerFiles = @(Get-ChildItem -LiteralPath (Join-Path $destinationFull 'UnityProject/Assets') -Recurse -Force -File |
        Where-Object { $_.Name -eq 'csc.rsp' -or $_.Name -eq 'csc.rsp.meta' })
    foreach ($generatedCompilerFile in $generatedCompilerFiles) {
        Remove-Item -LiteralPath $generatedCompilerFile.FullName -Force
    }
    Copy-RelativeFile -RelativePath 'UnityProject/UnityProject.slnx' -SourceRoot $repo -TargetRoot $destinationFull
    $publicEvidenceFiles = @(Get-ChildItem -LiteralPath (Join-Path $repo 'Evidence') -File -Filter '*.json')
    foreach ($evidenceFile in $publicEvidenceFiles) {
        Copy-RelativeFile -RelativePath ('Evidence/' + $evidenceFile.Name) -SourceRoot $repo -TargetRoot $destinationFull
        $publicEvidencePath = Join-Path $destinationFull ('Evidence/' + $evidenceFile.Name)
        $publicEvidence = Get-Content -LiteralPath $publicEvidencePath -Raw
        $sanitizedEvidence = [regex]::Replace($publicEvidence, '(?i)[A-Z]:[\\/][^"\r\n]+', 'relative-build-path')
        if ($sanitizedEvidence -ne $publicEvidence) {
            Set-Content -LiteralPath $publicEvidencePath -Value $sanitizedEvidence -Encoding UTF8
        }
    }
    foreach ($imagePath in $evidenceImageFiles) {
        Assert-PublicPng -Path (Join-Path $repo $imagePath)
        Copy-RelativeFile -RelativePath $imagePath -SourceRoot $repo -TargetRoot $destinationFull
    }
    foreach ($documentPath in $evidenceDocumentFiles) {
        Copy-RelativeFile -RelativePath $documentPath -SourceRoot $repo -TargetRoot $destinationFull
    }
    $releaseReceiptTarget = Join-Path $destinationFull 'Release/receipt.json'
    New-Item -ItemType Directory -Path (Split-Path -Parent $releaseReceiptTarget) -Force | Out-Null
    Copy-Item -LiteralPath $releaseReceiptPath -Destination $releaseReceiptTarget

    $forbidden = @(Get-ChildItem -LiteralPath $destinationFull -Recurse -Force -File |
        Where-Object { $_.Name -match '\.log$|\.db$|\.sqlite($|\.)|\.env$|transcript|credentials|private' -or $_.FullName -match '\\(Library|Temp|Logs|UserSettings|Builds|\.git|Backups)(\\|$)' })
    if ($forbidden.Count -gt 0) { throw "Forbidden public source file(s): $($forbidden.FullName -join ', ')" }
    if (-not (Get-Command rg -ErrorAction SilentlyContinue)) { throw "ripgrep is required for the fail-closed private-path scan." }
    $normalizedRoot = [IO.Path]::GetFullPath($repo).TrimEnd('\').Replace('/', '\')
    $privatePattern = [regex]::Escape($normalizedRoot).Replace('\\', '[\\/]')
    $privatePaths = @(& rg --hidden --no-ignore -a -l -i $privatePattern -- $destinationFull 2>$null)
    $rgExit = $LASTEXITCODE
    if ($rgExit -gt 1) { throw "Private absolute-path scan failed with ripgrep exit code $rgExit." }
    if ($rgExit -eq 0 -and $privatePaths.Count -gt 0) {
        throw "Private absolute path(s) found in public source file(s): $($privatePaths -join ', ')"
    }

    $files = @(Get-ChildItem -LiteralPath $destinationFull -Recurse -Force -File |
        Where-Object { $_.Name -ne 'manifest.json' })
    $manifest = [ordered]@{
        export = 'Robo Street Soccer public source snapshot'
        evidence = @($evidenceFiles | ForEach-Object { $_.Name })
        files = @($files | ForEach-Object {
            [ordered]@{ path = (Get-RelativePathCompat -Base $destinationFull -Target $_.FullName).Replace('\', '/'); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
        })
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destinationFull 'manifest.json') -Encoding UTF8
    Write-Output "Source export: $destinationFull"
    Write-Output "Files: $($files.Count)"
}
catch {
    if (Test-Path -LiteralPath $destinationFull) {
        Assert-ContainedPath -Child $destinationFull -Root $publicRoot -Label 'Failed export cleanup target'
        Remove-Item -LiteralPath $destinationFull -Recurse -Force
    }
    throw
}
