#Requires -Version 7
<#
.SYNOPSIS
    Opens a visible pwsh window running a Claude Code session in a folder under
    ~/repos.

.DESCRIPTION
    Resolves the folder, launches pwsh with claude --dangerously-skip-permissions
    in it, and returns an object describing what started. This is the same launch
    the watchdog's scheduled task performs, so sessions started here look
    identical to the always-on one.

.PARAMETER Name
    The folder under ~/repos. Either a top-level name such as 'mosaic' or a
    relative subpath such as 'Me\Francois\home-purchase'. Matching is
    case-insensitive and the resolved path is reported back, so 'mosaic' and
    'Mosaic' both work.

.PARAMETER Create
    Create the folder, and any missing parents, when it does not exist yet.
    Without this the script fails on a missing folder, which is what you want
    for a typo in the name of an existing repo.

.OUTPUTS
    A PSCustomObject with Name, Path, and ProcessId.

.EXAMPLE
    pwsh -NoProfile -File scripts/Start-Session.ps1 -Name mosaic

.EXAMPLE
    pwsh -NoProfile -File scripts/Start-Session.ps1 -Name 'Me\Francois\home-purchase' -Create
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Name,

    [switch]$Create
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Everything lives under ~/repos. An absolute path or a .. segment would escape
# that, and silently opening a session somewhere unexpected is worse than an
# error telling you the name was wrong.
if ([System.IO.Path]::IsPathRooted($Name)) {
    throw "Pass a path relative to ~/repos, not an absolute one. Got '$Name'."
}

$segments = $Name -split '[\\/]+' | Where-Object { $_ -ne '' }
if ($segments -contains '..') {
    throw "'..' is not allowed in the folder name. Got '$Name'."
}
if (-not $segments) {
    throw 'Name is empty.'
}

$reposRoot = Join-Path $HOME 'repos'
$repoPath = Join-Path $reposRoot ($segments -join [System.IO.Path]::DirectorySeparatorChar)

if (Test-Path -LiteralPath $repoPath -PathType Leaf) {
    throw "'$repoPath' is a file, not a folder."
}

if (-not (Test-Path -LiteralPath $repoPath -PathType Container)) {
    if ($Create) {
        Write-Host "Creating $repoPath"
        New-Item -ItemType Directory -Path $repoPath -Force | Out-Null
    }
    else {
        # Suggest siblings from the deepest parent that does exist, so a typo in
        # any segment still gets a useful hint rather than a bare failure.
        $parent = Split-Path -Parent $repoPath
        $candidates = @()
        if (Test-Path -LiteralPath $parent -PathType Container) {
            $leaf = $segments[-1]
            $candidates = Get-ChildItem -LiteralPath $parent -Directory |
                Where-Object { $_.Name -like "*$leaf*" } |
                Select-Object -ExpandProperty Name
        }

        $hint = if ($candidates) { " Did you mean: $($candidates -join ', ')?" } else { ' Pass -Create to make it.' }
        throw "'$repoPath' does not exist.$hint"
    }
}

if (-not (Get-Command claude -ErrorAction SilentlyContinue)) {
    throw "'claude' is not on PATH. Expected it at $HOME\.local\bin\claude.exe."
}

# Resolve the name as the filesystem actually spells it. Windows matches paths
# case-insensitively, so reporting back what was typed can misrepresent what was
# opened.
$resolved = Get-Item -LiteralPath $repoPath

# Count before launching. Afterwards the new claude.exe has not necessarily
# spawned yet, because pwsh has to start first, so counting later would
# undercount and suppress a warning that does apply.
$watchdogPath = Join-Path $HOME '.local\bin\claude.exe'
$alreadyRunning = @(
    Get-CimInstance Win32_Process -Filter "Name='claude.exe'" |
        Where-Object { $_.ExecutablePath -eq $watchdogPath }
).Count

# A new window must not inherit the markers Claude Code injects into its tool
# subprocesses, and Start-Process passes the whole environment down.
# CLAUDE_CODE_CHILD_SESSION makes the new session think it is a child and turns
# transcript saving off. The session and process identifiers name the launching
# session, which is equally wrong in a fresh window. NO_COLOR strips all color
# from the new window. CLAUDECODE trips nested-session detection, and
# GIT_TERMINAL_PROMPT=0 silently disables git credential prompts in what is an
# interactive window. Configuration such as CLAUDE_AUTOCOMPACT_PCT_OVERRIDE is
# deliberately left alone: injected markers must not be inherited, settings
# may be. Keep this list in sync with ~/.claude/skills/open-repo.
$inherited = @(
    'CLAUDE_CODE_CHILD_SESSION'
    'CLAUDE_CODE_SESSION_ID'
    'CLAUDE_CODE_BRIDGE_SESSION_ID'
    'CLAUDE_CODE_ENTRYPOINT'
    'CLAUDE_PID'
    'CLAUDECODE'
    'NO_COLOR'
    'GIT_TERMINAL_PROMPT'
)
$clearEnv = ($inherited | ForEach-Object { "Remove-Item Env:$_ -ErrorAction SilentlyContinue" }) -join '; '

$launch = Start-Process pwsh -PassThru -ArgumentList @(
    '-NoExit',
    '-NoProfile',
    '-Command',
    "$clearEnv; Set-Location '$($resolved.FullName)'; claude --dangerously-skip-permissions"
)

if ($alreadyRunning -ge 1) {
    Write-Host "Note: $alreadyRunning Claude session(s) were already running. The watchdog only" -ForegroundColor Yellow
    Write-Host 'relaunches when none are left, so it will not restart any of them until every' -ForegroundColor Yellow
    Write-Host 'window is closed.' -ForegroundColor Yellow
}

[PSCustomObject]@{
    Name      = $resolved.Name
    Path      = $resolved.FullName
    ProcessId = $launch.Id
}
