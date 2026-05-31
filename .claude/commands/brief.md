---
description: Show machine vitals and recent Claude Code sessions. Use when the user asks for a brief, wants to know what was running on this PC, wants situational awareness after coming back to the machine, or wants to resume a recent session.
argument-hint: [skip|continue|<session-number>]
allowed-tools: Bash(pwsh:*), Read
---

# /brief

Fast situational brief: machine vitals + the 10 most recent Claude Code sessions + an optional resume.

**Run the single pwsh block in step 1 in ONE Bash tool call. Do not split it. Do not invoke any other helper commands. All data you need is in its JSON output - do not re-read files or re-run anything to "verify" or "look deeper".**

## Step 1 - collect everything in one shot

```pwsh
$ErrorActionPreference = 'SilentlyContinue'

# 1a) Watchdog status (5s timeout). Fall back to local probes if unreachable.
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

# 1b) The 10 most recent session transcripts.
$root  = Join-Path $env:USERPROFILE '.claude\projects'
$files = Get-ChildItem -Path $root -Filter *.jsonl -Recurse -File |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 10

$sessions = foreach ($f in $files) {
    # First line carries cwd; subsequent lines carry messages.
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
        isSubAgent      = $f.Name.StartsWith('agent-')
    }
}

[pscustomobject]@{ vitals = $vitals; sessions = @($sessions) } | ConvertTo-Json -Depth 8 -Compress
```

That single call returns everything. Parse the JSON it emits and move to step 2.

## Step 2 - render the brief

Use the layout below (markdown), filling values from the JSON. No second pwsh call. No file reads. Just rendering.

```
machine status

| metric       | value                                          |
| ------------ | ---------------------------------------------- |
| watchdog     | reachable | unreachable                        |
| machine      | <vitals.machineName>                           |
| uptime       | <format uptimeSeconds as `Dd HHh MMm`>         |
| cpu          | <vitals.cpuPercent>%        (skip if null)     |
| memory       | <used GiB> / <total GiB> GiB                   |
| disk C:      | <used GiB> / <total GiB> GiB                   |
| public IP    | <vitals.publicIp>           (skip if null)     |
| claude procs | <vitals.targetClaudeCount>                     |

recent sessions

| # | last activity (UTC)  | dir              | size   | first prompt              |
| - | -------------------- | ---------------- | ------ | ------------------------- |
| 1 | 2026-05-31T07:00:02Z | repos\foo        | 123KiB | ...                       |
| 2 | ...                  |                  |        | sub-agent (not resumable) |
```

Conversion rules:
- Bytes -> GiB with one decimal.
- Keep ISO 8601 UTC timestamps as-is.
- For rows where `isSubAgent: true`, write `sub-agent (not resumable)` in the prompt cell.
- Trim cwd by stripping the leading `C:\Users\andre\` so it reads as `repos\start-claude`.
- No emojis. No em dashes. Hyphens fine.

End with one line:

> Reply with a session number to resume, `continue` for the most recent top-level session, or `skip`.

## Step 3 - optional resume

If `$ARGUMENTS` was passed (`skip` / `continue` / `latest` / integer) OR the user replies with one:

- `skip` or empty after rendering - stop. Done.
- `continue` / `latest` - find the first session row where `isSubAgent == false` and launch with `--continue`.
- integer N - take the Nth session row. If `isSubAgent`, refuse politely and suggest a top-level row instead.

Launch in a detached visible pwsh window:

```pwsh
Start-Process pwsh -ArgumentList @(
  '-NoExit','-NoProfile','-Command',
  "Set-Location '$cwd'; claude --dangerously-skip-permissions --resume '$sessionId'"
)
```

For continue mode the inner command becomes:

```
Set-Location '$cwd'; claude --dangerously-skip-permissions --continue
```

Confirm the new window's PID and `cwd` in one short line. Done.
