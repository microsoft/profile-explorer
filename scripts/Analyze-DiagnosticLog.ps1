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

# Categorize lines
$infoLines = $log | Where-Object { $_ -match '\[Information\]' }
$warningLines = $log | Where-Object { $_ -match '\[Warning\]' }
$debugLines = $log | Where-Object { $_ -match '\[Debug\]' }
$errorLines = $log | Where-Object { $_ -match '\[Error\]' }

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

if ($startTime -and $endTime) {
    $totalTime = ($endTime - $startTime).TotalSeconds
    Write-Host "Total Symbol Loading Time: " -NoNewline
    Write-Host ("{0:F1}s" -f $totalTime) -ForegroundColor $(if ($totalTime -gt 120) { "Red" } elseif ($totalTime -gt 60) { "Yellow" } else { "Green" })
}

if ($binStartTime -and $binEndTime) {
    $binTime = ($binEndTime - $binStartTime).TotalSeconds
    Write-Host "  Binary Download Phase:   " -NoNewline
    Write-Host ("{0:F1}s" -f $binTime) -ForegroundColor $(if ($binTime -gt 60) { "Red" } elseif ($binTime -gt 30) { "Yellow" } else { "Green" })
}

if ($pdbStartTime -and $pdbEndTime) {
    $pdbTime = ($pdbEndTime - $pdbStartTime).TotalSeconds
    Write-Host "  PDB Download Phase:      " -NoNewline
    Write-Host ("{0:F1}s" -f $pdbTime) -ForegroundColor $(if ($pdbTime -gt 60) { "Red" } elseif ($pdbTime -gt 30) { "Yellow" } else { "Green" })
}

# Count timeouts vs successes
$binaryTimeouts = ($log | Where-Object { $_ -match '\[BinarySearch\] TIMEOUT' }).Count
$binaryFound = ($log | Where-Object { $_ -match '\[BinarySearch\] Found binary for' }).Count
$binaryFailed = ($log | Where-Object { $_ -match '\[BinarySearch\] Failed to find binary' }).Count
$binarySkipped = ($log | Where-Object { $_ -match '\[BinarySearch\] SKIPPED' }).Count

$pdbTimeouts = ($log | Where-Object { $_ -match '\[SymbolSearch\] TIMEOUT' }).Count
$pdbFound = ($log | Where-Object { $_ -match '\[SymbolSearch\] Successfully found symbol' }).Count
$pdbFailed = ($log | Where-Object { $_ -match '\[SymbolSearch\] Failed to find symbol' }).Count
$pdbSkipped = ($log | Where-Object { $_ -match '\[SymbolLoading\] Skipping PDB lookup' }).Count

Write-Host ""
Write-Host "Binary Search Results:" -ForegroundColor Cyan
Write-Host "  Found:    $binaryFound" -ForegroundColor Green
Write-Host "  Failed:   $binaryFailed" -ForegroundColor Yellow
Write-Host "  Timeouts: $binaryTimeouts" -ForegroundColor $(if ($binaryTimeouts -gt 10) { "Red" } else { "Yellow" })
Write-Host "  Skipped:  $binarySkipped" -ForegroundColor Gray

Write-Host ""
Write-Host "PDB Search Results:" -ForegroundColor Cyan
Write-Host "  Found:    $pdbFound" -ForegroundColor Green
Write-Host "  Failed:   $pdbFailed" -ForegroundColor Yellow
Write-Host "  Timeouts: $pdbTimeouts" -ForegroundColor $(if ($pdbTimeouts -gt 10) { "Red" } else { "Yellow" })
Write-Host "  Skipped (company filter): $pdbSkipped" -ForegroundColor Gray

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
$windowsModules = @()
$driverModules = @()
$thirdPartyModules = @()

foreach ($module in $failedModules.Keys) {
    $count = $failedModules[$module]
    if ($module -match '\.sys$') {
        $driverModules += [PSCustomObject]@{Name=$module; Count=$count}
    } elseif ($module -match 'ntoskrnl|ntkrnl|hal\.dll') {
        $kernelModules += [PSCustomObject]@{Name=$module; Count=$count}
    } elseif ($module -match '^Windows\.|^Microsoft\.|explorer\.exe|shell32|combase|ExplorerFrame|dui70|duser|thumbcache|twinui') {
        $windowsModules += [PSCustomObject]@{Name=$module; Count=$count}
    } else {
        $thirdPartyModules += [PSCustomObject]@{Name=$module; Count=$count}
    }
}

$kernelTotal = ($kernelModules | Measure-Object -Property Count -Sum).Sum
$windowsTotal = ($windowsModules | Measure-Object -Property Count -Sum).Sum
$driverTotal = ($driverModules | Measure-Object -Property Count -Sum).Sum
$thirdPartyTotal = ($thirdPartyModules | Measure-Object -Property Count -Sum).Sum

Write-Host ""
Write-Host "Kernel/NT:     $kernelTotal failures in $($kernelModules.Count) modules" -ForegroundColor Red
Write-Host "Windows Shell: $windowsTotal failures in $($windowsModules.Count) modules" -ForegroundColor Yellow
Write-Host "Drivers:       $driverTotal failures in $($driverModules.Count) modules" -ForegroundColor Yellow
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
    Write-Host "Third-party modules without symbols (expected for proprietary software):"
    foreach ($m in $thirdPartyHigh | Sort-Object Count -Descending | Select-Object -First 5) {
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
$prevTimestamp = $null
$prevLine = $null
$prevLineNum = 0
$lineNum = 0
$totalTimestamped = 0

foreach ($line in $log) {
    $lineNum++
    if ($line -match '^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})') {
        $totalTimestamped++
        $timestamp = [datetime]::ParseExact($Matches[1], "yyyy-MM-dd HH:mm:ss.fff", $null)
        
        if ($null -ne $prevTimestamp) {
            $gapMs = ($timestamp - $prevTimestamp).TotalMilliseconds
            if ($gapMs -gt 500) {  # Only track gaps > 500ms for performance
                [void]$gaps.Add([PSCustomObject]@{
                    GapMs = $gapMs
                    StartLine = $prevLineNum
                    EndLine = $lineNum
                    StartTime = $prevTimestamp.ToString("HH:mm:ss.fff")
                    EndTime = $timestamp.ToString("HH:mm:ss.fff")
                    BeforeLine = $prevLine
                    AfterLine = $line
                })
            }
        }
        
        $prevTimestamp = $timestamp
        $prevLine = $line
        $prevLineNum = $lineNum
    }
}

Write-Host "Total timestamped lines: $totalTimestamped"
Write-Host "Gaps > 500ms found: $($gaps.Count)"
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
    Write-Host "No significant gaps (>100ms) detected." -ForegroundColor Green
}

# Analyze what operations are causing the biggest gaps
Write-Host ""
Write-Host ("-" * 80) -ForegroundColor DarkGray
Write-Host "  GAP CAUSE ANALYSIS" -ForegroundColor Yellow
Write-Host ("-" * 80) -ForegroundColor DarkGray

$gapCauses = @{}
foreach ($gap in $gaps | Where-Object { $_.GapMs -gt 500 }) {
    $cause = "Unknown"
    
    if ($gap.AfterLine -match '\[ModuleInit\].*Starting.*module: ([^\s]+)') {
        $cause = "ModuleInit: $($Matches[1])"
    } elseif ($gap.AfterLine -match '\[BinaryLoading\].*Binary: ([^\s,]+)') {
        $cause = "BinaryLoad: $($Matches[1])"
    } elseif ($gap.AfterLine -match '\[PDBDebugInfo\].*Binary: ([^\s,]+)') {
        $cause = "PDBLoad: $($Matches[1])"
    } elseif ($gap.AfterLine -match '\[FunctionResolution\].*Module: ([^\s,]+)') {
        $cause = "FuncResolve: $($Matches[1])"
    } elseif ($gap.BeforeLine -match '\[ModuleInit\].*module: ([^\s]+)') {
        $cause = "After ModuleInit: $($Matches[1])"
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


