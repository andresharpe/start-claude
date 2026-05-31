# start-claude

This repository is the always-on Claude Code session entry point on this machine. A Windows Service (`StartClaude.Service`) watches for the absence of a `claude.exe` process from `%USERPROFILE%\.local\bin\claude.exe` (configured via `Watchdog:ClaudeExecutablePath`) and re-launches a visible pwsh window in this folder running `claude --dangerously-skip-permissions`.

The project-level `.claude/settings.json` sets the auto-compaction threshold to 50% via `CLAUDE_AUTOCOMPACT_PCT_OVERRIDE`. The same value is also merged into `~/.claude/settings.json` at install time.

If you are reading this inside a claude session, you are likely inside the watchdog-spawned session. Treat unexpected exits as something the watchdog will recover from within ~60 seconds.

## Shell guidance

This is a Windows machine, but the Claude Code Bash tool runs commands through **git bash**, not PowerShell. This trips up several common patterns:

- PowerShell cmdlets like `Select-Object`, `Get-Content`, `Measure-Object`, `Where-Object` are NOT available in the Bash tool. Piping bash output into them will fail with `command not found`.
- Inline multi-line PowerShell through a bash heredoc (`@'...'@`) or `pwsh -Command "..."` with quoted multi-line bodies often fails parsing in the Bash tool's wrapper.
- The Bash tool's working directory is the repo root, so relative paths like `scripts/foo.ps1` resolve correctly.

**Rules to follow:**

1. To run a multi-line PowerShell script, put it in a `.ps1` file in the repo and invoke it with `pwsh -NoProfile -File path/to/script.ps1`. Do not try to inline it through a heredoc.
2. For a one-off PowerShell command use `pwsh -NoProfile -Command '<single-line command>'` with single quotes around the command.
3. Do NOT mix shells in a single Bash call - either it's all bash (`git`, `cat`, `head`, `grep`, `ls`, etc.) or it's a single `pwsh ...` invocation. Never pipe bash output into a PowerShell cmdlet.
4. To inspect file contents prefer the dedicated Read tool over `cat`/`head`/`Get-Content` shell calls.
5. To search for content prefer the Grep tool over piping bash text utilities.
