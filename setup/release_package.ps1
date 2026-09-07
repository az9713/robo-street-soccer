param(
    [Parameter(Mandatory = $true)]
    [string]$BuildDirectory,

    [string]$OutputDirectory = "Releases",

    [string]$Version = "final",

    [Parameter(Mandatory = $true)]
    [string]$EvidenceDirectory,

    [switch]$Force
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

function Relative-PublicPath {
    param([string]$Base, [string]$Target)
    return (Get-RelativePathCompat -Base $Base -Target $Target).Replace('\', '/')
}

function Get-DeterministicRuntimeManifest {
    param([string]$Root, [string]$ManifestPath)
    $lines = New-Object 'System.Collections.Generic.List[string]'
    foreach ($file in @(Get-ChildItem -LiteralPath $Root -Recurse -Force -File |
        Where-Object { $_.FullName -ne [IO.Path]::GetFullPath($ManifestPath) -and $_.Extension -ne '.pdb' -and $_.Name -notlike '*BurstDebugInformation*' })) {
        $relative = (Get-RelativePathCompat -Base $Root -Target $file.FullName).Replace('\', '/')
        $fileHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        [void]$lines.Add("$relative|$($file.Length)|$fileHash")
    }
    $lines.Sort([StringComparer]::Ordinal)
    $text = ($lines -join "`n") + "`n"
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($ManifestPath, $text, $utf8NoBom)
    return (Get-FileHash -LiteralPath $ManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
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

function Assert-NoPersonalAbsolutePathStrings {
    param([string]$Root, [string]$PersonalRoot)
    if (-not (Get-Command rg -ErrorAction SilentlyContinue)) { throw "ripgrep is required for the fail-closed personal-path scan." }
    $normalizedRoot = [IO.Path]::GetFullPath($PersonalRoot).TrimEnd('\').Replace('/', '\')
    $privatePattern = [regex]::Escape($normalizedRoot).Replace('\\', '[\\/]')
    $matches = @(& rg --hidden --no-ignore -a -l -i $privatePattern -- $Root 2>$null)
    $rgExit = $LASTEXITCODE
    if ($rgExit -gt 1) { throw "Personal-path scan failed with ripgrep exit code $rgExit." }
    if ($rgExit -eq 0 -and $matches.Count -gt 0) { throw "Personal absolute path(s) found in release payload: $($matches -join ', ')" }
}

if ($Version -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$' -or $Version.Contains('..')) {
    throw "Version must be a safe filename slug without path traversal."
}

$build = (Resolve-Path -LiteralPath $BuildDirectory).Path
$executables = @(Get-ChildItem -LiteralPath $build -File -Filter "*.exe" |
    Where-Object { $_.Name -notmatch "CrashHandler" } |
    Select-Object)
if ($executables.Count -ne 1) { throw "Expected exactly one game executable directly inside '$build'; found $($executables.Count)." }
$exe = $executables[0]

$evidenceRoot = (Resolve-Path -LiteralPath $EvidenceDirectory).Path
$evidenceFiles = @(Assert-FinalEvidence -Root $evidenceRoot)

$dataDirectory = Join-Path $build ($exe.BaseName + "_Data")
if (-not (Test-Path -LiteralPath $dataDirectory -PathType Container)) {
    throw "The Unity data directory '$($exe.BaseName)_Data' is missing beside the executable."
}
if (-not (Test-Path -LiteralPath (Join-Path $build "UnityPlayer.dll") -PathType Leaf)) {
    throw "UnityPlayer.dll is missing beside the executable."
}
$assemblyPath = Join-Path $dataDirectory 'Managed/Assembly-CSharp.dll'
if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
    throw "Managed/Assembly-CSharp.dll is missing beside the executable."
}
$buildHash = (Get-FileHash -LiteralPath $exe.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$assemblyHash = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash.ToLowerInvariant()
$buildReceipt = Get-Content -LiteralPath (Join-Path $evidenceRoot 'match-windows-build.json') -Raw | ConvertFrom-Json
if ($buildReceipt.sha256.ToLowerInvariant() -ne $buildHash) {
    throw "Build receipt sha256 does not match the supplied executable."
}
if ($buildReceipt.assemblySha256.ToLowerInvariant() -ne $assemblyHash) {
    throw "Build receipt assemblySha256 does not match the supplied Assembly-CSharp.dll."
}

$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$packageName = "RoboStreetSoccer-$Version-Windows"
$packageDirectory = Join-Path $outputRoot $packageName
$zipPath = Join-Path $outputRoot ($packageName + ".zip")
Assert-ContainedPath -Child $packageDirectory -Root $outputRoot -Label 'Package directory'
Assert-ContainedPath -Child $zipPath -Root $outputRoot -Label 'ZIP path'
if ((Test-Path -LiteralPath $packageDirectory) -or (Test-Path -LiteralPath $zipPath)) {
    if (-not $Force) { throw "Release output already exists. Choose another -Version or pass -Force after checking the target." }
    if (Test-Path -LiteralPath $packageDirectory) { Remove-Item -LiteralPath $packageDirectory -Recurse -Force }
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
}
New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null

# Unity desktop players need the executable, its _Data directory, UnityPlayer,
# and the managed runtime. Copy only these runtime groups; omit editor caches,
# logs, debug symbols and the Burst debug-information bundle.
Copy-Item -LiteralPath $exe.FullName -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $build "UnityPlayer.dll") -Destination $packageDirectory
foreach ($name in @("UnityCrashHandler64.exe", "WinPixEventRuntime.dll", "d3d12.dll", "d3d12core.dll", "dstorage.dll", "dstoragecore.dll")) {
    $candidate = Join-Path $build $name
    if (Test-Path -LiteralPath $candidate -PathType Leaf) { Copy-Item -LiteralPath $candidate -Destination $packageDirectory }
}
Copy-Item -LiteralPath $dataDirectory -Destination $packageDirectory -Recurse
$mono = Join-Path $build "MonoBleedingEdge"
if (Test-Path -LiteralPath $mono -PathType Container) { Copy-Item -LiteralPath $mono -Destination $packageDirectory -Recurse }
$d3d12 = Join-Path $build "D3D12"
if (Test-Path -LiteralPath $d3d12 -PathType Container) { Copy-Item -LiteralPath $d3d12 -Destination $packageDirectory -Recurse }

# Preserve runtime bytes exactly. Personal paths in compiled metadata must be
# fixed at the build/source boundary; packaging never rewrites Unity or vendor
# binaries. The verifier scans the untouched copied payload.
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Assert-NoPersonalAbsolutePathStrings -Root $packageDirectory -PersonalRoot $repoRoot

# Symbols and Burst diagnostics are not needed to run the game and can expose
# build details or inflate the public archive.
Get-ChildItem -LiteralPath $packageDirectory -Recurse -Force -File |
    Where-Object { $_.Extension -eq ".pdb" -or $_.Name -like "*BurstDebugInformation*" } |
    Remove-Item -Force

$forbidden = @(Get-ChildItem -LiteralPath $packageDirectory -Recurse -Force -File |
    Where-Object { $_.Name -match '\.pdb$|\.log$|\.db$|\.sqlite($|\.)|\.env$|BurstDebugInformation|transcript|credentials|profile' })
if ($forbidden.Count -gt 0) {
    throw "Forbidden runtime package file(s): $($forbidden.FullName -join ', ')"
}

$instructions = @"
Robo Street Soccer — Windows portable build

Run $($exe.Name). Keep the executable beside its $($exe.BaseName)_Data folder.
This package is portable and does not install a Unity editor or service.

Controls and mode details are in the repository README.md.
"@
Set-Content -LiteralPath (Join-Path $packageDirectory "PLAY.txt") -Value $instructions -Encoding UTF8
$runtimeManifestPath = Join-Path $packageDirectory 'runtime-manifest.txt'
$runtimeManifestHash = Get-DeterministicRuntimeManifest -Root $packageDirectory -ManifestPath $runtimeManifestPath

Compress-Archive -LiteralPath $packageDirectory -DestinationPath $zipPath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$size = (Get-Item -LiteralPath $zipPath).Length
$buildEvidencePath = Join-Path $evidenceRoot 'match-windows-build.json'
$acceptanceEvidenceFiles = @(Get-ChildItem -LiteralPath $evidenceRoot -File -Filter 'match-acceptance-*.json')
$receipt = [ordered]@{
    package = (Split-Path -Leaf $zipPath)
    sha256 = $hash
    bytes = $size
    sourceBuild = (Relative-PublicPath -Base ((Resolve-Path (Join-Path $PSScriptRoot '..')).Path) -Target $exe.FullName)
    sourceBuildSha256 = $buildHash
    assemblySha256 = $assemblyHash
    runtimeManifestSha256 = $runtimeManifestHash
    buildEvidence = [ordered]@{ name = 'match-windows-build.json'; sha256 = (Get-FileHash -LiteralPath $buildEvidencePath -Algorithm SHA256).Hash.ToLowerInvariant() }
    acceptanceEvidence = @($acceptanceEvidenceFiles | ForEach-Object {
        [ordered]@{ name = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    excluded = @("UnityProject/Library", "UnityProject/Temp", "UnityProject/Logs", "*.pdb", "*BurstDebugInformation*", "*.log", ".env", "credentials", "private profiles")
}
$receipt | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputRoot ($packageName + ".json")) -Encoding UTF8
Write-Output "Package: $zipPath"
Write-Output "SHA256:  $hash"
Write-Output "Bytes:   $size"
