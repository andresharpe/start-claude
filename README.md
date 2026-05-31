# start-claude

Always-on Claude Code watchdog. A Windows Service that keeps at least one `claude --dangerously-skip-permissions` session alive on a machine, with a small Tailscale-reachable HTTP control API.

## Why

This exists for driving Claude Code remotely. When you monitor and direct your sessions from a phone, a session sometimes shuts itself down, or one session closes another by mistake. Once that happens there is nothing left running to take a command, so you wait until you are back at the laptop to start it again.

This service removes that wait. It watches for a missing `claude.exe` and relaunches a session within about a minute, so a session is almost always there for your phone to reach. The HTTP API lets you check status and spawn or kill sessions remotely, and the dashboard shows what the machine is doing without physical access.

## What it does

- Polls every 60 seconds for any `claude.exe` running from `%USERPROFILE%\.local\bin\claude.exe` (configurable via `Watchdog:ClaudeExecutablePath`).
- If none is found, triggers the `StartClaudeLauncher` Scheduled Task, which opens a visible `pwsh -NoExit` window in the repo folder and runs `claude --dangerously-skip-permissions`.
- Exposes an HTTP control API on port `19720` by default (loopback + Tailscale IPv4), configurable via `Http:Port`.

## Install

From an elevated pwsh, in the repo root:

```
.\scripts\install.ps1
```

By default the service runs as `LocalSystem` (no stored credentials) and the Scheduled Task launches claude in the current user's interactive session. The installer resolves that user's `claude.exe` path and writes it, along with the log path and HTTP port, into the deployed `appsettings.json`.

Useful switches:

- `-RunServiceAsUser` runs the Windows Service as `-ServiceUser` instead of `LocalSystem`; you are prompted for that account's password.
- `-ServiceUser DOMAIN\user` sets which user's session hosts the claude window. Defaults to the current user.
- `-ClaudeExecutablePath <path>` overrides the watched claude.exe location.
- `-HttpPort <n>` overrides the API port and writes it back into `appsettings.json` so the firewall rule matches.

## Settings

The service reads `src/StartClaude.Service/appsettings.json`. The installer copies it to the install directory and fills in machine-specific values. Keys worth knowing:

- `Watchdog:ClaudeExecutablePath` - absolute path to the watched claude.exe. Empty means resolve from the current user profile.
- `Watchdog:PollIntervalSeconds` - how often to check, default 60.
- `Http:Port` - the API and dashboard port, default 19720.
- `Logging:FilePath` - rolling log path. Empty means a `logs/` directory next to the executable.

## Uninstall

```
.\scripts\uninstall.ps1
```

## HTTP API

All endpoints unauthenticated; access is gated by Tailscale + the Windows firewall rule installed by `install.ps1`.

- `GET  /` - HTML control dashboard (status, machine vitals, screenshots, log tail)
- `GET  /healthz`
- `GET  /status` - watchdog state plus machine vitals
- `POST /spawn`
- `POST /kill-all`
- `GET  /logs/tail?lines=200`
- `POST /screenshots` - capture every connected monitor
- `GET  /screenshots/{index}` - the captured PNG for one monitor

## Secret scanning

Commits are scanned for secrets by [gitleaks](https://github.com/gitleaks/gitleaks). The hook lives in `.githooks/pre-commit` and is wired up through `core.hooksPath`, so a fresh clone needs one command to enable it:

```
git config core.hooksPath .githooks
```

The hook runs `gitleaks git --staged` and fails the commit when it finds a secret. If gitleaks is not on `PATH` the hook skips the scan rather than blocking work. Bypass deliberately with `git commit --no-verify`.

## Layout

```
.claude/                 project-level Claude Code settings (auto-compact 50%)
.githooks/               version-controlled git hooks (gitleaks pre-commit)
config/                  global settings fragment merged at install time
scripts/                 install / uninstall PowerShell scripts
src/StartClaude.Service  .NET 10 Worker Service (watchdog + HTTP API)
logs/                    runtime log files (gitignored)
```
