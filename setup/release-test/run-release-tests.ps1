$ErrorActionPreference = "Stop"

function Expect-Failure {
    param([string]$Name, [scriptblock]$Action)
    $failed = $false
    try { & $Action }
    catch { $failed = $true; Write-Output "PASS: $Name" }
    if (-not $failed) { throw "Expected failure did not occur: $Name" }
}

function New-SyntheticBuild {
    param([string]$Path, [bool]$WithData = $true)
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $Path 'RoboStreetSoccer.exe') -Value 'synthetic executable' -Encoding ASCII
    Set-Content -LiteralPath (Join-Path $Path 'UnityPlayer.dll') -Value 'synthetic player' -Encoding ASCII
    if ($WithData) {
        New-Item -ItemType Directory -Path (Join-Path $Path 'RoboStreetSoccer_Data') -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $Path 'RoboStreetSoccer_Data/globalgamemanagers.assets') -Value 'synthetic data' -Encoding ASCII
        New-Item -ItemType Directory -Path (Join-Path $Path 'RoboStreetSoccer_Data/Managed') -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $Path 'RoboStreetSoccer_Data/Managed/Assembly-CSharp.dll') -Value 'synthetic gameplay assembly' -Encoding ASCII
    }
}

function New-SyntheticEvidence {
    param([string]$Path)
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    @{ result = 'Succeeded'; errors = 0; sha256 = ('a' * 64); assemblySha256 = ('b' * 64); synthetic = $true } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Path 'match-windows-build.json') -Encoding UTF8
    foreach ($speed in @(1.0, 0.5, 0.25)) {
        @{ passed = $true; practiceSpeed = $speed; synthetic = $true } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Path ("match-acceptance-$($speed.ToString('0.00')).json")) -Encoding UTF8
    }
}

$work = Join-Path $PSScriptRoot '.work'
if (Test-Path -LiteralPath $work) { throw "Test workspace already exists; remove it after inspecting the prior run." }
New-Item -ItemType Directory -Path $work -Force | Out-Null
$packageScript = Join-Path $PSScriptRoot '..\release_package.ps1'
$verifyScript = Join-Path $PSScriptRoot '..\release_verify.ps1'
$exportScript = Join-Path $PSScriptRoot '..\release_export_source.ps1'
try {
    $evidence = Join-Path $work 'evidence'
    New-SyntheticEvidence -Path $evidence

    $missingDataBuild = Join-Path $work 'missing-data-build'
    New-SyntheticBuild -Path $missingDataBuild -WithData $false
    Expect-Failure 'package rejects missing Unity data directory' {
        & $packageScript -BuildDirectory $missingDataBuild -OutputDirectory (Join-Path $work 'missing-data-output') -Version missingdata -EvidenceDirectory $evidence
    }

    $validBuild = Join-Path $work 'valid-build'
    New-SyntheticBuild -Path $validBuild
    $syntheticBuildReceipt = Get-Content -LiteralPath (Join-Path $evidence 'match-windows-build.json') -Raw | ConvertFrom-Json
    $syntheticBuildReceipt.sha256 = (Get-FileHash -LiteralPath (Join-Path $validBuild 'RoboStreetSoccer.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
    $syntheticBuildReceipt.assemblySha256 = (Get-FileHash -LiteralPath (Join-Path $validBuild 'RoboStreetSoccer_Data/Managed/Assembly-CSharp.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
    $syntheticBuildReceipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'match-windows-build.json') -Encoding UTF8
    $assemblyMismatchReceipt = Get-Content -LiteralPath (Join-Path $evidence 'match-windows-build.json') -Raw | ConvertFrom-Json
    $assemblyMismatchReceipt.assemblySha256 = 'c' * 64
    $assemblyMismatchReceipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'match-windows-build.json') -Encoding UTF8
    Expect-Failure 'package rejects mismatched gameplay assembly hash' {
        & $packageScript -BuildDirectory $validBuild -OutputDirectory (Join-Path $work 'assembly-mismatch-output') -Version assemblymismatch -EvidenceDirectory $evidence
    }
    $syntheticBuildReceipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'match-windows-build.json') -Encoding UTF8
    $validOutput = Join-Path $work 'valid-output'
    $validPlayerHash = (Get-FileHash -LiteralPath (Join-Path $validBuild 'UnityPlayer.dll') -Algorithm SHA256).Hash
    & $packageScript -BuildDirectory $validBuild -OutputDirectory $validOutput -Version synthetic -EvidenceDirectory $evidence
    & $verifyScript -ZipPath (Join-Path $validOutput 'RoboStreetSoccer-synthetic-Windows.zip') -EvidenceDirectory $evidence
    $validExtract = Join-Path $work 'valid-extract'
    Expand-Archive -LiteralPath (Join-Path $validOutput 'RoboStreetSoccer-synthetic-Windows.zip') -DestinationPath $validExtract
    $packagedPlayerHash = (Get-FileHash -LiteralPath (Join-Path $validExtract 'RoboStreetSoccer-synthetic-Windows/UnityPlayer.dll') -Algorithm SHA256).Hash
    if ($packagedPlayerHash -ne $validPlayerHash) { throw 'Package changed a clean runtime binary hash.' }
    Write-Output 'PASS: synthetic portable package and receipt verification'

    $privateBuild = Join-Path $work 'private-build'
    New-SyntheticBuild -Path $privateBuild
    $syntheticPrivatePath = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path + [char]92 + 'Library' + [char]92 + 'generated.pdb'
    Add-Content -LiteralPath (Join-Path $privateBuild 'UnityPlayer.dll') -Value $syntheticPrivatePath -Encoding ASCII
    $privateBeforeHash = (Get-FileHash -LiteralPath (Join-Path $privateBuild 'UnityPlayer.dll') -Algorithm SHA256).Hash
    Expect-Failure 'package rejects personal project paths without mutating the binary' {
        & $packageScript -BuildDirectory $privateBuild -OutputDirectory (Join-Path $work 'private-output') -Version privatepath -EvidenceDirectory $evidence
    }
    if ((Get-FileHash -LiteralPath (Join-Path $privateBuild 'UnityPlayer.dll') -Algorithm SHA256).Hash -ne $privateBeforeHash) { throw 'Binary hash changed during failed package validation.' }

    $emptyEvidence = Join-Path $work 'empty-evidence'
    New-Item -ItemType Directory -Path $emptyEvidence -Force | Out-Null
    Expect-Failure 'package rejects missing final match evidence' {
        & $packageScript -BuildDirectory $validBuild -OutputDirectory (Join-Path $work 'no-evidence-output') -Version noevidence -EvidenceDirectory $emptyEvidence
    }
    Expect-Failure 'source export rejects missing final match evidence' {
        & $exportScript -RepositoryRoot (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path -Destination (Join-Path $work 'source-export') -EvidenceDirectory $emptyEvidence
    }

    $privateRoot = Join-Path $work 'private-package'
    New-SyntheticBuild -Path $privateRoot
    $syntheticPrivatePath = 'C:' + [char]92 + 'Users' + [char]92 + 'private' + [char]92 + 'asset.blend'
    Set-Content -LiteralPath (Join-Path $privateRoot 'private-asset.fbx') -Value $syntheticPrivatePath -Encoding ASCII
    $privateZip = Join-Path $work 'private-package.zip'
    Compress-Archive -LiteralPath $privateRoot -DestinationPath $privateZip
    Expect-Failure 'verifier rejects private absolute paths in binary/text assets' {
        & $verifyScript -ZipPath $privateZip -EvidenceDirectory $evidence -ForbiddenPath ('C:' + [char]92 + 'Users' + [char]92 + 'private')
    }

    Write-Output 'PASS: release fixture audit complete; no production receipt was created.'
}
finally {
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}
