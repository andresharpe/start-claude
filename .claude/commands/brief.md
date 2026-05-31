---
description: Show machine vitals and recent resumable Claude Code sessions. Use when the user asks for a brief, wants to know what was running on this PC, wants situational awareness after coming back to the machine, or wants to resume a recent session.
argument-hint: [skip|continue|<session-number>]
allowed-tools: Bash(pwsh:*), Read
---

# /brief

Fast situational brief: machine vitals + the 10 most recent resumable Claude Code sessions + an optional resume.

## Step 1 - collect

Run this exact command in a single Bash tool call, with no here-string and no `-Command` wrapper:

```
pwsh -NoProfile -File scripts/brief-collect.ps1
```

(That .ps1 lives in this repo. Inline multi-line pwsh through bash heredocs is broken in the Claude Code Bash tool and we hit it on the previous version - use the file.)

The script emits a single compact JSON object:

```
{
  "vitals":   { ...watchdog /status payload, or local fallback... },
  "sessions": [ { lastActivityUtc, sessionId, cwd, path, sizeKiB, firstPrompt }, ... ]
}
```

Sub-agent transcripts (`agent-*.jsonl`) are already filtered out by the script, so every row in `sessions` is resumable. Do not re-read files, do not run helper commands - just render the JSON.

## Step 2 - render the brief

Format the JSON as plain markdown. Use this layout:

```
machine status

| metric       | value                                    |
| ------------ | ---------------------------------------- |
| watchdog     | reachable | unreachable                  |
| machine      | <vitals.machineName>                     |
| uptime       | <format uptimeSeconds as `Dd HHh MMm`>   |
| cpu          | <vitals.cpuPercent>%      (skip if null) |
| memory       | <used GiB> / <total GiB> GiB             |
| disk C:      | <used GiB> / <total GiB> GiB             |
| public IP    | <vitals.publicIp>         (skip if null) |
| claude procs | <vitals.targetClaudeCount>               |

recent sessions

| # | last activity (UTC)  | dir            | size   | first prompt |
| - | -------------------- | -------------- | ------ | ------------ |
| 1 | 2026-05-31T07:00:02Z | repos\foo      | 123KiB | ...          |
| 2 | ...                  |                |        |              |
```

Conversion rules:
- Bytes -> GiB with one decimal.
- Keep ISO 8601 UTC timestamps as-is.
- Trim cwd by stripping the leading `C:\Users\andre\` so it reads as `repos\start-claude`.
- No emojis. No em dashes. Hyphens fine.

End with one line:

> Reply with a session number to resume, `continue` for the most recent session, or `skip`.

## Step 3 - optional resume

If `$ARGUMENTS` was passed (`skip` / `continue` / `latest` / integer) OR the user replies with one:

- `skip` or empty after rendering - stop. Done.
- `continue` / `latest` - launch the first session row with `--continue`.
- integer N - launch the Nth session row with `--resume`.

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
