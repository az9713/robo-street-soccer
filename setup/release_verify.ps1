param(
    [Parameter(Mandatory = $true)]
    [string]$ZipPath,

    [Parameter(Mandatory = $true)]
    [string]$EvidenceDirectory,

    [string[]]$ForbiddenPath
)

$ErrorActionPreference = "Stop"

function Assert-ContainedPath {
    param([string]$Child, [string]$Root, [string]$Label)
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $childFull = [IO.Path]::GetFullPath($Child)
    if ($childFull -eq $rootFull.TrimEnd('\') -or -not $childFull.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label escaped '$Root'."
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
    $evidenceFiles = @(Get-ChildItem -LiteralPath $evidenceRoot -File -Filter 'match-acceptance-*.json')
    if ($evidenceFiles.Count -ne 3) { throw "Expected exactly three final acceptance receipts at 1x, 0.5x and 0.25x; found $($evidenceFiles.Count)." }
    $requiredSpeeds = @(1.0, 0.5, 0.25)
    foreach ($evidenceFile in $evidenceFiles) {
        try { $evidence = Get-Content -LiteralPath $evidenceFile.FullName -Raw | ConvertFrom-Json }
        catch { throw "Acceptance receipt '$($evidenceFile.Name)' is not valid JSON." }
        if ($evidence.passed -ne $true) { throw "Acceptance receipt '$($evidenceFile.Name)' is not marked passed=true." }
        if (-not ($requiredSpeeds | Where-Object { [Math]::Abs(([double]$evidence.practiceSpeed) - $_) -lt 0.0001 })) { throw "Acceptance receipt '$($evidenceFile.Name)' has an unexpected practice speed." }
    }
    $speeds = @($evidenceFiles | ForEach-Object { [double](Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json).practiceSpeed } | Sort-Object -Unique)
    if (($speeds -join ',') -ne '0.25,0.5,1') { throw "Acceptance receipts must cover exactly 1x, 0.5x and 0.25x." }
}

function Assert-RuntimeManifest {
    param([string]$Root)
    $manifest = Join-Path $Root 'runtime-manifest.txt'
    if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) { throw 'runtime-manifest.txt is missing from the package.' }
    $expected = @(Get-ChildItem -LiteralPath $Root -Recurse -Force -File |
        Where-Object { $_.FullName -ne [IO.Path]::GetFullPath($manifest) -and $_.Extension -ne '.pdb' -and $_.Name -notlike '*BurstDebugInformation*' })
    $expectedMap = @{}
    foreach ($file in $expected) {
        $relative = (Get-RelativePathCompat -Base $Root -Target $file.FullName).Replace('\', '/')
        $expectedMap[$relative.ToLowerInvariant()] = $file
    }
    $seen = @{}
    foreach ($line in @(Get-Content -LiteralPath $manifest)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $parts = $line -split '\|', 3
        $relative = $parts[0].Replace('\', '/')
        $segments = @($relative.Split('/'))
        if ($parts.Count -ne 3 -or [IO.Path]::IsPathRooted($relative) -or $relative.StartsWith('/') -or ($segments -contains '..') -or ($segments -contains '.') -or $parts[2] -notmatch '^[0-9a-fA-F]{64}$') { throw "Malformed runtime manifest line: $line" }
        $key = $relative.ToLowerInvariant()
        if ($seen.ContainsKey($key)) { throw "Duplicate runtime manifest path: $relative" }
        if (-not $expectedMap.ContainsKey($key)) { throw "Runtime manifest names a missing or excluded file: $relative" }
        $file = $expectedMap[$key]
        if ([int64]$parts[1] -ne $file.Length) { throw "Runtime manifest byte count mismatch: $relative" }
        $actualHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $parts[2].ToLowerInvariant()) { throw "Runtime manifest hash mismatch: $relative" }
        $seen[$key] = $true
    }
    if ($seen.Count -ne $expectedMap.Count) { throw "Runtime manifest does not cover every shipped runtime file." }
}

$evidenceRoot = (Resolve-Path -LiteralPath $EvidenceDirectory).Path
Assert-FinalEvidence -Root $evidenceRoot
$zip = (Resolve-Path -LiteralPath $ZipPath).Path
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$work = Join-Path $tempRoot ("robo-street-soccer-verify-" + [Guid]::NewGuid().ToString("N"))
Assert-ContainedPath -Child $work -Root $tempRoot -Label 'Verification workspace'
if (Test-Path -LiteralPath $work) { throw "Verification workspace unexpectedly already exists." }
try {
    Expand-Archive -LiteralPath $zip -DestinationPath $work
    $executables = @(Get-ChildItem -LiteralPath $work -Recurse -File -Filter "*.exe" |
        Where-Object { $_.Name -notmatch "CrashHandler" }
    )
    if ($executables.Count -ne 1) { throw "Expected exactly one game executable; found $($executables.Count)." }
    $exe = $executables[0]
    $data = Join-Path $exe.Directory.FullName ($exe.BaseName + "_Data")
    if (-not (Test-Path -LiteralPath $data -PathType Container)) { throw "Missing Unity data directory beside the game executable." }
    if (-not (Test-Path -LiteralPath (Join-Path $exe.Directory.FullName "UnityPlayer.dll") -PathType Leaf)) { throw "Missing UnityPlayer.dll beside the game executable." }
    if (-not (Test-Path -LiteralPath (Join-Path $data 'Managed/Assembly-CSharp.dll') -PathType Leaf)) { throw 'Missing Assembly-CSharp.dll beside the game executable.' }
    Assert-RuntimeManifest -Root $exe.Directory.FullName
    $forbidden = @(Get-ChildItem -LiteralPath $work -Recurse -Force -File |
        Where-Object { $_.Name -match "\.pdb$|\.log$|\.db$|\.sqlite($|\.)|\.env$|BurstDebugInformation|profile|transcript|credentials" })
    if ($forbidden.Count -gt 0) { throw "Forbidden public-package file(s): $($forbidden.FullName -join ', ')" }
    if (-not (Get-Command rg -ErrorAction SilentlyContinue)) { throw "ripgrep is required for the fail-closed private-path scan." }
    if (-not $ForbiddenPath -or $ForbiddenPath.Count -eq 0) {
        $ForbiddenPath = @((Resolve-Path (Join-Path $evidenceRoot '..')).Path)
    }
    $privatePatternParts = @()
    foreach ($path in $ForbiddenPath) {
        $normalizedPath = [IO.Path]::GetFullPath($path).TrimEnd('\').Replace('/', '\')
        $privatePatternParts += [regex]::Escape($normalizedPath).Replace('\\', '[\\/]')
    }
    $privatePattern = '(' + ($privatePatternParts -join '|') + ')'
    $privatePaths = @(& rg --hidden --no-ignore -a -l -i $privatePattern -- $work 2>$null)
    $rgExit = $LASTEXITCODE
    if ($rgExit -gt 1) { throw "Private absolute-path scan failed with ripgrep exit code $rgExit." }
    if ($rgExit -eq 0 -and $privatePaths.Count -gt 0) {
        throw "Private absolute path(s) found in package file(s): $($privatePaths -join ', ')"
    }
    Write-Output "PASS: portable Unity runtime layout, final evidence and public-file exclusions verified."
    Write-Output "Executable: $($exe.FullName)"
    Write-Output "SHA256: $((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant())"
}
finally {
    if (Test-Path -LiteralPath $work) {
        Assert-ContainedPath -Child $work -Root $tempRoot -Label 'Verification cleanup target'
        Remove-Item -LiteralPath $work -Recurse -Force
    }
}
