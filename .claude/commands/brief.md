---
description: Show machine vitals and recent Claude Code sessions. Use when the user asks for a brief, wants to know what was running on this PC, wants situational awareness after coming back to the machine, or wants to resume a recent session.
argument-hint: [skip|continue|<session-number>]
allowed-tools: Bash(pwsh:*), Read
---

# /brief

Give the user a fast, scannable orientation of this machine and their recent Claude Code activity, then offer to resume any recent session in a fresh pwsh window.

If an argument was passed (`$ARGUMENTS`), short-circuit the interactive flow:
- `skip` or empty - run steps 1-3 only, do not prompt for a resume choice.
- `continue` / `latest` - run step 4 immediately with `claude --continue` against the most recent top-level session's `cwd`.
- a positive integer - treat it as the session number from step 2 and launch step 4 directly.
Otherwise follow all four steps below.

You are being invoked inside a Claude Code session that the StartClaude watchdog spawned in `C:\Users\andre\repos\start-claude`. That means the watchdog's HTTP API is the freshest source of machine info, and the user might be reading this remotely (e.g. via Tailscale from a phone). Keep the brief short - one screen unless the user asks for more.

## Output rules
- ISO 8601 timestamps (`2026-05-31T06:46:58Z`).
- No emojis. No em dashes. Plain prose, hyphens are fine.
- Numbers right-aligned where it helps scanning.
- Section headers in lower-case plain text, no fancy formatting.

## Step 1 - machine + watchdog status

Try the local watchdog API first:

```pwsh
try { Invoke-RestMethod -Uri 'http://localhost:19720/status' -TimeoutSec 2 } catch { $null }
```

If it returns JSON, summarise: machine name, system uptime, CPU%, memory used/total, disk C: used/total, public IP if present, count of target `claude.exe` processes, last poll time, last error. Convert byte counts to GiB.

If the call fails (timeout or non-200), call this out explicitly ("watchdog service unreachable") and fall back to local commands:

```pwsh
[Environment]::MachineName
[TimeSpan]::FromMilliseconds([Environment]::TickCount64)
@(Get-Process claude.exe -ErrorAction SilentlyContinue).Count
```

## Step 2 - recent Claude Code sessions

Enumerate the 10 most recently touched JSONL transcripts:

```pwsh
$root = "$env:USERPROFILE\.claude\projects"
Get-ChildItem -Path $root -Filter *.jsonl -Recurse -File -ErrorAction SilentlyContinue |
  Sort-Object LastWriteTime -Descending |
  Select-Object -First 10
```

For each file extract:
- `lastActivity` (file LastWriteTime in ISO).
- `cwd`: read the first line as JSON and take its `cwd` field. If absent, derive it from the parent directory name (Claude Code encodes path separators by replacing `\` with `-` and prefixing with the drive, e.g. `C--Users-andre-repos-foo`).
- `sessionId`: filename without `.jsonl`.
- `firstPrompt`: scan lines top to bottom, take the first one whose `type == 'user'` and `message.role == 'user'`. If `message.content` is a string use it directly; if it's an array, take the first item with `type == 'text'`. Strip whitespace and trim to ~90 chars.
- `isSubAgent`: true when the filename starts with `agent-`.

Render as a numbered table, newest first. Mark sub-agent rows clearly - they cannot be resumed.

## Step 3 - deeper look at the most recent top-level session

For the most recent transcript whose name does NOT start with `agent-`, also report:
- Total line count of the JSONL (~ message turns).
- Approximate size in KiB.
- The first user prompt verbatim (no trimming).
- A one-sentence summary of the last assistant turn, drawn from the final `type == 'assistant'` line's text content.

## Step 4 - offer to resume

End with a single prompt like:

> Pick a session number to resume in a new pwsh window, or type `skip`, `continue` (most recent), or a free-form question.

When the user replies with a number, launch the chosen session in a detached visible pwsh window so the current terminal stays free:

```pwsh
Start-Process pwsh -ArgumentList @(
  '-NoExit','-NoProfile','-Command',
  "Set-Location '$cwd'; claude --dangerously-skip-permissions --resume '$sessionId'"
)
```

If the user says `continue` / `latest`, drop the `--resume` and use `--continue` instead, with `$cwd` set to the most recent top-level session's directory:

```pwsh
Start-Process pwsh -ArgumentList @(
  '-NoExit','-NoProfile','-Command',
  "Set-Location '$cwd'; claude --dangerously-skip-permissions --continue"
)
```

Refuse to resume sub-agent transcripts (`agent-*.jsonl`) - they aren't resumable on their own. If the user picks one, suggest the parent top-level session instead.

After launching, confirm the new window's PID and the working directory. Do not block; control returns to the current session.
