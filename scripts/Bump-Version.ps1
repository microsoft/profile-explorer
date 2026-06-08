<#
.SYNOPSIS
    Bumps the Profile Explorer application version across all files that hold it.

.DESCRIPTION
    Updates the app version in:
      - src/ProfileExplorerUI/ProfileExplorerUI.csproj (3 elements: AssemblyVersion, FileVersion, Version)
      - installer/x64/prepare-installer.cmd
      - installer/arm64/prepare-installer.cmd

    Refuses to operate when the files disagree on the current version, unless
    -Force is supplied alongside -Version. Preserves file encoding (UTF-8 BOM
    where present) and line endings.

.PARAMETER Bump
    Auto-increment a single semver component. Major resets minor and patch to 0;
    Minor resets patch to 0; Patch bumps the patch only.

.PARAMETER Version
    Set an explicit version in the form Major.Minor.Patch (e.g. 1.5.0).

.PARAMETER Show
    Print the current version found in each file and exit.

.PARAMETER Force
    Bypass the drift check. Only valid with -Version; rewrites every file to the
    given version regardless of what it currently holds.

.EXAMPLE
    .\Bump-Version.ps1 -Show
    Print the current version of each file.

.EXAMPLE
    .\Bump-Version.ps1 -Bump Patch
    Bump 1.2.1 -> 1.2.2 across all files.

.EXAMPLE
    .\Bump-Version.ps1 -Bump Minor -WhatIf
    Preview a 1.2.1 -> 1.3.0 bump without writing.

.EXAMPLE
    .\Bump-Version.ps1 -Version 1.5.0
    Set every file's version to 1.5.0 (requires all files to currently agree).

.EXAMPLE
    .\Bump-Version.ps1 -Version 1.5.0 -Force
    Set every file's version to 1.5.0 even if the files currently disagree.
#>

[CmdletBinding(SupportsShouldProcess = $true, DefaultParameterSetName = 'Bump')]
param(
    [Parameter(ParameterSetName = 'Bump', Mandatory = $true)]
    [ValidateSet('Major', 'Minor', 'Patch')]
    [string]$Bump,

    [Parameter(ParameterSetName = 'Version', Mandatory = $true)]
    [string]$Version,

    [Parameter(ParameterSetName = 'Show', Mandatory = $true)]
    [switch]$Show,

    [Parameter(ParameterSetName = 'Version')]
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# Repository root is the parent of the scripts/ directory holding this file.
$RepoRoot = Split-Path -Parent $PSScriptRoot
if (-not $RepoRoot) {
    $RepoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
}

# File targets. Each entry describes one file and the regex patterns that
# locate the version string within it. ExpectedHits is asserted before we
# write -- a mismatch means the file's schema changed and the script needs
# updating.
$Targets = @(
    [pscustomobject]@{
        Name         = 'src/ProfileExplorerUI/ProfileExplorerUI.csproj'
        Path         = Join-Path $RepoRoot 'src\ProfileExplorerUI\ProfileExplorerUI.csproj'
        # Anchored on the specific element names so PackageReference <Version>
        # elements (none today, but possible) cannot be matched by accident.
        Patterns     = @(
            '(?<prefix><AssemblyVersion>)(?<ver>\d+\.\d+\.\d+)(?<suffix></AssemblyVersion>)',
            '(?<prefix><FileVersion>)(?<ver>\d+\.\d+\.\d+)(?<suffix></FileVersion>)',
            '(?<prefix><Version>)(?<ver>\d+\.\d+\.\d+)(?<suffix></Version>)'
        )
        ExpectedHits = 3
    },
    [pscustomobject]@{
        Name         = 'installer/x64/prepare-installer.cmd'
        Path         = Join-Path $RepoRoot 'installer\x64\prepare-installer.cmd'
        Patterns     = @('(?<prefix>set %_VERSION=")(?<ver>\d+\.\d+\.\d+)(?<suffix>")')
        ExpectedHits = 1
    },
    [pscustomobject]@{
        Name         = 'installer/arm64/prepare-installer.cmd'
        Path         = Join-Path $RepoRoot 'installer\arm64\prepare-installer.cmd'
        Patterns     = @('(?<prefix>set %_VERSION=")(?<ver>\d+\.\d+\.\d+)(?<suffix>")')
        ExpectedHits = 1
    }
)

function Read-FileWithEncoding {
    param([string]$Path)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $encoding = New-Object System.Text.UTF8Encoding($hasBom)
    $text = $encoding.GetString($bytes)
    if ($hasBom) {
        # GetString includes the BOM as U+FEFF when the bytes are present; strip
        # it from the content view but remember to re-add via the encoding.
        $text = $text.TrimStart([char]0xFEFF)
    }
    return [pscustomobject]@{ Text = $text; Encoding = $encoding }
}

function Write-FileWithEncoding {
    param(
        [string]$Path,
        [string]$Text,
        [System.Text.Encoding]$Encoding
    )
    [System.IO.File]::WriteAllText($Path, $Text, $Encoding)
}

function Get-VersionHits {
    param([pscustomobject]$Target, [string]$Text)

    $hits = @()
    foreach ($pattern in $Target.Patterns) {
        $matchResults = [regex]::Matches($Text, $pattern)
        foreach ($m in $matchResults) {
            # Compute 1-based line number for human-readable output.
            $line = ($Text.Substring(0, $m.Index) -split "`n").Count
            $hits += [pscustomobject]@{
                Pattern  = $pattern
                Match    = $m
                Version  = $m.Groups['ver'].Value
                Prefix   = $m.Groups['prefix'].Value
                Suffix   = $m.Groups['suffix'].Value
                Line     = $line
            }
        }
    }
    return ,$hits
}

function Test-SemVer {
    param([string]$Value)
    return $Value -match '^\d+\.\d+\.\d+$'
}

function Get-BumpedVersion {
    param([string]$Current, [string]$Component)

    $parts = $Current -split '\.'
    [int]$major = $parts[0]
    [int]$minor = $parts[1]
    [int]$patch = $parts[2]

    switch ($Component) {
        'Major' { $major++; $minor = 0; $patch = 0 }
        'Minor' { $minor++; $patch = 0 }
        'Patch' { $patch++ }
    }
    return "$major.$minor.$patch"
}

# ----- Phase 1: read every file, collect current versions -------------------

$fileStates = @()
foreach ($target in $Targets) {
    if (-not (Test-Path $target.Path)) {
        Write-Host "ERROR: File not found: $($target.Path)" -ForegroundColor Red
        exit 1
    }

    $read = Read-FileWithEncoding -Path $target.Path
    $hits = Get-VersionHits -Target $target -Text $read.Text

    if ($hits.Count -ne $target.ExpectedHits) {
        Write-Host "ERROR: $($target.Name) -- expected $($target.ExpectedHits) version match(es), found $($hits.Count)." -ForegroundColor Red
        Write-Host "       The file's schema may have changed. Update the Patterns/ExpectedHits in Bump-Version.ps1." -ForegroundColor Red
        exit 1
    }

    $distinct = @($hits.Version | Select-Object -Unique)
    if ($distinct.Count -gt 1) {
        Write-Host "ERROR: $($target.Name) -- internal version mismatch: $($distinct -join ', ')" -ForegroundColor Red
        exit 1
    }

    $fileStates += [pscustomobject]@{
        Target   = $target
        Text     = $read.Text
        Encoding = $read.Encoding
        Hits     = $hits
        Version  = $distinct[0]
    }
}

# ----- Phase 2: handle -Show ------------------------------------------------

if ($Show) {
    $allAgree = @($fileStates.Version | Select-Object -Unique).Count -eq 1
    if ($allAgree) {
        Write-Host "Current version: $($fileStates[0].Version) (consistent across $($fileStates.Count) files)" -ForegroundColor Green
    } else {
        Write-Host "Version drift detected:" -ForegroundColor Yellow
    }
    foreach ($state in $fileStates) {
        $color = if ($allAgree) { 'Gray' } else { 'Yellow' }
        Write-Host ("  {0,-55} -> {1}" -f $state.Target.Name, $state.Version) -ForegroundColor $color
    }
    exit 0
}

# ----- Phase 3: drift check (skippable via -Force + -Version) ---------------

$distinctVersions = @($fileStates.Version | Select-Object -Unique)
$drift = $distinctVersions.Count -gt 1

if ($drift -and -not ($Force -and $PSCmdlet.ParameterSetName -eq 'Version')) {
    Write-Host "ERROR: Version drift detected across files:" -ForegroundColor Red
    foreach ($state in $fileStates) {
        Write-Host ("  {0,-55} -> {1}" -f $state.Target.Name, $state.Version) -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "Refusing to bump from an inconsistent state. Options:" -ForegroundColor Red
    Write-Host "  1. Resolve drift manually, then re-run." -ForegroundColor Yellow
    Write-Host "  2. Pin an explicit target with -Version <X.Y.Z> -Force" -ForegroundColor Yellow
    Write-Host "     (this will rewrite all files to the given version regardless of current value)" -ForegroundColor Yellow
    exit 1
}

if ($Force -and -not $drift) {
    Write-Host "Note: -Force supplied but no drift detected; proceeding normally." -ForegroundColor DarkGray
}

# ----- Phase 4: compute target version --------------------------------------

$currentVersion = $fileStates[0].Version  # When drift+Force, "current" is meaningless; only -Version is allowed in that case.

if ($PSCmdlet.ParameterSetName -eq 'Bump') {
    $newVersion = Get-BumpedVersion -Current $currentVersion -Component $Bump
} else {
    if (-not (Test-SemVer $Version)) {
        Write-Host "ERROR: -Version must be in the form Major.Minor.Patch (e.g. 1.2.3). Got: $Version" -ForegroundColor Red
        exit 1
    }
    $newVersion = $Version
}

if (-not $drift -and $newVersion -eq $currentVersion) {
    Write-Host "Nothing to do: target version $newVersion already matches current version." -ForegroundColor Yellow
    exit 0
}

# ----- Phase 5: report / apply ----------------------------------------------

if (-not $drift) {
    Write-Host "Current version: $currentVersion (consistent across $($fileStates.Count) files)" -ForegroundColor Cyan
} else {
    Write-Host "Current version: (drift -- see below)" -ForegroundColor Yellow
}
$dryRunSuffix = if ($WhatIfPreference) { '  (dry run, no files modified)' } else { '' }
Write-Host "New version:     $newVersion$dryRunSuffix" -ForegroundColor Cyan
Write-Host ""

if ($WhatIfPreference) {
    Write-Host "Would update:" -ForegroundColor Cyan
} else {
    Write-Host "Updated:" -ForegroundColor Green
}

foreach ($state in $fileStates) {
    $target = $state.Target
    $text = $state.Text

    if ($WhatIfPreference) {
        Write-Host "  $($target.Name)" -ForegroundColor Gray
        foreach ($hit in $state.Hits) {
            $before = "$($hit.Prefix)$($hit.Version)$($hit.Suffix)"
            $after  = "$($hit.Prefix)$newVersion$($hit.Suffix)"
            Write-Host ("    line {0,3}: {1} -> {2}" -f $hit.Line, $before, $after) -ForegroundColor DarkGray
        }
        continue
    }

    # Apply replacements. Re-run regex against the live text on each iteration
    # to keep indices accurate after each substitution.
    $newText = $text
    $totalReplacements = 0
    foreach ($pattern in $target.Patterns) {
        $newText = [regex]::Replace($newText, $pattern, {
            param($m)
            $script:totalReplacements++
            return "$($m.Groups['prefix'].Value)$newVersion$($m.Groups['suffix'].Value)"
        })
    }

    if ($totalReplacements -ne $target.ExpectedHits) {
        Write-Host "ERROR: $($target.Name) -- expected $($target.ExpectedHits) replacement(s), made $totalReplacements. Aborting before writing." -ForegroundColor Red
        exit 1
    }

    Write-FileWithEncoding -Path $target.Path -Text $newText -Encoding $state.Encoding
    Write-Host ("  {0,-55} ({1} replacement{2})" -f $target.Name, $target.ExpectedHits, $(if ($target.ExpectedHits -eq 1) { '' } else { 's' })) -ForegroundColor Gray
}

Write-Host ""
if ($WhatIfPreference) {
    Write-Host "Dry run complete. Re-run without -WhatIf to apply." -ForegroundColor Cyan
} else {
    Write-Host "Done. Review with: git diff" -ForegroundColor Green
}
