<#
.SYNOPSIS
    Analyzes Profile Explorer diagnostic log files to summarize symbol loading issues.

.DESCRIPTION
    This script parses Profile Explorer diagnostic logs (generated when PROFILE_EXPLORER_DEBUG=1)
    and provides a comprehensive summary of symbol loading performance, failures, and recommendations.

.PARAMETER LogPath
    Path to the diagnostic log file. If not specified, uses the most recent log in %TEMP%.

.EXAMPLE
    .\Analyze-DiagnosticLog.ps1
    Analyzes the most recent diagnostic log.

.EXAMPLE
    .\Analyze-DiagnosticLog.ps1 -LogPath "C:\temp\ProfileExplorer_Diagnostic_20260109.log"
    Analyzes a specific log file.
#>

param(
    [string]$LogPath
)

# Find the most recent log if not specified
if (-not $LogPath) {
    $logFiles = Get-ChildItem "$env:TEMP\ProfileExplorer_Diagnostic_*.log" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending
    if ($logFiles.Count -eq 0) {
        Write-Host "No diagnostic logs found in $env:TEMP" -ForegroundColor Red
        Write-Host "Set PROFILE_EXPLORER_DEBUG=1 and load a trace to generate logs." -ForegroundColor Yellow
        exit 1
    }
    $LogPath = $logFiles[0].FullName
    Write-Host "Using most recent log: $LogPath" -ForegroundColor Cyan
}

if (-not (Test-Path $LogPath)) {
    Write-Host "Log file not found: $LogPath" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host ("=" * 80) -ForegroundColor Cyan
Write-Host "  PROFILE EXPLORER DIAGNOSTIC LOG ANALYZER" -ForegroundColor Cyan
Write-Host ("=" * 80) -ForegroundColor Cyan
Write-Host ""

$fileInfo = Get-Item $LogPath
Write-Host "Log File: $LogPath"
Write-Host "Size: $([math]::Round($fileInfo.Length / 1MB, 2)) MB"
Write-Host "Created: $($fileInfo.CreationTime)"
Write-Host ""

# Read log file
Write-Host "Reading log file..." -ForegroundColor Gray
$log = Get-Content $LogPath

# ============================================================================
# CRITICAL ERROR CHECK - DIA SDK Registration
# ============================================================================
$diaError = $log | Where-Object { $_ -match 'DIA SDK.*is not registered|msdia140\.dll.*not registered|\[CRITICAL\].*DIA' } | Select-Object -First 1
if ($diaError) {
    Write-Host ""
    Write-Host ("!" * 80) -ForegroundColor Red
    Write-Host "  CRITICAL ERROR: DIA SDK NOT REGISTERED" -ForegroundColor Red
    Write-Host ("!" * 80) -ForegroundColor Red
    Write-Host ""
    Write-Host "  The DIA SDK (msdia140.dll) is not registered as a COM component." -ForegroundColor Red
    Write-Host "  This prevents ALL symbol resolution - no function names will be displayed." -ForegroundColor Red
    Write-Host ""
    Write-Host "  TO FIX (run as Administrator):" -ForegroundColor Yellow
    Write-Host "    regsvr32 `"<ProfileExplorer_Install_Path>\msdia140.dll`"" -ForegroundColor White
    Write-Host ""
    Write-Host "  Or for dev builds:" -ForegroundColor Yellow
    Write-Host "    regsvr32 `"C:\src\profile-explorer\src\ProfileExplorerUI\bin\Release\net8.0-windows\msdia140.dll`"" -ForegroundColor White
    Write-Host ""
    Write-Host ("!" * 80) -ForegroundColor Red
    Write-Host ""
}

# Categorize lines
$infoLines = $log | Where-Object { $_ -match '\[Information\]' }
$warningLines = $log | Where-Object { $_ -match '\[Warning\]' }
$debugLines = $log | Where-Object { $_ -match '\[Debug\]' }
$errorLines = $log | Where-Object { $_ -match '\[Error\]' }

# ============================================================================
# TRACE INFO & SYMBOL SERVER STATUS
# ============================================================================
Write-Host ("-" * 80) -ForegroundColor DarkGray
Write-Host "  TRACE INFO & SYMBOL SERVER STATUS" -ForegroundColor Yellow
Write-Host ("-" * 80) -ForegroundColor DarkGray

# Check for ImageID events
$hasImageIdEvents = $log | Where-Object { $_ -match 'HasImageIdEvents=True' } | Select-Object -First 1
$noImageIdEvents = $log | Where-Object { $_ -match 'HasImageIdEvents=False|Trace has no ImageID DbgID events' } | Select-Object -First 1
$symbolServerDisabled = $log | Where-Object { $_ -match 'Symbol server disabled|Disabling symbol server|SourceServerEnabled=False' } | Select-Object -First 1
$symbolServerEnabled = $log | Where-Object { $_ -match 'SourceServerEnabled=True' } | Select-Object -First 1

# ImageID event counts - parse from log format:
# "[TraceLoad] Event counts: ImageLoad=X (Y with timestamp), ImageID=Z, ImageID_DbgID=W"
$imageLoadCount = 0
$imageIdCount = 0
$dbgIdCount = 0

$eventCountLine = $log | Where-Object { $_ -match '\[TraceLoad\] Event counts:' } | Select-Object -First 1
if ($eventCountLine) {
    if ($eventCountLine -match 'ImageLoad=(\d+)') {
        $imageLoadCount = [int]$Matches[1]
    }
    if ($eventCountLine -match 'ImageID=(\d+)') {
        $imageIdCount = [int]$Matches[1]
    }
    if ($eventCountLine -match 'ImageID_DbgID=(\d+)') {
        $dbgIdCount = [int]$Matches[1]
    }
}

# Parse Microsoft module info from [SymbolLoading] logs
# Format: "[SymbolLoading]   1. modulename.dll: 12345 samples [Microsoft]"
$microsoftModules = @{}
$log | Where-Object { $_ -match '\[SymbolLoading\].*\d+\.\s+([^:]+):\s+\d+\s+samples' } | ForEach-Object {
    if ($_ -match '\[SymbolLoading\].*\d+\.\s+([^:]+):\s+(\d+)\s+samples(.*)$') {
        $moduleName = $Matches[1].Trim()
        $isMicrosoft = $Matches[3] -match '\[Microsoft\]'
        $microsoftModules[$moduleName.ToLower()] = $isMicrosoft
    }
}

Write-Host ""
# Only report "MISSING" if we explicitly found evidence of missing events
# (not just because we couldn't parse the count)
$knownMissing = $noImageIdEvents -or ($eventCountLine -and $dbgIdCount -eq 0)
$knownPresent = $hasImageIdEvents -or ($eventCountLine -and $dbgIdCount -gt 0)

if ($knownMissing) {
    Write-Host "[!] MISSING ImageID DbgID EVENTS" -ForegroundColor Red
    Write-Host "    This trace is missing ImageID DbgID (RSDS) events which contain PDB GUID/Age." -ForegroundColor Yellow
    Write-Host "    Without these events, symbol server lookups are impossible because PDB matching" -ForegroundColor Yellow
    Write-Host "    requires the exact GUID+Age from the trace (not extracted from binaries)." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "    Root cause: The trace was likely captured without the right ETW providers enabled." -ForegroundColor Gray
    Write-Host "    Solution: Re-capture using Profile Explorer's built-in capture (File -> Record Profile)" -ForegroundColor Cyan
    Write-Host "              or from command line: wpr -start CPU" -ForegroundColor Cyan
    Write-Host ""
} elseif ($knownPresent) {
    Write-Host "[OK] Trace has ImageID DbgID events (PDB GUID/Age available)" -ForegroundColor Green
    if ($dbgIdCount -gt 0) {
        Write-Host "     ImageID_DbgID event count: $dbgIdCount" -ForegroundColor Gray
    }
} else {
    Write-Host "[?] Could not determine ImageID event status from log" -ForegroundColor Yellow
}

if ($symbolServerDisabled) {
    Write-Host "[!] SYMBOL SERVER DISABLED" -ForegroundColor Yellow
    Write-Host "    Symbol server lookups were disabled (either by user or due to missing ImageID events)." -ForegroundColor Gray
    Write-Host "    Only local/cached symbols will be used." -ForegroundColor Gray
    Write-Host ""
} elseif ($symbolServerEnabled) {
    Write-Host "[OK] Symbol server enabled" -ForegroundColor Green
}

Write-Host ""

# ============================================================================
# PHASE TIMING ANALYSIS
# ============================================================================
Write-Host ("-" * 80) -ForegroundColor DarkGray
Write-Host "  PHASE TIMING ANALYSIS" -ForegroundColor Yellow
Write-Host ("-" * 80) -ForegroundColor DarkGray

# Find phase markers
$loadStart = $log | Where-Object { $_ -match 'Starting LoadBinaryAndDebugFiles' } | Select-Object -First 1
$loadComplete = $log | Where-Object { $_ -match 'LoadBinaryAndDebugFiles completed' } | Select-Object -First 1
$binaryPhaseStart = $log | Where-Object { $_ -match 'Binary download phase: Started' } | Select-Object -First 1
$binaryPhaseEnd = $log | Where-Object { $_ -match 'All binary downloads completed|Binary download complete' } | Select-Object -First 1
$pdbPhaseStart = $log | Where-Object { $_ -match 'PDB download phase: Started' } | Select-Object -First 1
$pdbPhaseEnd = $log | Where-Object { $_ -match 'All PDB downloads completed|PDB download complete' } | Select-Object -First 1

function Get-TimestampFromLine($line) {
    if ($line -match '^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})') {
        return [datetime]::ParseExact($Matches[1], "yyyy-MM-dd HH:mm:ss.fff", $null)
    }
    return $null
}

$startTime = Get-TimestampFromLine $loadStart
$endTime = Get-TimestampFromLine $loadComplete
$binStartTime = Get-TimestampFromLine $binaryPhaseStart
$binEndTime = Get-TimestampFromLine $binaryPhaseEnd
$pdbStartTime = Get-TimestampFromLine $pdbPhaseStart
$pdbEndTime = Get-TimestampFromLine $pdbPhaseEnd

# Extract embedded time from log if available: "LoadBinaryAndDebugFiles completed in X.Xs"
$loadCompleteEmbeddedTime = $null
if ($loadComplete -match 'completed in (\d+\.?\d*)s') {
    $loadCompleteEmbeddedTime = [double]$Matches[1]
}

# Calculate total time - use embedded time, or timestamp diff, but sanity check against PDB time later
$totalTime = $null
if ($loadCompleteEmbeddedTime) {
    $totalTime = $loadCompleteEmbeddedTime
} elseif ($startTime -and $endTime) {
    $totalTime = ($endTime - $startTime).TotalSeconds
}

if ($binStartTime -and $binEndTime) {
    $binTime = ($binEndTime - $binStartTime).TotalSeconds
    Write-Host "  Binary Download Phase:   " -NoNewline
    Write-Host ("{0:F1}s" -f $binTime) -ForegroundColor $(if ($binTime -gt 60) { "Red" } elseif ($binTime -gt 30) { "Yellow" } else { "Green" })
}

# Calculate PDB/Symbol loading time
# Try multiple approaches since log format may vary

# Approach 1: Find time from "Starting PDB/symbol file search" to last PDBDebugInfo BEFORE lazy load
$pdbStartLine = $log | Where-Object { $_ -match '\[SymbolLoading\].*Starting PDB' } | Select-Object -First 1
$pdbStartTs = Get-TimestampFromLine $pdbStartLine

# Find the line index where lazy load starts (if any) - we only want PDBDebugInfo before this
$lazyLoadLineIndex = $null
for ($i = 0; $i -lt $log.Count; $i++) {
    if ($log[$i] -match '\[LazyBinaryLoad\]') {
        $lazyLoadLineIndex = $i
        break
    }
}

# Get last PDBDebugInfo line BEFORE lazy load (or last overall if no lazy load)
$pdbEndLine = $null
if ($lazyLoadLineIndex) {
    $pdbEndLine = $log[0..($lazyLoadLineIndex-1)] | Where-Object { $_ -match '\[PDBDebugInfo\]' } | Select-Object -Last 1
} else {
    $pdbEndLine = $log | Where-Object { $_ -match '\[PDBDebugInfo\]' } | Select-Object -Last 1
}
$pdbEndTs = Get-TimestampFromLine $pdbEndLine

# Approach 2: Try to extract embedded time from log message
$pdbTimeLine = $log | Where-Object { $_ -match 'PDB downloads? completed? in (\d+\.?\d*)s|PDB download complete:.*in (\d+\.?\d*)s' } | Select-Object -First 1
$pdbEmbeddedTime = $null
if ($pdbTimeLine -match 'in (\d+\.?\d*)s') {
    $pdbEmbeddedTime = [double]$Matches[1]
}

$pdbTime = $null
if ($pdbStartTs -and $pdbEndTs) {
    $pdbTime = ($pdbEndTs - $pdbStartTs).TotalSeconds
} elseif ($pdbEmbeddedTime) {
    $pdbTime = $pdbEmbeddedTime
}

# Display timing - use PDB time as authoritative if total time seems wrong
if ($pdbTime -and $pdbTime -gt 0.5) {
    # If total time < PDB time, use PDB time as total (old logs may have wrong embedded time)
    if ($totalTime -and $totalTime -lt $pdbTime) {
        $totalTime = $pdbTime
    }
}

if ($totalTime -and $totalTime -gt 0.5) {
    Write-Host "Total Symbol Loading Time: " -NoNewline
    Write-Host ("{0:F1}s" -f $totalTime) -ForegroundColor $(if ($totalTime -gt 120) { "Red" } elseif ($totalTime -gt 60) { "Yellow" } else { "Green" })
}

if ($pdbTime -and $pdbTime -gt 0.5) {
    Write-Host "  PDB/Symbol Loading:      " -NoNewline
    Write-Host ("{0:F1}s" -f $pdbTime) -ForegroundColor $(if ($pdbTime -gt 60) { "Red" } elseif ($pdbTime -gt 30) { "Yellow" } else { "Green" })
}

# Count timeouts vs successes
$binaryTimeouts = ($log | Where-Object { $_ -match '\[BinarySearch\] TIMEOUT' }).Count
$binaryFound = ($log | Where-Object { $_ -match '\[BinarySearch\] Found binary for' }).Count
$binaryFailed = ($log | Where-Object { $_ -match '\[BinarySearch\] Failed to find binary' }).Count
$binarySkipped = ($log | Where-Object { $_ -match '\[BinarySearch\] SKIPPED' }).Count
$binarySkippedDisabled = ($log | Where-Object { $_ -match '\[SymbolLoading\] Symbol server disabled - skipping binary downloads' }).Count

$pdbTimeouts = ($log | Where-Object { $_ -match '\[SymbolSearch\] TIMEOUT' }).Count
$pdbFound = ($log | Where-Object { $_ -match '\[SymbolSearch\] Successfully found symbol' }).Count
$pdbFailed = ($log | Where-Object { $_ -match '\[SymbolSearch\] Failed to find symbol' }).Count
$pdbSkipped = ($log | Where-Object { $_ -match '\[SymbolLoading\] Skipping PDB lookup' }).Count
$pdbSkippedDisabled = ($log | Where-Object { $_ -match '\[SymbolLoading\] Symbol server disabled' }).Count

Write-Host ""
Write-Host "Binary Search Results:" -ForegroundColor Cyan
if ($binarySkippedDisabled -gt 0) {
    Write-Host "  [Symbol server disabled - all downloads skipped]" -ForegroundColor Yellow
} else {
    Write-Host "  Found:    $binaryFound" -ForegroundColor Green
    Write-Host "  Failed:   $binaryFailed" -ForegroundColor Yellow
    Write-Host "  Timeouts: $binaryTimeouts" -ForegroundColor $(if ($binaryTimeouts -gt 10) { "Red" } else { "Yellow" })
    Write-Host "  Skipped:  $binarySkipped" -ForegroundColor Gray
}

Write-Host ""
Write-Host "PDB Search Results:" -ForegroundColor Cyan
if ($pdbSkippedDisabled -gt 0 -and $pdbFound -eq 0) {
    Write-Host "  [Symbol server disabled - no PDB lookups attempted]" -ForegroundColor Yellow
} else {
    Write-Host "  Found:    $pdbFound" -ForegroundColor Green
    Write-Host "  Failed:   $pdbFailed" -ForegroundColor Yellow
    Write-Host "  Timeouts: $pdbTimeouts" -ForegroundColor $(if ($pdbTimeouts -gt 10) { "Red" } else { "Yellow" })
    Write-Host "  Skipped (company filter): $pdbSkipped" -ForegroundColor Gray
}

Write-Host ""

Write-Host ("-" * 80) -ForegroundColor DarkGray
Write-Host "  LOG STATISTICS" -ForegroundColor Yellow
Write-Host ("-" * 80) -ForegroundColor DarkGray
Write-Host "Total Lines:    $($log.Count)"
Write-Host "  Information:  $($infoLines.Count)" -ForegroundColor Green
Write-Host "  Warnings:     $($warningLines.Count)" -ForegroundColor Yellow
Write-Host "  Debug:        $($debugLines.Count)" -ForegroundColor Gray
Write-Host "  Errors:       $($errorLines.Count)" -ForegroundColor Red
Write-Host ""

# Analyze NOT_RESOLVED failures
$notResolved = $warningLines | Where-Object { $_ -match 'NOT_RESOLVED' }
$failedModules = @{}
$notResolved | ForEach-Object {
    if ($_ -match 'Module: ([^,]+),') {
        $module = $Matches[1]
        if ($failedModules.ContainsKey($module)) {
            $failedModules[$module]++
        } else {
            $failedModules[$module] = 1
        }
    }
}

Write-Host ("-" * 80) -ForegroundColor DarkGray
Write-Host "  SYMBOL RESOLUTION FAILURES" -ForegroundColor Yellow
Write-Host ("-" * 80) -ForegroundColor DarkGray
Write-Host "Total unresolved symbols: $($notResolved.Count)" -ForegroundColor Red
Write-Host "Unique modules with failures: $($failedModules.Count)"
Write-Host ""

# Top failed modules
Write-Host "Top 30 Modules Missing Symbols:" -ForegroundColor Yellow
Write-Host ""
$sortedFailures = $failedModules.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 30
$totalFailures = ($failedModules.Values | Measure-Object -Sum).Sum

foreach ($item in $sortedFailures) {
    $pct = [math]::Round(($item.Value / $totalFailures) * 100, 1)
    $bar = "#" * [math]::Min([int]($pct / 2), 40)
    $color = if ($item.Value -gt 500) { "Red" } elseif ($item.Value -gt 100) { "Yellow" } else { "White" }
    Write-Host ("{0,6} ({1,5}%) {2,-30} {3}" -f $item.Value, $pct, $item.Key, $bar) -ForegroundColor $color
}

# Analyze successful resolutions
$resolved = $infoLines | Where-Object { $_ -match 'resolved via debug info|found in cache|found via PDB query' }
$successModules = @{}
$resolved | ForEach-Object {
    if ($_ -match 'Module: ([^,]+),') {
        $module = $Matches[1]
        if ($successModules.ContainsKey($module)) {
            $successModules[$module]++
        } else {
            $successModules[$module] = 1
        }
    }
}

Write-Host ""
Write-Host ("-" * 80) -ForegroundColor DarkGray
Write-Host "  SUCCESSFUL SYMBOL RESOLUTION" -ForegroundColor Yellow
Write-Host ("-" * 80) -ForegroundColor DarkGray
Write-Host "Total resolved symbols: $($resolved.Count)" -ForegroundColor Green
Write-Host ""

Write-Host "Top 20 Modules With Symbols:" -ForegroundColor Green
$sortedSuccess = $successModules.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 20
foreach ($item in $sortedSuccess) {
    Write-Host ("{0,6} - {1}" -f $item.Value, $item.Key) -ForegroundColor Green
}

# Analyze binary loading times
$slowBinaries = $log | Where-Object { $_ -match '\[BinaryLoading\].*Duration:' }
$binaryTimes = @{}
$slowBinaries | ForEach-Object {
    if ($_ -match 'Binary: ([^,]+).*Duration: (\d+)ms') {
        $binary = $Matches[1]
        $duration = [int]$Matches[2]
        if (-not $binaryTimes.ContainsKey($binary) -or $binaryTimes[$binary] -lt $duration) {
            $binaryTimes[$binary] = $duration
        }
    }
}

if ($binaryTimes.Count -gt 0) {
    Write-Host ""
    Write-Host ("-" * 80) -ForegroundColor DarkGray
    Write-Host "  SLOW BINARY LOADING (>500ms)" -ForegroundColor Yellow
    Write-Host ("-" * 80) -ForegroundColor DarkGray
    
    $slowest = $binaryTimes.GetEnumerator() | Where-Object { $_.Value -gt 500 } | Sort-Object Value -Descending | Select-Object -First 20
    if ($slowest.Count -gt 0) {
        foreach ($item in $slowest) {
            $secs = [math]::Round($item.Value / 1000, 2)
            $color = if ($item.Value -gt 5000) { "Red" } elseif ($item.Value -gt 2000) { "Yellow" } else { "White" }
            Write-Host ("{0,8}ms ({1,5}s) - {2}" -f $item.Value, $secs, $item.Key) -ForegroundColor $color
        }
    } else {
        Write-Host "No slow binary loads detected." -ForegroundColor Green
    }
}

# Analyze PDB loading
$pdbLoads = $log | Where-Object { $_ -match '\[PDBDebugInfo\]' }
$pdbCacheHits = ($pdbLoads | Where-Object { $_ -match 'found in cache' }).Count
$pdbQueries = ($pdbLoads | Where-Object { $_ -match 'found via PDB query' }).Count
$pdbAlreadyLoaded = ($pdbLoads | Where-Object { $_ -match 'already loaded' }).Count

Write-Host ""
Write-Host ("-" * 80) -ForegroundColor DarkGray
Write-Host "  PDB LOADING EFFICIENCY" -ForegroundColor Yellow
Write-Host ("-" * 80) -ForegroundColor DarkGray
Write-Host "PDB Cache Hits:     $pdbCacheHits" -ForegroundColor Green
Write-Host "PDB Queries:        $pdbQueries" -ForegroundColor Cyan
Write-Host "PDB Already Loaded: $pdbAlreadyLoaded" -ForegroundColor Green

$totalPdbOps = $pdbCacheHits + $pdbQueries + $pdbAlreadyLoaded
if ($totalPdbOps -gt 0) {
    $cacheRate = [math]::Round((($pdbCacheHits + $pdbAlreadyLoaded) / $totalPdbOps) * 100, 1)
    Write-Host "Cache Hit Rate:     $cacheRate%" -ForegroundColor $(if ($cacheRate -gt 80) { "Green" } else { "Yellow" })
}

# Module initialization analysis
$moduleInits = $log | Where-Object { $_ -match '\[ModuleInit\]' }
$modulesWithDebugInfo = ($moduleInits | Where-Object { $_ -match 'HasDebugInfo=True' }).Count
$modulesWithoutDebugInfo = ($moduleInits | Where-Object { $_ -match 'HasDebugInfo=False' }).Count

Write-Host ""
Write-Host ("-" * 80) -ForegroundColor DarkGray
Write-Host "  MODULE INITIALIZATION" -ForegroundColor Yellow
Write-Host ("-" * 80) -ForegroundColor DarkGray
Write-Host "Modules With Debug Info:    $modulesWithDebugInfo" -ForegroundColor Green
Write-Host "Modules Without Debug Info: $modulesWithoutDebugInfo" -ForegroundColor Yellow

# Categorize failed modules
Write-Host ""
Write-Host ("-" * 80) -ForegroundColor DarkGray
Write-Host "  MODULE CATEGORIES" -ForegroundColor Yellow
Write-Host ("-" * 80) -ForegroundColor DarkGray

$kernelModules = @()
$microsoftOtherModules = @()
$driverModules = @()
$thirdPartyModules = @()

foreach ($module in $failedModules.Keys) {
    $count = $failedModules[$module]
    $moduleLower = $module.ToLower()

    # Check if module is marked as Microsoft from trace FileVersion events
    $isMicrosoftFromTrace = $microsoftModules.ContainsKey($moduleLower) -and $microsoftModules[$moduleLower]

    # Fallback to name-based heuristics if no trace info
    $isMicrosoftByName = $module -match '^Windows\.|^Microsoft\.|explorer\.exe|shell32|combase|ExplorerFrame|dui70|duser|thumbcache|twinui|dwm|dcomp|CoreMessaging'

    $isMicrosoft = $isMicrosoftFromTrace -or $isMicrosoftByName

    if ($module -match 'ntoskrnl|ntkrnl|hal\.dll') {
        $kernelModules += [PSCustomObject]@{Name=$module; Count=$count; IsMicrosoft=$true}
    } elseif ($module -match '\.sys$') {
        $driverModules += [PSCustomObject]@{Name=$module; Count=$count; IsMicrosoft=$isMicrosoft}
    } elseif ($isMicrosoft) {
        $microsoftOtherModules += [PSCustomObject]@{Name=$module; Count=$count; IsMicrosoft=$true}
    } else {
        $thirdPartyModules += [PSCustomObject]@{Name=$module; Count=$count; IsMicrosoft=$false}
    }
}

$kernelTotal = ($kernelModules | Measure-Object -Property Count -Sum).Sum
$microsoftTotal = ($microsoftOtherModules | Measure-Object -Property Count -Sum).Sum
$driverTotal = ($driverModules | Measure-Object -Property Count -Sum).Sum
$thirdPartyTotal = ($thirdPartyModules | Measure-Object -Property Count -Sum).Sum
$msDriverCount = ($driverModules | Where-Object { $_.IsMicrosoft } | Measure-Object).Count

Write-Host ""
Write-Host "Kernel/NT:     $kernelTotal failures in $($kernelModules.Count) modules" -ForegroundColor Red
Write-Host "Microsoft:     $microsoftTotal failures in $($microsoftOtherModules.Count) modules" -ForegroundColor Yellow
Write-Host "Drivers:       $driverTotal failures in $($driverModules.Count) modules ($msDriverCount Microsoft)" -ForegroundColor Yellow
Write-Host "Third-Party:   $thirdPartyTotal failures in $($thirdPartyModules.Count) modules" -ForegroundColor White

# Extract symbol path from log
$symbolPathLine = $log | Where-Object { $_ -match '\[SymbolSearch\] Symbol search path:' } | Select-Object -First 1
$symbolPath = ""
if ($symbolPathLine -match 'Symbol search path:\s*(.+)$') {
    $symbolPath = $Matches[1].Trim()
}

# Recommendations
Write-Host ""
Write-Host ("=" * 80) -ForegroundColor Cyan
Write-Host "  SYMBOL CONFIGURATION" -ForegroundColor Cyan
Write-Host ("=" * 80) -ForegroundColor Cyan
Write-Host ""

if ($symbolPath) {
    Write-Host "Symbol Path Used:" -ForegroundColor Cyan
    # Split on semicolons and display each part
    $symbolPath -split ';' | Where-Object { $_.Trim() } | ForEach-Object {
        Write-Host "  - $_" -ForegroundColor White
    }
    Write-Host ""
} else {
    Write-Host "[!] Could not determine symbol path from log" -ForegroundColor Yellow
    Write-Host ""
}

Write-Host ("=" * 80) -ForegroundColor Cyan
Write-Host "  RECOMMENDATIONS" -ForegroundColor Cyan
Write-Host ("=" * 80) -ForegroundColor Cyan
Write-Host ""

if ($failedModules.ContainsKey("ntoskrnl.exe") -and $failedModules["ntoskrnl.exe"] -gt 100) {
    Write-Host "[!] " -NoNewline -ForegroundColor Red
    Write-Host "High ntoskrnl.exe failures ($($failedModules['ntoskrnl.exe'])). Kernel symbols not resolving."
    Write-Host "    This binary may not have public symbols, or the symbol server is timing out."
    Write-Host ""
}

if ($failedModules.ContainsKey("combase.dll") -and $failedModules["combase.dll"] -gt 100) {
    Write-Host "[!] " -NoNewline -ForegroundColor Red
    Write-Host "High combase.dll failures ($($failedModules['combase.dll'])). COM infrastructure symbols missing."
    Write-Host "    Verify this binary version has symbols available on your configured symbol server."
    Write-Host ""
}

if ($failedModules.ContainsKey("explorer.exe") -and $failedModules["explorer.exe"] -gt 50) {
    Write-Host "[!] " -NoNewline -ForegroundColor Yellow
    Write-Host "explorer.exe symbols missing ($($failedModules['explorer.exe'])). Profile is for explorer.exe but symbols not loaded."
    Write-Host ""
}

$thirdPartyHigh = $thirdPartyModules | Where-Object { $_.Count -gt 50 }
if ($thirdPartyHigh.Count -gt 0) {
    Write-Host "[i] " -NoNewline -ForegroundColor Cyan
    Write-Host "Third-party modules without symbols (PDBs not available on symbol server):"
    foreach ($m in $thirdPartyHigh | Sort-Object Count -Descending | Select-Object -First 5) {
        Write-Host "    - $($m.Name) ($($m.Count) failures)"
    }
    Write-Host ""
}

# Also show high-failure Microsoft modules (may indicate symbol server issues)
$microsoftHigh = $microsoftOtherModules | Where-Object { $_.Count -gt 50 }
if ($microsoftHigh.Count -gt 0) {
    Write-Host "[!] " -NoNewline -ForegroundColor Yellow
    Write-Host "Microsoft modules without symbols (check symbol server access):"
    foreach ($m in $microsoftHigh | Sort-Object Count -Descending | Select-Object -First 5) {
        Write-Host "    - $($m.Name) ($($m.Count) failures)"
    }
    Write-Host ""
}

# Analyze timestamp gaps (find slow operations) - OPTIMIZED
Write-Host ""
Write-Host ("-" * 80) -ForegroundColor DarkGray
Write-Host "  TIMESTAMP GAP ANALYSIS (Slow Operations)" -ForegroundColor Yellow
Write-Host ("-" * 80) -ForegroundColor DarkGray

Write-Host "Analyzing timestamps..." -ForegroundColor Gray

# Optimized: Use ArrayList and only parse timestamps, track gaps inline
$gaps = [System.Collections.ArrayList]::new()
$lazyLoadOps = [System.Collections.ArrayList]::new()  # Track lazy load operations (start to finish)
$prevTimestamp = $null
$prevLine = $null
$prevLineNum = 0
$lineNum = 0
$totalTimestamped = 0
$loadingCompleted = $false

# For tracking lazy load operations
$lazyLoadStart = $null
$lazyLoadStartLine = $null
$lazyLoadModule = $null

foreach ($line in $log) {
    $lineNum++

    # Track when initial loading is done - gaps after this before lazy load are user think time
    if ($line -match 'LoadBinaryAndDebugFiles completed|=== Trace loading completed') {
        $loadingCompleted = $true
    }

    # Track lazy load operations (start to finish for each binary)
    if ($line -match '\[LazyBinaryLoad\] Loading binary on-demand for ([^\s]+)') {
        $lazyLoadModule = $Matches[1]  # Save module name BEFORE timestamp match overwrites $Matches
        $lazyLoadStart = Get-TimestampFromLine $line
        $lazyLoadStartLine = $lineNum
    }
    if ($lazyLoadStart -and ($line -match '\[LazyBinaryLoad\] Successfully loaded|\[LazyBinaryLoad\] Could not find|\[LazyBinaryLoad\] Found binary')) {
        $lazyLoadEnd = Get-TimestampFromLine $line
        if ($lazyLoadEnd) {
            $lazyLoadMs = ($lazyLoadEnd - $lazyLoadStart).TotalMilliseconds
            [void]$lazyLoadOps.Add([PSCustomObject]@{
                Module = $lazyLoadModule
                DurationMs = $lazyLoadMs
                StartLine = $lazyLoadStartLine
                EndLine = $lineNum
                StartTime = $lazyLoadStart.ToString("HH:mm:ss.fff")
                EndTime = $lazyLoadEnd.ToString("HH:mm:ss.fff")
            })
        }
        $lazyLoadStart = $null
        $lazyLoadModule = $null
    }

    if ($line -match '^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})') {
        $totalTimestamped++
        $timestamp = [datetime]::ParseExact($Matches[1], "yyyy-MM-dd HH:mm:ss.fff", $null)

        if ($null -ne $prevTimestamp) {
            $gapMs = ($timestamp - $prevTimestamp).TotalMilliseconds
            if ($gapMs -gt 500) {  # Only track gaps > 500ms for performance
                $gapObj = [PSCustomObject]@{
                    GapMs = $gapMs
                    StartLine = $prevLineNum
                    EndLine = $lineNum
                    StartTime = $prevTimestamp.ToString("HH:mm:ss.fff")
                    EndTime = $timestamp.ToString("HH:mm:ss.fff")
                    BeforeLine = $prevLine
                    AfterLine = $line
                }

                # Only track gaps during initial load (not lazy load related)
                # Lazy load operations are tracked separately
                $isLazyLoadRelated = ($line -match '\[LazyBinaryLoad\]') -or ($prevLine -match '\[LazyBinaryLoad\]')

                if (-not $isLazyLoadRelated) {
                    [void]$gaps.Add($gapObj)
                }
            }
        }

        $prevTimestamp = $timestamp
        $prevLine = $line
        $prevLineNum = $lineNum
    }
}

Write-Host "Total timestamped lines: $totalTimestamped"
Write-Host "Initial load gaps > 500ms: $($gaps.Count)" -ForegroundColor $(if ($gaps.Count -gt 0) { "Yellow" } else { "Green" })
if ($lazyLoadOps.Count -gt 0) {
    $lazyLoadTotal = ($lazyLoadOps | Measure-Object -Property DurationMs -Sum).Sum / 1000
    Write-Host "Lazy load operations: $($lazyLoadOps.Count) binaries ($([math]::Round($lazyLoadTotal, 1))s total)" -ForegroundColor Cyan
}
Write-Host ""

# Sort by gap size and show top gaps
$topGaps = $gaps | Sort-Object GapMs -Descending | Select-Object -First 25

if ($topGaps.Count -gt 0) {
    Write-Host "Top 25 Largest Gaps (potential slow operations):" -ForegroundColor Yellow
    Write-Host ""
    
    $rank = 0
    foreach ($gap in $topGaps) {
        $rank++
        $secs = [math]::Round($gap.GapMs / 1000, 2)
        $color = if ($gap.GapMs -gt 5000) { "Red" } elseif ($gap.GapMs -gt 1000) { "Yellow" } else { "White" }
        
        Write-Host ("{0,3}. " -f $rank) -NoNewline -ForegroundColor Cyan
        Write-Host ("{0,8}ms ({1,6}s) " -f [int]$gap.GapMs, $secs) -NoNewline -ForegroundColor $color
        Write-Host "Lines $($gap.StartLine)-$($gap.EndLine) @ $($gap.StartTime)" -ForegroundColor Gray
        
        # Extract context from the lines
        $beforeContext = ""
        $afterContext = ""
        
        if ($gap.BeforeLine -match '\[([^\]]+)\]\s*\[([^\]]+)\]\s*(.*)') {
            $beforeContext = "[$($Matches[2])] $($Matches[3].Substring(0, [Math]::Min(60, $Matches[3].Length)))"
        }
        if ($gap.AfterLine -match '\[([^\]]+)\]\s*\[([^\]]+)\]\s*(.*)') {
            $afterContext = "[$($Matches[2])] $($Matches[3].Substring(0, [Math]::Min(60, $Matches[3].Length)))"
        }
        
        if ($beforeContext) {
            Write-Host "       Before: $beforeContext..." -ForegroundColor DarkGray
        }
        if ($afterContext) {
            Write-Host "       After:  $afterContext..." -ForegroundColor DarkGray
        }
        Write-Host ""
    }
    
    # Summary statistics
    $totalGapTime = ($gaps | Measure-Object -Property GapMs -Sum).Sum
    $avgGap = ($gaps | Measure-Object -Property GapMs -Average).Average
    $gapsOver1s = ($gaps | Where-Object { $_.GapMs -gt 1000 }).Count
    $gapsOver5s = ($gaps | Where-Object { $_.GapMs -gt 5000 }).Count
    
    Write-Host "Gap Statistics:" -ForegroundColor Cyan
    Write-Host "  Total gaps > 100ms:  $($gaps.Count)"
    Write-Host "  Gaps > 1 second:     $gapsOver1s" -ForegroundColor $(if ($gapsOver1s -gt 10) { "Yellow" } else { "White" })
    Write-Host "  Gaps > 5 seconds:    $gapsOver5s" -ForegroundColor $(if ($gapsOver5s -gt 0) { "Red" } else { "White" })
    Write-Host "  Total gap time:      $([math]::Round($totalGapTime / 1000, 2))s"
    Write-Host "  Average gap:         $([math]::Round($avgGap, 0))ms"
    
    # Calculate total trace time
    if ($timestampedLines.Count -gt 1) {
        $firstTime = $timestampedLines[0].Timestamp
        $lastTime = $timestampedLines[-1].Timestamp
        $totalTime = ($lastTime - $firstTime).TotalSeconds
        $gapPercent = [math]::Round(($totalGapTime / 1000) / $totalTime * 100, 1)
        Write-Host ""
        Write-Host "  Total trace time:    $([math]::Round($totalTime, 2))s"
        Write-Host "  Time in gaps:        $gapPercent%" -ForegroundColor $(if ($gapPercent -gt 50) { "Red" } elseif ($gapPercent -gt 25) { "Yellow" } else { "Green" })
    }
} else {
    Write-Host "No significant performance gaps (>500ms) detected." -ForegroundColor Green
}

# Show lazy load operations (on-demand binary downloads)
if ($lazyLoadOps.Count -gt 0) {
    Write-Host ""
    Write-Host ("-" * 80) -ForegroundColor DarkGray
    Write-Host "  LAZY LOAD OPERATIONS (on-demand binary downloads)" -ForegroundColor Cyan
    Write-Host ("-" * 80) -ForegroundColor DarkGray
    Write-Host "Time spent downloading binaries when user views assembly/graph." -ForegroundColor Gray
    Write-Host ""

    foreach ($op in $lazyLoadOps | Sort-Object DurationMs -Descending | Select-Object -First 10) {
        $secs = [math]::Round($op.DurationMs / 1000, 2)
        $color = if ($op.DurationMs -gt 5000) { "Red" } elseif ($op.DurationMs -gt 2000) { "Yellow" } else { "White" }
        Write-Host ("  {0,6}ms ({1,5}s) - {2}" -f [int]$op.DurationMs, $secs, $op.Module) -ForegroundColor $color
    }

    $lazyLoadTotal = ($lazyLoadOps | Measure-Object -Property DurationMs -Sum).Sum / 1000
    Write-Host ""
    Write-Host "  Total lazy load time: $([math]::Round($lazyLoadTotal, 1))s" -ForegroundColor Cyan
}


# Analyze what operations are causing the biggest gaps (excluding user think time)
Write-Host ""
Write-Host ("-" * 80) -ForegroundColor DarkGray
Write-Host "  GAP CAUSE ANALYSIS" -ForegroundColor Yellow
Write-Host ("-" * 80) -ForegroundColor DarkGray

$gapCauses = @{}
foreach ($gap in $gaps | Where-Object { $_.GapMs -gt 500 }) {
    $cause = "Unknown"

    if ($gap.AfterLine -match '\[LazyBinaryLoad\].*for ([^\s]+)') {
        # LazyBinaryLoad during loading (not user-triggered) is a real delay
        $cause = "LazyBinaryLoad: $($Matches[1])"
    } elseif ($gap.AfterLine -match '\[ModuleInit\].*Starting.*module: ([^\s]+)') {
        $cause = "ModuleInit: $($Matches[1])"
    } elseif ($gap.AfterLine -match '\[BinaryLoading\].*Binary: ([^\s,]+)') {
        $cause = "BinaryLoad: $($Matches[1])"
    } elseif ($gap.AfterLine -match '\[PDBDebugInfo\].*Binary: ([^\s,]+)') {
        $cause = "PDBLoad: $($Matches[1])"
    } elseif ($gap.AfterLine -match '\[SymbolSearch\].*for ([^\s]+)') {
        $cause = "SymbolSearch: $($Matches[1])"
    } elseif ($gap.AfterLine -match '\[DebugInfoInit\].*module ([^\s]+)') {
        $cause = "DebugInfoInit: $($Matches[1])"
    } elseif ($gap.AfterLine -match '\[FunctionResolution\].*Module: ([^\s,]+)') {
        $cause = "FuncResolve: $($Matches[1])"
    } elseif ($gap.BeforeLine -match '\[ModuleInit\].*module: ([^\s]+)') {
        $cause = "After ModuleInit: $($Matches[1])"
    } elseif ($gap.BeforeLine -match '\[SymbolLoading\].*PDB download') {
        $cause = "PDB Downloads"
    }

    if ($gapCauses.ContainsKey($cause)) {
        $gapCauses[$cause] += $gap.GapMs
    } else {
        $gapCauses[$cause] = $gap.GapMs
    }
}

if ($gapCauses.Count -gt 0) {
    Write-Host "Operations causing most delay (gaps > 500ms):" -ForegroundColor Yellow
    Write-Host ""
    $gapCauses.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 15 | ForEach-Object {
        $secs = [math]::Round($_.Value / 1000, 2)
        Write-Host ("{0,8}ms ({1,6}s) - {2}" -f [int]$_.Value, $secs, $_.Key)
    }
}

# Calculate overall resolution rate
$totalAttempts = $resolved.Count + $notResolved.Count
if ($totalAttempts -gt 0) {
    $resolutionRate = [math]::Round(($resolved.Count / $totalAttempts) * 100, 1)
    Write-Host ""
    Write-Host "Overall Symbol Resolution Rate: " -NoNewline
    if ($resolutionRate -gt 80) {
        Write-Host "$resolutionRate%" -ForegroundColor Green
    } elseif ($resolutionRate -gt 60) {
        Write-Host "$resolutionRate%" -ForegroundColor Yellow
    } else {
        Write-Host "$resolutionRate%" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host ("=" * 80) -ForegroundColor Cyan
Write-Host "  SUMMARY DIAGNOSIS" -ForegroundColor Cyan
Write-Host ("=" * 80) -ForegroundColor Cyan
Write-Host ""

# Determine the primary issue
$primaryIssue = $null
$issueDetails = @()

if ($noImageIdEvents -or $dbgIdCount -eq 0) {
    $primaryIssue = "Missing ImageID DbgID Events"
    $issueDetails += "The trace is missing PDB GUID/Age information required for symbol server lookups."
    $issueDetails += "This typically happens when using traces from external tools that don't capture ImageID events."
    $issueDetails += ""
    $issueDetails += "FIX: Re-capture with Profile Explorer (File -> Record Profile) or 'wpr -start CPU'"
}
elseif ($binaryTimeouts -gt 10 -or $pdbTimeouts -gt 10) {
    $primaryIssue = "Symbol Server Timeouts"
    $issueDetails += "Multiple symbol server requests are timing out."
    $issueDetails += "This could indicate network issues, slow symbol server, or corporate firewall blocks."
    $issueDetails += ""
    $issueDetails += "FIX: Check network connectivity, verify symbol server URL, or increase timeout settings."
}
elseif ($totalAttempts -gt 0 -and $resolutionRate -lt 50) {
    $primaryIssue = "Low Symbol Resolution Rate"
    $issueDetails += "Only $resolutionRate% of symbols are resolving successfully."
    $issueDetails += "This may indicate missing symbols on the symbol server or version mismatch."
    $issueDetails += ""
    $issueDetails += "FIX: Ensure symbol server is correctly configured and symbols exist for your binaries."
}
else {
    $primaryIssue = "Symbol Loading Appears Normal"
    $issueDetails += "No major issues detected in symbol loading."
    if ($totalAttempts -gt 0) {
        $issueDetails += "Symbol resolution rate: $resolutionRate%"
    }
}

Write-Host "Primary Issue: " -NoNewline
if ($primaryIssue -eq "Symbol Loading Appears Normal") {
    Write-Host $primaryIssue -ForegroundColor Green
} else {
    Write-Host $primaryIssue -ForegroundColor Red
}
Write-Host ""

foreach ($detail in $issueDetails) {
    if ($detail -match '^FIX:') {
        Write-Host $detail -ForegroundColor Cyan
    } else {
        Write-Host "  $detail" -ForegroundColor Gray
    }
}

Write-Host ""

# ============================================================================
# SOURCE FILE LOADING ANALYSIS
# ============================================================================
Write-Host ("-" * 80) -ForegroundColor DarkGray
Write-Host "  SOURCE FILE LOADING ANALYSIS" -ForegroundColor Yellow
Write-Host ("-" * 80) -ForegroundColor DarkGray

$sourceFileLines = $log | Where-Object { $_ -match '\[SourceFile\]' }
$strippedPdbLines = $log | Where-Object { $_ -match 'PDB appears to be STRIPPED' }
$privatePdbLines = $log | Where-Object { $_ -match 'PDB has source info\.' }
$sourceServerEnabled = $log | Where-Object { $_ -match 'SourceServerEnabled=True' } | Select-Object -First 1
$sourceServerLookups = $log | Where-Object { $_ -match 'Attempting source server lookup' }
$sourceFileSuccess = $log | Where-Object { $_ -match 'Downloaded and verified source file' }
$sourceFileFailed = $log | Where-Object { $_ -match 'Failed to download|GetSourceFile returned: null|lineInfo is Unknown' }
$pdbFileSizes = $log | Where-Object { $_ -match '\[PDBDebugInfo\] PDB file size:' }

# Parse PDB file sizes and build lookup table
$pdbSizeTable = @{}
$pdbFileSizes | ForEach-Object {
    if ($_ -match 'PDB file size: ([0-9,]+) bytes \(([0-9.]+) MB\) - (.+)$') {
        $sizeMB = [double]$Matches[2]
        $path = $Matches[3]
        $pdbName = Split-Path $path -Leaf
        $pdbSizeTable[$path] = @{ Name = $pdbName; SizeMB = $sizeMB }
    }
}

# Parse stripped/private PDB lists
$strippedPdbPaths = @()
$strippedPdbLines | ForEach-Object {
    if ($_ -match 'for: (.+)$') {
        $strippedPdbPaths += $Matches[1]
    }
}

$privatePdbPaths = @()
$privatePdbLines | ForEach-Object {
    # Private PDB log format: "PDB has source info. Sample source file: <file>"
    # The PDB path is logged earlier, need to correlate
}

Write-Host ""
# PDB Classification Summary
$totalPdbs = $strippedPdbLines.Count + $privatePdbLines.Count
if ($totalPdbs -gt 0) {
    Write-Host "PDB CLASSIFICATION SUMMARY:" -ForegroundColor Cyan
    Write-Host ("=" * 50) -ForegroundColor DarkGray
    $privateCount = $privatePdbLines.Count
    $strippedCount = $strippedPdbLines.Count
    $privatePercent = if ($totalPdbs -gt 0) { [math]::Round(($privateCount / $totalPdbs) * 100, 1) } else { 0 }
    $strippedPercent = if ($totalPdbs -gt 0) { [math]::Round(($strippedCount / $totalPdbs) * 100, 1) } else { 0 }

    Write-Host "  Total PDBs loaded:     $totalPdbs" -ForegroundColor White
    Write-Host "  PRIVATE (has source):  $privateCount ($privatePercent%)" -ForegroundColor Green
    Write-Host "  PUBLIC/STRIPPED:       $strippedCount ($strippedPercent%)" -ForegroundColor Yellow
    Write-Host ""

    if ($strippedCount -gt 0 -and $privateCount -gt 0) {
        Write-Host "  [i] Some PDBs are private (from symweb), others are public (from msdl)." -ForegroundColor Gray
        Write-Host "      Private PDBs support source file viewing. Public PDBs only have function names." -ForegroundColor Gray
    } elseif ($strippedCount -gt 0 -and $privateCount -eq 0) {
        Write-Host "  [!] ALL PDBs are PUBLIC/STRIPPED - source file viewing will NOT work." -ForegroundColor Red
        Write-Host "      Check if symweb auth is working. You may need to re-authenticate." -ForegroundColor Yellow
    } elseif ($privateCount -gt 0 -and $strippedCount -eq 0) {
        Write-Host "  [OK] All PDBs are PRIVATE - source file viewing should work!" -ForegroundColor Green
    }
    Write-Host ""
}

Write-Host "Source File Lookups:" -ForegroundColor Cyan
Write-Host "  Total [SourceFile] log entries: $($sourceFileLines.Count)"
Write-Host "  Source server lookups attempted: $($sourceServerLookups.Count)"
Write-Host "  Successful downloads: $($sourceFileSuccess.Count)" -ForegroundColor $(if ($sourceFileSuccess.Count -gt 0) { "Green" } else { "Yellow" })
Write-Host "  Failed lookups: $($sourceFileFailed.Count)" -ForegroundColor $(if ($sourceFileFailed.Count -gt 0) { "Red" } else { "Green" })
Write-Host ""

if ($strippedPdbLines.Count -gt 0) {
    Write-Host "STRIPPED PDBs (no source info):" -ForegroundColor Yellow
    $strippedPdbPaths | Select-Object -First 10 | ForEach-Object {
        $path = $_
        $pdbName = Split-Path $path -Leaf
        $sizeInfo = $pdbSizeTable[$path]
        if ($sizeInfo) {
            Write-Host ("  {0,8:F2} MB - {1}" -f $sizeInfo.SizeMB, $pdbName) -ForegroundColor Yellow
        } else {
            Write-Host "  - $pdbName" -ForegroundColor Yellow
        }
    }
    if ($strippedPdbPaths.Count -gt 10) {
        Write-Host "  ... and $($strippedPdbPaths.Count - 10) more" -ForegroundColor Gray
    }
    Write-Host ""
}

if ($pdbFileSizes.Count -gt 0) {
    Write-Host "PDB File Sizes (top 10 by size):" -ForegroundColor Cyan
    # Sort by size descending and show top 10
    $sortedPdbs = $pdbSizeTable.Values | Sort-Object -Property SizeMB -Descending | Select-Object -First 10
    $sortedPdbs | ForEach-Object {
        $sizeMB = $_.SizeMB
        $pdbName = $_.Name
        Write-Host ("  {0,8:F2} MB - {1}" -f $sizeMB, $pdbName) -ForegroundColor White
    }
    Write-Host ""
}

# ============================================================================
# AUTH & SYMBOL SERVER ANALYSIS
# ============================================================================
Write-Host ("-" * 80) -ForegroundColor DarkGray
Write-Host "  AUTH & SYMBOL SERVER ANALYSIS" -ForegroundColor Yellow
Write-Host ("-" * 80) -ForegroundColor DarkGray

# Look for explicit auth failure/success messages from our logging
$authFailed = $log | Where-Object { $_ -match 'auth FAILED|PrimaryServerAuthFailed=True' }
$authVerified = $log | Where-Object { $_ -match 'auth VERIFIED|PrimaryServerVerified=True' }
$symwebHits = $log | Where-Object { $_ -match 'symweb' -and $_ -match 'TraceEvent log' }
$msdlHits = $log | Where-Object { $_ -match 'msdl\.microsoft\.com|download/symbols' -and $_ -match 'TraceEvent log' }

Write-Host ""
if ($authFailed.Count -gt 0) {
    Write-Host "[!] AUTH FAILURES DETECTED:" -ForegroundColor Red
    $authFailed | Select-Object -First 5 | ForEach-Object {
        Write-Host "  $_" -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "    If you're EXTERNAL to Microsoft, symweb auth failures are expected." -ForegroundColor Gray
    Write-Host "    The fallback to public symbols (msdl) should be automatic." -ForegroundColor Gray
    Write-Host ""
} elseif ($authVerified.Count -gt 0) {
    Write-Host "[OK] Primary server (symweb) auth verified" -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host "[?] Could not determine auth status from logs" -ForegroundColor Yellow
    Write-Host "    Look for 401/403 errors in TraceEvent logs below" -ForegroundColor Gray
    Write-Host ""
}

# Show TraceEvent logs for symbol downloads
$traceEventLogs = $log | Where-Object { $_ -match '\[SymbolSearch\] TraceEvent log for' }
if ($traceEventLogs.Count -gt 0) {
    Write-Host "Symbol Download Details (first 5):" -ForegroundColor Cyan
    $traceEventLogs | Select-Object -First 5 | ForEach-Object {
        if ($_ -match 'TraceEvent log for ([^:]+):') {
            Write-Host "  - $($Matches[1])" -ForegroundColor White
        }
    }
    Write-Host ""
}

Write-Host ("=" * 80) -ForegroundColor Cyan


