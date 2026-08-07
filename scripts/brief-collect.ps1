#requires -Version 7.0
<#
.SYNOPSIS
  Collects machine vitals + the 10 most recent resumable Claude Code sessions.
  Emits a single compact JSON blob.

.DESCRIPTION
  Used by the /brief slash command (see .claude/commands/brief.md). Sub-agent
  transcripts (filenames starting with 'agent-') are NOT resumable and are
  filtered out here so the consumer never has to deal with them.

  Output shape:
    {
      "vitals":   { ...status object from the watchdog API, or local fallback... },
      "sessions": [ { lastActivityUtc, sessionId, cwd, path, sizeKiB, firstPrompt }, ... ],
      "backup":   { ...output of pc-backup's Get-BackupStatus.ps1 -AsJson,
                     or { available: false, reason: <text> } if that call
                     failed, threw, or the script was missing... }
    }
#>

$ErrorActionPreference = 'SilentlyContinue'

# 1) Watchdog status (5s timeout). Fall back to local probes if unreachable.
$vitals = try {
    Invoke-RestMethod -Uri 'http://localhost:19720/status' -TimeoutSec 5 -ErrorAction Stop
} catch { $null }

if (-not $vitals) {
    $os    = Get-CimInstance Win32_OperatingSystem
    $disk  = Get-PSDrive C
    $vitals = [pscustomobject]@{
        watchdogReachable = $false
        machineName       = $env:COMPUTERNAME
        uptimeSeconds     = [int64]([Environment]::TickCount64 / 1000)
        usedMemoryBytes   = [int64](($os.TotalVisibleMemorySize - $os.FreePhysicalMemory) * 1KB)
        totalMemoryBytes  = [int64]($os.TotalVisibleMemorySize * 1KB)
        diskFreeBytes     = [int64]$disk.Free
        diskTotalBytes    = [int64]($disk.Free + $disk.Used)
        targetClaudeCount = @(Get-Process claude.exe).Count
        publicIp          = $null
        cpuPercent        = $null
    }
} else {
    $vitals | Add-Member -NotePropertyName watchdogReachable -NotePropertyValue $true -Force
}

# 2) The 10 most recent resumable session transcripts. Sub-agent files
#    ('agent-*.jsonl') are not resumable on their own, so skip them entirely.
$root  = Join-Path $env:USERPROFILE '.claude\projects'
$files = Get-ChildItem -Path $root -Filter *.jsonl -Recurse -File |
            Where-Object { -not $_.Name.StartsWith('agent-') } |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 10

$sessions = foreach ($f in $files) {
    # First line carries the cwd; subsequent lines carry messages.
    $firstLine = Get-Content -LiteralPath $f.FullName -TotalCount 1
    $cwd = ''
    try { $cwd = ($firstLine | ConvertFrom-Json).cwd } catch {}

    $firstPrompt = ''
    foreach ($line in Get-Content -LiteralPath $f.FullName) {
        try {
            $o = $line | ConvertFrom-Json -ErrorAction Stop
            if ($o.type -eq 'user' -and $o.message.role -eq 'user') {
                $c = $o.message.content
                if     ($c -is [string]) { $firstPrompt = $c }
                elseif ($c)              { $firstPrompt = ($c | Where-Object type -eq 'text' | Select-Object -First 1).text }
                if ($firstPrompt) { break }
            }
        } catch {}
    }
    if ($firstPrompt) {
        $firstPrompt = $firstPrompt -replace '\s+', ' '
        if ($firstPrompt.Length -gt 100) { $firstPrompt = $firstPrompt.Substring(0,97) + '...' }
    }

    [pscustomobject]@{
        lastActivityUtc = $f.LastWriteTime.ToUniversalTime().ToString('o')
        sessionId       = $f.BaseName
        cwd             = $cwd
        path            = $f.FullName
        sizeKiB         = [int][math]::Round($f.Length / 1KB)
        firstPrompt     = $firstPrompt
    }
}


# 3) Backup status, from pc-backup's own status script. That script already
#    reads its own disk-or-cache fallback and decides what's healthy, so
#    this just calls it and carries the result through. A missing or
#    failing script must not break /brief, and must not be silently dropped
#    either - the user specifically wants a broken integration to be
#    visible, not indistinguishable from a healthy backup, so failure here
#    still produces a "backup" key, just one that says it is unavailable
#    and why.
$backupStatusScript = 'C:\Users\andre\repos\pc-backup\scripts\Get-BackupStatus.ps1'
$backup = try {
    if (-not (Test-Path -LiteralPath $backupStatusScript -PathType Leaf)) {
        throw "Get-BackupStatus.ps1 not found at $backupStatusScript"
    }
    $backupJson = & $backupStatusScript -AsJson -ErrorAction Stop
    if ([string]::IsNullOrWhiteSpace([string]$backupJson)) {
        throw 'Get-BackupStatus.ps1 produced no output.'
    }
    $backupJson | ConvertFrom-Json -ErrorAction Stop
} catch {
    [pscustomobject]@{
        available = $false
        reason    = "backup status unavailable: $($_.Exception.Message)"
    }
}

[pscustomobject]@{ vitals = $vitals; sessions = @($sessions); backup = $backup } | ConvertTo-Json -Depth 8 -Compress
